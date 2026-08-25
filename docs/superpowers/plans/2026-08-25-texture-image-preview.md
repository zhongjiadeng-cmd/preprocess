# Texture Image Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在两个纹理转 Hatch 入口中显示导入图片预览、像素与真实 DPI 信息，并用像素和 DPI 自动填写仍可手动修改的目标宽高。

**Architecture:** Avalonia 负责位图预览；`texture_to_hatch_dxf.py` 新增只读 JSON 检查模式，以 Pillow 的现有 DPI 语义返回像素和真实 DPI。C# 新增独立图片信息模型与纯尺寸换算器，`MainWindow` 只负责进程调用、预览资源生命周期和控件更新。

**Tech Stack:** Python 3、Pillow、unittest、C#、.NET 10、Avalonia 11.3.18、MSTest 4.3.3

## Global Constraints

- 仅修改主流程和“Texture to Hatch”两个同时包含纹理输入与目标宽高的页面。
- 单独的“灰度图分层”页面不增加预览或目标尺寸行为。
- Python 转换算法与现有转换命令参数保持兼容；新增检查模式不得生成文件或修改输入图片。
- 图片内置 DPI 优先；备用 DPI 仅在图片缺少有效 DPI 时生效。
- 自动填写后的目标宽高允许手动修改。
- 自动写入只发生在成功导入带 DPI 的新图片，或无内置 DPI 图片的备用 DPI 发生有效变化时。
- 不添加 ImageSharp 或其他新的图像元数据依赖。
- 保留工作区中 `.workbuddy/`、`overlay_viewer/` 和现有未跟踪图片，不纳入本功能提交。

## File Structure

- Modify: `texture_to_hatch_dxf.py` — Pillow 图片信息读取函数与 `--inspect-image` JSON 模式。
- Modify: `tests/test_texture_to_hatch_dxf.py` — Python API 和 CLI 回归测试。
- Create: `GrayscaleLayersMac/TextureImageInfo.cs` — JSON 模型、DPI 判定与毫米换算。
- Create: `GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj` — 无 UI 的 .NET 测试入口。
- Create: `GrayscaleLayersMac.Tests/TextureImageInfoTests.cs` — JSON、DPI 和换算测试。
- Modify: `GrayscaleLayersMac/MainWindow.cs` — 两个预览卡片、检查进程、备用 DPI 事件与位图释放。
- Modify: `GrayscaleLayersMac/README.md` — 用户可见行为说明。

---

### Task 1: Pillow 图片信息检查命令

**Files:**
- Modify: `tests/test_texture_to_hatch_dxf.py`
- Modify: `texture_to_hatch_dxf.py`

**Interfaces:**
- Produces: `inspect_texture_image(image_path: Path) -> dict[str, int | float | None]`
- Produces: `python3 texture_to_hatch_dxf.py INPUT --inspect-image`，标准输出只包含一行 JSON，键为 `pixel_width`、`pixel_height`、`dpi_x`、`dpi_y`
- Preserves: `python3 texture_to_hatch_dxf.py INPUT OUTPUT --width ... --height ...`

- [ ] **Step 1: Write failing Python API tests**

在 `tests/test_texture_to_hatch_dxf.py` 增加 `subprocess`、`sys` 导入，导入 `inspect_texture_image`，并新增 `TextureImageInspectionTests(unittest.TestCase)` 测试类：

```python
class TextureImageInspectionTests(unittest.TestCase):
    def test_inspect_texture_image_reports_pixels_and_axis_dpi(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "anisotropic.png"
            Image.new("L", (600, 300), 255).save(path, dpi=(300, 150))
            info = inspect_texture_image(path)
            self.assertEqual((info["pixel_width"], info["pixel_height"]), (600, 300))
            self.assertAlmostEqual(info["dpi_x"], 300, delta=0.1)
            self.assertAlmostEqual(info["dpi_y"], 150, delta=0.1)

    def test_inspect_texture_image_uses_null_for_missing_dpi(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "no-dpi.png"
            Image.new("L", (40, 20), 255).save(path)
            self.assertEqual(inspect_texture_image(path), {
                "pixel_width": 40, "pixel_height": 20,
                "dpi_x": None, "dpi_y": None,
            })

    def test_inspect_texture_image_rejects_incomplete_dpi(self):
        with mock.patch("texture_to_hatch_dxf.Image.open") as open_image:
            image = open_image.return_value.__enter__.return_value
            image.size = (12, 8)
            image.info = {"dpi": (300, 0)}
            info = inspect_texture_image(Path("texture.png"))
            self.assertIsNone(info["dpi_x"])
            self.assertIsNone(info["dpi_y"])
```

- [ ] **Step 2: Run API tests and verify RED**

