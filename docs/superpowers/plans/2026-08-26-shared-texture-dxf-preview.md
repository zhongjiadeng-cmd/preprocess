# TIFF-Compatible Shared Texture/DXF Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preview Pillow-supported TIFF/PNG textures and DXF files in one right-side tabbed viewport on both texture workflows while preserving editable DPI-based target sizing.

**Architecture:** Extend the existing Pillow inspection command to return a bounded Base64 PNG preview with its metadata, then validate that payload in a focused C# model before Avalonia displays it. Keep texture and DXF state independent; a small selection model and page-level container decide which content surface is visible.

**Tech Stack:** Python 3, Pillow, `unittest`, C#/.NET 10, Avalonia 11, MSTest.

## Global Constraints

- Cover the main pipeline and independent “纹理转 Hatch DXF” page.
- Do not modify source images, conversion algorithms, DXF content, or machine-file format.
- Generate previews in memory only; create no temporary preview files.
- Keep both tabs visible. Texture import selects texture; successful DXF generation/import selects DXF; manual switching preserves both.
- Keep target width/height editable and retain the existing automatic-write triggers only.
- Limit preview longest edge to 380 px and reject decoded preview data over 4 MiB.
- Preserve unrelated user files and the existing untracked paths.

---

## File Structure

- `texture_to_hatch_dxf.py`: optional Pillow PNG preview output.
- `tests/test_texture_to_hatch_dxf.py`: TIFF/PNG preview contract tests.
- `GrayscaleLayersMac/TextureImageInspection.cs`: combined JSON payload parser.
- `GrayscaleLayersMac.Tests/TextureImageInspectionTests.cs`: parser validation tests.
- `GrayscaleLayersMac/SharedPreviewSelection.cs`: control-independent selection state.
- `GrayscaleLayersMac.Tests/SharedPreviewSelectionTests.cs`: switching tests.
- `GrayscaleLayersMac/MainWindow.cs`: shared viewport and event wiring.
- `GrayscaleLayersMac/TexturePreviewController.cs`: actionable bounded failures.
- `GrayscaleLayersMac/README.md`: user-facing behavior.

---

### Task 1: Produce a bounded PNG preview with Pillow

**Files:**
- Modify: `texture_to_hatch_dxf.py:1-20,964-1010,2120-2260`
- Test: `tests/test_texture_to_hatch_dxf.py:1-190`

**Interfaces:**
- Consumes: `PIL.Image.open(Path)` and `_valid_image_dpi(value)`.
- Produces: `inspect_texture_image(image_path: Path, preview_max_edge: int | None = None) -> dict[str, object]`; optional JSON field `preview_png_base64`; CLI option `--preview-max-edge` valid only with `--inspect-image`.

- [ ] **Step 1: Write failing TIFF and aspect-ratio tests**

```python
def test_inspect_texture_image_embeds_bounded_tiff_preview(self):
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "texture.tif"
        Image.new("L", (1500, 1500), 128).save(path, dpi=(1270, 1270))
        payload = inspect_texture_image(path, preview_max_edge=380)
        raw = base64.b64decode(payload["preview_png_base64"], validate=True)
        with Image.open(io.BytesIO(raw)) as preview:
            self.assertEqual((preview.format, preview.size), ("PNG", (380, 380)))
        self.assertEqual((payload["pixel_width"], payload["pixel_height"]), (1500, 1500))
        self.assertAlmostEqual(payload["dpi_x"], 1270, delta=0.1)

def test_preview_preserves_aspect_ratio(self):
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "wide.png"
        Image.new("RGB", (800, 200), "white").save(path)
        payload = inspect_texture_image(path, preview_max_edge=380)
        with Image.open(io.BytesIO(base64.b64decode(payload["preview_png_base64"]))) as preview:
            self.assertEqual(preview.size, (380, 95))
```

- [ ] **Step 2: Run the tests and verify they fail because the parameter is missing**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.TextureImageInspectionTests -v
```

- [ ] **Step 3: Implement validation and PNG encoding**

```python
MAX_PREVIEW_EDGE = 4096

def _validate_preview_max_edge(value: object | None) -> int | None:
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, int) or not 1 <= value <= MAX_PREVIEW_EDGE:
        raise ValueError(f"预览最大边必须是 1 到 {MAX_PREVIEW_EDGE} 之间的整数。")
    return value

