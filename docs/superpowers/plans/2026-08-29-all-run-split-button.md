# “全部执行”上拉分割按钮实施计划

**目标：** 将“三步流程”的两个启动按钮合并为原生 `SplitButton`，保留完整流程主操作，并从右侧小按钮向上展开三个单步入口。

## 1. 固化界面契约

- 修改 `tests/test_pipeline_independent_steps.py`，用静态断言描述 `SplitButton`、`PlacementMode.TopEdgeAlignedLeft`、三个单步模式以及旧“单步执行”按钮被移除。
- 先运行该测试，确认新断言在实现前失败。

## 2. 增加统一主题

- 修改 `GrayscaleLayersMac/UiTheme.cs`，增加 `SplitButton` 的强调色样式和尺寸配置入口。
- 沿用现有蓝色设计令牌、44px 高度、15px 字号和半粗字重，不引入新的颜色常量。
- 覆盖原生模板使用的按钮资源，使主区、箭头区、悬停、按压和禁用状态保持一个连续控件的视觉语义。

## 3. 替换运行入口

- 修改 `GrayscaleLayersMac/MainWindow.cs`：用 `_pipelineRunSplitButton` 替换 `_pipelineRunButton` 与 `_pipelineSingleStepButton`。
- 主区 `Click` 继续调用 `RunPipelineAsync(PipelineRunMode.All)`。
- Flyout 使用 `PlacementMode.TopEdgeAlignedLeft`，内容沿用三个现有单步按钮和对应枚举。
- 运行期间只需禁用一个 `SplitButton`；保留取消按钮和现有 `try/finally` 恢复逻辑。
- 调整底部布局，让分割按钮位于原“全部执行”位置，右侧继续保留“打开加工文件目录”。

## 4. 文档与验证

- 更新 `GrayscaleLayersMac/README.md`，说明完整流程主按钮和右侧上拉单步菜单。
- 运行目标 Python 测试、完整 Python 测试、.NET 测试和 `dotnet build`。
- 执行 `git diff --check` 并检查变更范围只覆盖设计、计划、主题、窗口、测试与 README。