```bash
python3 -m unittest \
  tests.test_texture_to_hatch_dxf.TextureImageInspectionTests.test_inspect_texture_image_reports_pixels_and_axis_dpi \
  tests.test_texture_to_hatch_dxf.TextureImageInspectionTests.test_inspect_texture_image_uses_null_for_missing_dpi \
  tests.test_texture_to_hatch_dxf.TextureImageInspectionTests.test_inspect_texture_image_rejects_incomplete_dpi -v
```

Expected: import failure because `inspect_texture_image` does not exist.

- [ ] **Step 3: Implement the minimal Pillow metadata API**

在 `texture_to_hatch_dxf.py` 新增并由 `read_binary_texture` 复用：

```python
def _valid_image_dpi(value: object) -> tuple[float, float] | None:
    if not isinstance(value, (tuple, list)) or len(value) < 2:
        return None
    try:
        dpi_x, dpi_y = float(value[0]), float(value[1])
    except (TypeError, ValueError):
        return None
    if not math.isfinite(dpi_x) or not math.isfinite(dpi_y):
        return None
    return (dpi_x, dpi_y) if dpi_x > 0 and dpi_y > 0 else None

def inspect_texture_image(image_path: Path) -> dict[str, int | float | None]:
    with Image.open(image_path) as image:
        pixel_width, pixel_height = image.size
        dpi = _valid_image_dpi(image.info.get("dpi"))
    return {
        "pixel_width": int(pixel_width), "pixel_height": int(pixel_height),
        "dpi_x": dpi[0] if dpi else None,
        "dpi_y": dpi[1] if dpi else None,
    }
```

- [ ] **Step 4: Run Step 2 command and verify GREEN**

Expected: 3 tests pass.

- [ ] **Step 5: Write a failing CLI test**

```python
def test_inspect_image_cli_outputs_json_without_output_path(self):
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "source.png"
        Image.new("L", (80, 40), 255).save(path, dpi=(200, 100))
        completed = subprocess.run(
            [sys.executable, str(ROOT / "texture_to_hatch_dxf.py"),
             str(path), "--inspect-image"],
            check=False, capture_output=True, text=True,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        payload = json.loads(completed.stdout)
        self.assertEqual((payload["pixel_width"], payload["pixel_height"]), (80, 40))
        self.assertAlmostEqual(payload["dpi_x"], 200, delta=0.1)
        self.assertAlmostEqual(payload["dpi_y"], 100, delta=0.1)
```

- [ ] **Step 6: Run CLI test and verify RED**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.TextureImageInspectionTests.test_inspect_image_cli_outputs_json_without_output_path -v
```

Expected: FAIL because output and target size are still required.

- [ ] **Step 7: Add the inspection CLI branch**

`output` 改为 `nargs="?"`，新增 `--inspect-image`。检查模式只验证 input；转换模式继续验证 output、尺寸、blocks 和面积参数。

```python
parser.add_argument("output", type=Path, nargs="?", help="输出 DXF")
parser.add_argument("--inspect-image", action="store_true",
                    help="以 JSON 输出图片像素和 DPI 信息后退出")
```

`main()` 在转换前执行：

```python
if args.inspect_image:
    print(json.dumps(inspect_texture_image(args.input), ensure_ascii=False))
    return
```

- [ ] **Step 8: Run targeted and full Python tests**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.TextureImageInspectionTests.test_inspect_image_cli_outputs_json_without_output_path -v
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

Expected: zero failures and zero errors.

- [ ] **Step 9: Commit Task 1**

```bash
git add texture_to_hatch_dxf.py tests/test_texture_to_hatch_dxf.py
git commit -m "feat: expose texture image metadata"
```

---

### Task 2: C# 图片信息模型与尺寸换算

**Files:**
- Create: `GrayscaleLayersMac/TextureImageInfo.cs`
- Create: `GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj`
- Create: `GrayscaleLayersMac.Tests/TextureImageInfoTests.cs`

**Interfaces:**
- Consumes: Task 1 JSON keys `pixel_width`、`pixel_height`、`dpi_x`、`dpi_y`
- Produces: `public sealed record TextureImageInfo(int PixelWidth, int PixelHeight, double? DpiX, double? DpiY)`
- Produces: `HasEmbeddedDpi`、`ParseJson(string)`、`TryCalculateMillimeters(...)`

- [ ] **Step 1: Create the test project and write failing tests**

`GrayscaleLayersMac.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MSTest" Version="4.3.3" />
    <ProjectReference Include="../GrayscaleLayersMac/GrayscaleLayersMac.csproj" />
  </ItemGroup>