def _encode_preview_png(image: Image.Image, max_edge: int) -> str:
    preview = image.copy()
    preview.thumbnail((max_edge, max_edge), Image.Resampling.LANCZOS)
    if preview.mode not in ("L", "LA", "RGB", "RGBA"):
        preview = preview.convert("RGBA")
    output = io.BytesIO()
    preview.save(output, format="PNG", optimize=True)
    return base64.b64encode(output.getvalue()).decode("ascii")
```

Update `inspect_texture_image` so calls without `preview_max_edge` return the original four keys unchanged; when supplied, add `preview_png_base64` inside the same `Image.open` context.

- [ ] **Step 4: Add CLI tests and option wiring**

```python
def test_inspect_image_cli_includes_preview_when_requested(self):
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "source.tif"
        Image.new("L", (1500, 1500), 255).save(path, dpi=(1270, 1270))
        completed = subprocess.run(
            [sys.executable, str(ROOT / "texture_to_hatch_dxf.py"), str(path),
             "--inspect-image", "--preview-max-edge", "380"],
            check=False, capture_output=True, text=True)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        self.assertIn("preview_png_base64", json.loads(completed.stdout))
```

Add `parser.add_argument("--preview-max-edge", type=int)`; reject it unless `args.inspect_image`; pass it to `inspect_texture_image` in `main()`.

- [ ] **Step 5: Run the focused suite and commit**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.TextureImageInspectionTests -v
git add texture_to_hatch_dxf.py tests/test_texture_to_hatch_dxf.py
git commit -m "feat: emit bounded texture preview png"
```

---

### Task 2: Parse and bound preview data in C#

**Files:**
- Create: `GrayscaleLayersMac/TextureImageInspection.cs`
- Create: `GrayscaleLayersMac.Tests/TextureImageInspectionTests.cs`

**Interfaces:**
- Consumes: Task 1 JSON and `TextureImageInfo.ParseJson(string)`.
- Produces: `TextureImageInspection.ParseJson(string, int)`, `Info`, and `PreviewPng`.

- [ ] **Step 1: Write failing parser tests**

```csharp
private const string OnePixelPng =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

[TestMethod]
public void ParseJson_ReturnsMetadataAndPngBytes()
{
    var value = TextureImageInspection.ParseJson(
        $$"""{"pixel_width":1500,"pixel_height":1500,"dpi_x":1270,"dpi_y":1270,"preview_png_base64":"{{OnePixelPng}}"}""");
    Assert.AreEqual(1500, value.Info.PixelWidth);
    CollectionAssert.AreEqual(new byte[] {137,80,78,71,13,10,26,10}, value.PreviewPng[..8]);
}

[TestMethod]
public void ParseJson_RejectsInvalidOrOversizedPreview()
{
    var invalid = """{"pixel_width":1,"pixel_height":1,"dpi_x":null,"dpi_y":null,"preview_png_base64":"bad"}""";
    Assert.ThrowsExactly<ArgumentException>(() => TextureImageInspection.ParseJson(invalid));
    var valid = $$"""{"pixel_width":1,"pixel_height":1,"dpi_x":null,"dpi_y":null,"preview_png_base64":"{{OnePixelPng}}"}""";
    Assert.ThrowsExactly<ArgumentException>(() => TextureImageInspection.ParseJson(valid, 8));
}
```

- [ ] **Step 2: Run and verify the missing-type compile failure**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~TextureImageInspectionTests --no-restore
```

- [ ] **Step 3: Implement the parser**

```csharp
public sealed record TextureImageInspection(TextureImageInfo Info, byte[] PreviewPng)
{
    public const int DefaultMaximumPreviewBytes = 4 * 1024 * 1024;
    private static ReadOnlySpan<byte> PngSignature => [137,80,78,71,13,10,26,10];

    public static TextureImageInspection ParseJson(string json, int maximumPreviewBytes = DefaultMaximumPreviewBytes)
    {
        var info = TextureImageInfo.ParseJson(json);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("preview_png_base64", out var element) ||
            element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            throw new ArgumentException("图片预览数据缺失。", nameof(json));
        byte[] bytes;
        try { bytes = Convert.FromBase64String(element.GetString()!); }
        catch (FormatException error) { throw new ArgumentException("图片预览数据不是有效 Base64。", nameof(json), error); }
        if (bytes.Length > maximumPreviewBytes)
            throw new ArgumentException("图片预览数据过大。", nameof(json));
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature))
            throw new ArgumentException("图片预览数据不是有效 PNG。", nameof(json));
        return new TextureImageInspection(info, bytes);
    }
}
```

Also validate `maximumPreviewBytes >= 8` with `ArgumentOutOfRangeException`.

- [ ] **Step 4: Run tests and commit**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~TextureImageInspectionTests --no-restore
git add GrayscaleLayersMac/TextureImageInspection.cs GrayscaleLayersMac.Tests/TextureImageInspectionTests.cs
git commit -m "feat: parse texture preview payload"
```

