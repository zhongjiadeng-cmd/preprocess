# 打包应用脚本路径修复设计

## 背景

macOS 应用包会把 `grayscale_layers.py`、`texture_to_hatch_dxf.py` 和
`dxf_to_machine_file.py` 放入 `Contents/Resources/scripts`。多数执行路径已经通过
`ApplicationLayout.GetScriptPath` 同时兼容开发输出目录和 `.app` 布局，但图片检查路径
仍直接拼接 `AppContext.BaseDirectory`，导致打包应用导入 TIFF 时错误地访问
`Contents/MacOS/texture_to_hatch_dxf.py`。

用户提供的 `/Users/ccc/Desktop/preprocess/1` 已验证包含 10 个有效的 1500×1500 灰度
LZW TIFF，命名均符合 `layer_*.tiff`。错误来自应用脚本定位，而不是输入文件。

## 方案

在 `InspectTextureImageAsync` 中使用：

```csharp
ApplicationLayout.GetScriptPath(
    AppContext.BaseDirectory,
    "texture_to_hatch_dxf.py")
```

将解析结果传给 Python 进程。保持打包脚本位置、导入规则、预览行为和错误提示结构不变。

## 数据流

1. 用户选择分层 TIFF 文件夹。
2. 导入逻辑发现并排序 `layer_*.tiff`。
3. 图片检查通过 `ApplicationLayout` 解析脚本位置。
4. 开发环境从应用输出目录执行脚本；`.app` 环境从
   `Contents/Resources/scripts` 执行脚本。
5. 所有文件检查通过后，提交目录状态并刷新分层预览。

## 错误处理

- 输入文件缺失、为空或无法解码时，继续显示现有的具体文件名和底层错误。
- 脚本缺失时，由 Python 进程返回的错误仍会被包装为导入失败提示。
- 不复制、不移动、不修改用户的 TIFF 文件。

## 测试与验收

- 增加源代码回归断言，禁止图片检查路径再次直接拼接脚本文件名。
- 保留并运行 `ApplicationLayout` 对开发目录和 `.app` 目录的单元测试。
- 运行全部 Python 与 .NET 测试。
- 重新构建 macOS 应用包并确认脚本位于 `Contents/Resources/scripts`。
- 用 `/Users/ccc/Desktop/preprocess/1` 中的 TIFF 执行打包脚本检查，确认图片解析成功。

## 非目标

- 不改变 TIFF 命名约定。
- 不增加新的文件格式。
- 不调整“导入”按钮或流程界面。
- 不改变 Python 运行时发现逻辑。