</Project>
```

`TextureImageInfoTests.cs` 至少包含：

```csharp
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class TextureImageInfoTests
{
    [TestMethod]
    public void ParseJsonAndCalculate_UsesAxisDpi()
    {
        var info = TextureImageInfo.ParseJson(
            """{"pixel_width":600,"pixel_height":300,"dpi_x":300,"dpi_y":150}""");
        var ok = info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out var width, out var height, out var error);
        Assert.IsTrue(ok, error);
        Assert.AreEqual(50.8m, width);
        Assert.AreEqual(50.8m, height);
    }

    [TestMethod]
    public void Calculate_MissingDpiNeedsFallback()
    {
        var info = TextureImageInfo.ParseJson(
            """{"pixel_width":100,"pixel_height":50,"dpi_x":null,"dpi_y":null}""");
        Assert.IsFalse(info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out _, out _, out _));
        Assert.IsTrue(info.TryCalculateMillimeters(
            100, 0.01m, 100000m, out var width, out var height, out _));
        Assert.AreEqual(25.4m, width);
        Assert.AreEqual(12.7m, height);
    }

    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void Calculate_RejectsInvalidFallback(double dpi)
    {
        var info = new TextureImageInfo(100, 50, null, null);
        Assert.IsFalse(info.TryCalculateMillimeters(
            dpi, 0.01m, 100000m, out _, out _, out _));
    }

    [TestMethod]
    public void Calculate_RejectsResultOutsideControlRange()
    {
        var info = new TextureImageInfo(1_000_000, 1_000_000, 1, 1);
        Assert.IsFalse(info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out _, out _, out var error));
        StringAssert.Contains(error, "允许范围");
    }
}
```

- [ ] **Step 2: Run .NET tests and verify RED**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -v minimal
```

Expected: compile failure because `TextureImageInfo` does not exist.

- [ ] **Step 3: Implement the minimal model**

创建 `TextureImageInfo.cs`，使用 `[JsonPropertyName]` 映射 snake_case。`ParseJson` 调用 `JsonSerializer.Deserialize<TextureImageInfo>`，拒绝空结果、非正像素尺寸、只有单轴 DPI，以及非有限或非正内置 DPI。

`TryCalculateMillimeters` 的 DPI 选择顺序：

```csharp
var dpiX = HasEmbeddedDpi ? DpiX!.Value : fallbackDpi;
var dpiY = HasEmbeddedDpi ? DpiY!.Value : fallbackDpi;
```

验证 DPI 后按 `pixels / dpi * 25.4` 换算，以 `decimal.Round(value, 3, MidpointRounding.AwayFromZero)` 对齐现有控件精度，并在写入 out 参数前验证结果位于 `[minimum, maximum]`。

- [ ] **Step 4: Run .NET tests and verify GREEN**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -v minimal
```

Expected: all model tests pass.

- [ ] **Step 5: Commit Task 2**

```bash
git add GrayscaleLayersMac/TextureImageInfo.cs GrayscaleLayersMac.Tests
git commit -m "test: define texture size calculation"
```

---

### Task 3: Avalonia 纹理预览卡片与自动更新接线

**Files:**
- Modify: `GrayscaleLayersMac/TextureImageInfo.cs`
- Modify: `GrayscaleLayersMac.Tests/TextureImageInfoTests.cs`
- Modify: `GrayscaleLayersMac/MainWindow.cs`
- Modify: `GrayscaleLayersMac/UiTheme.cs` only if a reusable non-expandable card factory is needed

**Interfaces:**
- Consumes: Task 1 `--inspect-image` JSON command
- Consumes: Task 2 `TextureImageInfo.ParseJson` and `TryCalculateMillimeters`
- Produces: `InspectTextureImageAsync(string path) -> Task<TextureImageInfo>`
- Produces: shared preview loading and automatic-size helpers used by both pages

- [ ] **Step 1: Add failing metadata-format tests**

```csharp
[TestMethod]
public void FormatSummary_ShowsPixelsAxisDpiAndPhysicalSize()
{
    var info = new TextureImageInfo(600, 300, 300, 150);
    Assert.AreEqual("像素：600 × 300 px\nDPI：300 × 150", info.FormatMetadata());
    Assert.AreEqual("物理尺寸：50.8 × 50.8 mm",
        info.FormatPhysicalSize(50.8m, 50.8m));
}

[TestMethod]
public void FormatSummary_ExplainsMissingDpi()
{
    var info = new TextureImageInfo(40, 20, null, null);
    Assert.AreEqual("像素：40 × 20 px\nDPI：未提供", info.FormatMetadata());
}
```

- [ ] **Step 2: Run tests and verify RED**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -v minimal
```

Expected: compile failure because formatting methods do not exist.

- [ ] **Step 3: Implement deterministic formatting and verify GREEN**

在 `TextureImageInfo` 增加 `FormatMetadata()` 和 `FormatPhysicalSize(decimal, decimal)`。数字使用 invariant `0.###` 格式。再次运行 Step 2，Expected: all tests pass.

- [ ] **Step 4: Add page fields and reusable preview card**