---

### Task 3: Model shared selection independently of UI controls

**Files:**
- Create: `GrayscaleLayersMac/SharedPreviewSelection.cs`
- Create: `GrayscaleLayersMac.Tests/SharedPreviewSelectionTests.cs`

**Interfaces:**
- Produces: `SharedPreviewKind`; `SharedPreviewSelection.Current`, `HasTexture`, `HasDxf`, `BeginTextureImport`, `CompleteTextureImport`, `FailTextureImport`, `CompleteDxfLoad`, `ClearDxf`, and `Select`.

- [ ] **Step 1: Write failing switching tests**

```csharp
[TestMethod]
public void AutomaticAndManualSwitchingPreservesBothContents()
{
    var state = new SharedPreviewSelection();
    state.BeginTextureImport(); state.CompleteTextureImport();
    Assert.AreEqual(SharedPreviewKind.Texture, state.Current);
    state.CompleteDxfLoad();
    Assert.AreEqual(SharedPreviewKind.Dxf, state.Current);
    state.Select(SharedPreviewKind.Texture);
    Assert.IsTrue(state.HasTexture && state.HasDxf);
}

[TestMethod]
public void FailedTextureRemainsSelectedWithoutDiscardingDxf()
{
    var state = new SharedPreviewSelection();
    state.CompleteDxfLoad(); state.BeginTextureImport(); state.FailTextureImport();
    Assert.AreEqual(SharedPreviewKind.Texture, state.Current);
    Assert.IsFalse(state.HasTexture);
    Assert.IsTrue(state.HasDxf);
}
```

- [ ] **Step 2: Run and verify the missing-type failure**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~SharedPreviewSelectionTests --no-restore
```

- [ ] **Step 3: Implement the state model**

```csharp
public enum SharedPreviewKind { Texture, Dxf }

public sealed class SharedPreviewSelection
{
    public SharedPreviewKind Current { get; private set; } = SharedPreviewKind.Texture;
    public bool HasTexture { get; private set; }
    public bool HasDxf { get; private set; }
    public void BeginTextureImport() { HasTexture = false; Current = SharedPreviewKind.Texture; }
    public void CompleteTextureImport() { HasTexture = true; Current = SharedPreviewKind.Texture; }
    public void FailTextureImport() { HasTexture = false; Current = SharedPreviewKind.Texture; }
    public void CompleteDxfLoad() { HasDxf = true; Current = SharedPreviewKind.Dxf; }
    public void ClearDxf() => HasDxf = false;
    public void Select(SharedPreviewKind kind) => Current = kind;
}
```

- [ ] **Step 4: Run tests and commit**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter "FullyQualifiedName~SharedPreviewSelectionTests|FullyQualifiedName~TexturePreviewControllerTests" --no-restore
git add GrayscaleLayersMac/SharedPreviewSelection.cs GrayscaleLayersMac.Tests/SharedPreviewSelectionTests.cs
git commit -m "feat: model shared preview selection"
```

---

### Task 4: Integrate the Pillow preview and shared viewport

**Files:**
- Modify: `GrayscaleLayersMac/MainWindow.cs`
- Modify: `GrayscaleLayersMac/TexturePreviewController.cs`
- Test: `GrayscaleLayersMac.Tests/TexturePreviewControllerTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3 and existing `DxfPreviewControl`.
- Produces: one `SharedPreviewView` per page and `LoadDxfPreview(...) -> bool`.

- [ ] **Step 1: Change the failure test to require an actionable bounded message**

```csharp
Assert.IsTrue(controller.TryFail(request,
    new InvalidOperationException("图片预览数据不是有效 PNG。")));
Assert.AreEqual("无法读取图片：图片预览数据不是有效 PNG。", controller.State.MetadataText);
Assert.IsTrue(controller.State.MetadataText.Length <= 120);
```

- [ ] **Step 2: Run it and verify failure on the generic summary**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FailedImport --no-restore
```

- [ ] **Step 3: Sanitize the first exception line and cap it at 100 characters**

Implement `FormatFailureSummary(Exception)` in `TexturePreviewController`; remove control characters, exclude subsequent traceback lines, append `…` after truncation, and store `无法读取图片：{safe}`. Keep the full exception in `Trace.TraceError`.

