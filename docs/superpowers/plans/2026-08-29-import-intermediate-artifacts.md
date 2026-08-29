# 导入中间产物实施计划

**目标：** 在三步流程页面增加统一的“导入”菜单，支持导入分层 TIFF 文件夹后执行第 2 步，以及导入 DXF 文件夹后执行第 3 步。

## 1. 建立可复用的产物发现规则

- 新增 `GrayscaleLayersMac/PipelineArtifactDiscovery.cs`，集中发现并排序 `layer_*.tiff` 与 `*.dxf`。
- 在发现阶段拒绝不存在的目录、空目录、目录/符号链接文件和空文件，并返回清晰错误。
- 新增 `GrayscaleLayersMac.Tests/PipelineArtifactDiscoveryTests.cs`，先覆盖有效排序、错误扩展名、空文件、符号链接和缺失目录。
- 让第 2、3 步的现有前置扫描复用该规则，避免导入和执行使用不同契约。

## 2. 固化界面入口契约

- 更新 `tests/test_pipeline_independent_steps.py`，断言页面级按钮文案严格为“导入”、菜单包含两个导入类型、按钮位于页面说明与首张阶段卡片之间。
- 修改 `GrayscaleLayersMac/MainWindow.cs`，添加 `_pipelineImportButton` 和向下展开的 Flyout。
- 使用 `UiTheme.ApplyGhostStyle`，保持 34px 次级按钮样式；流程运行或导入进行中时禁用。
- 将路径标签调整为“分层 TIFF 目录”和“DXF 目录”。

## 3. 实现分层 TIFF 文件夹导入

- 文件夹选择取消时直接返回，不修改状态。
- 使用产物发现规则获得候选文件，再用现有图像检查器逐个解码；全部成功后才提交路径。
- 调用现有 `RefreshPipelineLayersAsync` 加载缩略图与预览，切换到纹理页并写入导入日志。
- 失败时显示具体文件和原因，保留先前路径与预览。

## 4. 实现 DXF 文件夹导入

- 文件夹选择取消时直接返回，不修改状态。
- 使用产物发现规则获得候选文件，再用现有 `DxfPreviewControl.LoadFile` 逐个解析并验证可选块元数据。
- 全部成功后提交目录，原子替换 DXF 列表，选中第一层并切换到 DXF 页，同时写入日志。
- 导入条目使用现有无纹理配准模型，不推断纹理叠加。

## 5. 文档与完整验证

- 更新 `GrayscaleLayersMac/README.md`，说明重新打开应用后如何从中间产物继续。
- 运行目标 Python 测试、完整 Python 测试、.NET 测试、`dotnet build` 和 macOS 应用包验证。
- 启动应用做视觉与键盘检查，确认按钮位置、Flyout 方向、菜单文案、禁用状态和窄面板布局。
- 执行 `git diff --check` 并审查变更范围。