在 `MainWindow` 增加两组 `Image`、元数据 `TextBlock`、物理尺寸 `TextBlock` 与 `TextureImageInfo?` 字段。图片控件基线：

```csharp
new Image
{
    Height = 190,
    Stretch = Stretch.Uniform,
    HorizontalAlignment = HorizontalAlignment.Stretch
}
```

新增 `MakeTexturePreviewCard(Image, TextBlock, TextBlock)`，沿用现有深色卡片、细边框和 sunken 预览背景，标题为“纹理预览”。将卡片放在两个页面各自输入字段之后、Hatch 参数之前。

- [ ] **Step 5: Implement the inspection process boundary**

`InspectTextureImageAsync`：

1. 调用现有 `FindPythonAsync()`；找不到时抛出已有中文安装提示。
2. 使用 `CreatePythonProcess(python)`。
3. 参数为脚本路径、图片路径、`--inspect-image`。
4. 并行读取 stdout 与 stderr，等待进程退出。
5. 非零退出码抛出包含 stderr 的 `InvalidOperationException`。
6. 将 stdout 交给 `TextureImageInfo.ParseJson`。

JSON 不写入运行日志，也不创建临时元数据文件。

- [ ] **Step 6: Implement shared preview loading and cleanup**

共享加载逻辑的顺序：

1. `Image.Source = null` 后释放旧 `Bitmap`，清空旧模型。
2. 状态显示“正在读取图片信息…”。
3. 使用 `Bitmap.DecodeToHeight(stream, 380)` 创建有限尺寸预览，避免超大纹理完整解码占用 UI 内存。
4. await `InspectTextureImageAsync(path)`。
5. 成功后设置预览、模型与元数据文本，并调用自动尺寸更新。
6. 失败时释放新位图，显示“无法读取图片：{message}”，不修改目标宽高。

窗口关闭时释放两个预览位图。

- [ ] **Step 7: Wire both pickers and fallback DPI events**

`PickHatchInputAsync` 和 `PickPipelineInputAsync` 在设置路径与默认输出位置后 await 共享加载方法。

为 `_dpiBox.TextChanged` 与 `_pipelineDpiBox.TextChanged` 注册事件：

- 仅当对应模型存在且 `HasEmbeddedDpi == false` 时解析备用 DPI。
- 有效时调用 `TryCalculateMillimeters`，更新宽高与物理尺寸文本。
- 无效时显示“物理尺寸：等待填写有效 DPI”，不得覆盖宽高。
- 图片带内置 DPI 时，DPI 文本框变化不得自动改写宽高。
- 重新选择图片时允许再次自动写入。

- [ ] **Step 8: Run .NET verification**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -c Release -v minimal
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
```

Expected: tests pass and Release build exits 0.

- [ ] **Step 9: Manually verify both pages**

```bash
dotnet run --project GrayscaleLayersMac/GrayscaleLayersMac.csproj
```

检查：带 DPI、X/Y DPI 不同、无 DPI 后补填、手动修改不被无关操作覆盖、重新选图重新自动填写、损坏图片不退出、连续换图正确替换预览。

- [ ] **Step 10: Commit Task 3**

```bash
git add GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac/TextureImageInfo.cs GrayscaleLayersMac.Tests/TextureImageInfoTests.cs
git commit -m "feat: preview imported texture images"
```

若 `UiTheme.cs` 实际修改，将它一并添加；否则不加入提交。

---

### Task 4: 用户文档与最终验证

**Files:**
- Modify: `GrayscaleLayersMac/README.md`

**Interfaces:**
- Documents: 两个页面的预览、图片信息、自动尺寸和备用 DPI 行为
- Verifies: Python API、C# 模型、Avalonia 构建与工作区改动范围

- [ ] **Step 1: Update README**

在主流程说明后记录：两个纹理转 Hatch 页面选图后显示等比例预览、像素与 DPI；目标毫米尺寸按 `像素 ÷ DPI × 25.4` 自动填写并可手动覆盖；无内置 DPI 时填写备用 DPI 后即时换算。

- [ ] **Step 2: Run fresh full verification**

```bash
python3 -m unittest discover -s tests -p 'test_*.py' -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -c Release -v minimal
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
git diff --check
git status --short
```

Expected: Python 与 .NET 测试零失败/零错误；Release 构建退出 0；`git diff --check` 无输出；status 只包含本功能文件和预先存在的未跟踪文件。

- [ ] **Step 3: Review acceptance criteria against the diff**

逐项核对 `docs/superpowers/specs/2026-08-25-texture-image-preview-design.md`。重点确认没有 ImageSharp 依赖、现有 Python 转换调用不变、两个页面复用同一帮助路径、失败导入不会覆盖宽高。

- [ ] **Step 4: Commit Task 4**

```bash
git add GrayscaleLayersMac/README.md
git commit -m "docs: explain texture image preview"
```