- [ ] **Step 4: Decode only Pillow's PNG payload**

Make `InspectTextureImageAsync` pass `--preview-max-edge 380` and return `TextureImageInspection`. Replace `Bitmap.DecodeToWidth/Height(File.OpenRead(path))` with:

```csharp
var inspection = await InspectTextureImageAsync(path, operation.CancellationToken);
using var stream = new MemoryStream(inspection.PreviewPng, writable: false);
candidateBitmap = new Bitmap(stream);
if (Math.Max(candidateBitmap.PixelSize.Width, candidateBitmap.PixelSize.Height) > 380)
    throw new InvalidOperationException("图片预览尺寸超过 380 px 限制。");
```

Pass `inspection.Info` to `TryCompleteImport`. Delete the obsolete `TexturePreviewDecodePolicy` types and test.

- [ ] **Step 5: Build a shared right-side panel**

Add:

```csharp
private sealed record SharedPreviewView(
    ToggleButton TextureTab,
    ToggleButton DxfTab,
    Control TextureContent,
    Control DxfContent,
    SharedPreviewSelection Selection);
```

Refactor `MakeDxfPreviewPanel` into reusable DXF content, create texture content using the existing `Image`, metadata, and physical-size controls, and wrap both with a single “实际预览” title and two top tabs. Remove both inspector `MakeTexturePreviewCard` calls and the fixed 190 px image height.

Use one renderer:

```csharp
private static void SelectSharedPreview(SharedPreviewView view, SharedPreviewKind kind)
{
    view.Selection.Select(kind);
    view.TextureContent.IsVisible = kind == SharedPreviewKind.Texture;
    view.DxfContent.IsVisible = kind == SharedPreviewKind.Dxf;
    view.TextureTab.IsChecked = kind == SharedPreviewKind.Texture;
    view.DxfTab.IsChecked = kind == SharedPreviewKind.Dxf;
}
```

- [ ] **Step 6: Wire automatic selection**

On texture begin/success/failure update the matching selection model and select texture. Change `LoadDxfPreview` to return `true` only when loading succeeds; hatch generation/import and pipeline selector changes call `CompleteDxfLoad()` and select DXF only after `true`. When existing pipeline logic clears DXF, call `ClearDxf()` without discarding the texture bitmap.

- [ ] **Step 7: Run C# tests and Release compile, then commit**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
git add GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac/TexturePreviewController.cs GrayscaleLayersMac.Tests/TexturePreviewControllerTests.cs
git commit -m "feat: share texture and dxf preview viewport"
```

---

### Task 5: Document and verify the complete feature

**Files:**
- Modify: `GrayscaleLayersMac/README.md:10-20`
- Verify: `30X30-40C-240u-FK.tif`

**Interfaces:**
- Produces: documentation, full regression evidence, and rebuilt files under `GrayscaleLayersMac/bin/Release/net10.0/`.

- [ ] **Step 1: Document TIFF compatibility and the shared tabs**

State that Pillow-supported TIFF/PNG/JPEG/BMP use the same in-memory preview path; image and DXF share the right-side preview; imports auto-select their content; manual tab switching preserves both.

- [ ] **Step 2: Run all tests**

```bash
python3 -m unittest discover -s tests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -c Release --no-restore
```

Expected: zero failures.

- [ ] **Step 3: Rebuild the actual Release program**

```bash
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
```

Confirm updated timestamps for `GrayscaleLayersMac`, `GrayscaleLayersMac.dll`, and `texture_to_hatch_dxf.py` under `bin/Release/net10.0/`.

- [ ] **Step 4: Verify the supplied TIFF through the built script**

Run the built script with `30X30-40C-240u-FK.tif --inspect-image --preview-max-edge 380`; parse JSON and assert 1500 × 1500 px, DPI within 0.1 of 1270, PNG signature, and 380 × 380 decoded preview.

- [ ] **Step 5: Smoke-check both pages**

Import the supplied TIFF, confirm preview plus `1500 × 1500 px`, `1270 × 1270 DPI`, and `30 × 30 mm`; confirm width/height remain editable. Generate or import DXF, verify automatic DXF selection, then click both tabs and confirm both previews remain in the same viewport.

- [ ] **Step 6: Commit docs and inspect final state**

```bash
git add GrayscaleLayersMac/README.md
git commit -m "docs: explain shared texture and dxf preview"
git status --short
git log -6 --oneline
```

Expected: only the user's pre-existing untracked paths remain outside the committed implementation.
