# 桌面工作台原布局 UI 精修实施计划

**目标：** 在不改变现有操作路径、参数顺序和业务逻辑的前提下，明显重做下拉框、按钮、输入框、菜单、参数卡片、预览工具和小型图标，并完整覆盖浅色、深色、键盘、VoiceOver 与减少动态效果。

**设计规范：** `docs/superpowers/specs/2026-08-29-desktop-ui-dual-theme-design.md`

**技术栈：** .NET 10、Avalonia 11.3.18、Avalonia Fluent Theme、MSTest、Python unittest、macOS arm64 应用包脚本。

**实现原则：** 每个阶段先写失败测试，再做最小实现；每完成一个可见区域立即运行应用并保存同尺寸截图。业务事件处理和管线逻辑不与样式重构混在同一提交中。

## 0. 建立基线与变更护栏

**文件：**

- 修改 `tests/test_pipeline_independent_steps.py`
- 新增 `GrayscaleLayersMac.Tests/UiStructureContractTests.cs`
- 新增 `design-evidence/ui-polish-v2/` 下的基线截图

**步骤：**

1. 用静态契约固定左右分栏、四个参数分组顺序、纹理/DXF 切换、日志位置、`SplitButton` 三个单步入口和既有字段文案。
2. 增加测试，明确不存在分类检查器、步骤向导和预览中央导入按钮。
3. 在 1440×940 下分别保存当前深色和浅色默认状态，作为改造前证据。
4. 保存当前 1080×720 最小窗口截图，记录已有裁切或滚动行为。
5. 运行现有测试，记录基线数量和结果。

**验证：**

```bash
python3 -m unittest tests/test_pipeline_independent_steps.py
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
```

**提交：** `test: 固化桌面工作台原有操作结构`

## 1. 补全语义令牌与公共尺寸

**文件：**

- 修改 `GrayscaleLayersMac/UiTheme.cs`
- 修改 `GrayscaleLayersMac.Tests/UiThemeContrastTests.cs`
- 新增 `GrayscaleLayersMac.Tests/UiThemeTokenTests.cs`

**步骤：**

1. 先写失败测试，枚举窗口、顶部栏、检查器、卡片、下沉表面、弹出层、四级文字、三级边框、强调、选中、焦点、禁用、危险、警告、成功、信息和图标状态。
2. 为浅色和深色补齐所有角色，保持现有公共画刷实例稳定，确保切换主题时不重建窗口。
3. 将公共高度、圆角和间距集中为命名常量：常规控件 36pt、图标按钮 32pt、主按钮 44pt、控件圆角 8pt、卡片圆角 12pt、分段外壳圆角 9pt。
4. 扩充对比度测试，覆盖主文字、次要文字、错误文字、按钮文字、菜单选中项、输入边界和焦点环。
5. 保留 DXF 图层与加工方向的数据调色板，但将其与通用交互令牌明确分离。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiTheme"
```

**提交：** `ui: 补全双主题语义令牌与尺寸规范`

## 2. 建立共享控件状态样式

**文件：**

- 修改 `GrayscaleLayersMac/UiTheme.cs`
- 新增 `GrayscaleLayersMac.Tests/UiControlStyleTests.cs`

**步骤：**

1. 先写失败测试，覆盖主按钮、次级按钮、安静按钮、图标按钮、危险按钮、TextBox、NumericUpDown、ComboBox、RadioButton、Flyout、Expander 和分段控件的样式契约。
2. 给每类按钮增加默认、悬停、按下、键盘焦点和禁用状态；按下反馈从 `:pressed` 开始，点击语义不变。
3. 为 TextBox、NumericUpDown 和 ComboBox 统一高度、圆角、内边距、只读、禁用、错误与焦点环。
4. 覆盖 Fluent 轻量资源，使弹出菜单、菜单项、下拉箭头、分割线、滚动条和数字框按钮使用同一套主题状态。
5. 将窗口里只负责设尺寸和类名的逻辑收敛为明确入口，不把业务 Click 处理移入主题层。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiControlStyle"
```

**提交：** `ui: 统一桌面控件状态样式`

## 3. 引入统一 Fluent 图标系统

**文件：**

- 修改 `GrayscaleLayersMac/GrayscaleLayersMac.csproj`
- 新增 `GrayscaleLayersMac/UiIcons.cs`
- 新增 `GrayscaleLayersMac.Tests/UiIconsTests.cs`

**步骤：**

1. 先写失败测试，定义导入、清缓存、外观、上一层、下一层、缩小、放大、适合窗口、实际尺寸、清空日志和折叠等必需图标。
2. 固定使用 `FluentIcons.Avalonia` 2.0.317；该版本面向 Avalonia 11。只使用 Regular 单色图标，规避 macOS 上彩色字体图标的已知渲染限制。
3. 在 `UiIcons` 中集中完成图标名称到控件的映射、默认尺寸、主题前景和不可用回退。
4. 图标加载失败时回退带文字的原生按钮，不能让操作入口消失。
5. 禁止在窗口或预览控件中新增字符符号、散落 Path 数据或手写 SVG。

**验证：**

```bash
dotnet restore GrayscaleLayersMac/GrayscaleLayersMac.csproj
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiIcons"
```

**提交：** `ui: 引入 Avalonia Fluent 工具图标`

## 4. 精修顶部工具与按钮层级

**文件：**

- 修改 `GrayscaleLayersMac/MainWindow.cs`
- 修改 `tests/test_pipeline_independent_steps.py`
- 修改 `GrayscaleLayersMac.Tests/UiStructureContractTests.cs`

**步骤：**

1. 先更新失败契约，固定原工作流不变，同时描述新的顶部紧凑工具组。
2. 保留应用图标、名称和副标题，降低品牌区重量；在现有顶部区域内对齐“导入、清缓存、外观”。
3. “全部执行”继续使用主按钮样式；路径选择和打开目录使用次级按钮；导入与外观使用安静按钮；清缓存使用带 Tooltip 的图标按钮。
4. 保持导入 Flyout、外观选择和清缓存事件处理完全不变，只替换内容呈现与排列。
5. 为顶部图标按钮设置自动化名称、Tooltip、32pt 目标区域和键盘焦点。
6. 运行应用，分别保存深色与浅色顶部区域改造后截图，并与基线同尺寸对比。

**验证：**

```bash
python3 -m unittest tests/test_pipeline_independent_steps.py
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStructureContract"
```

**提交：** `ui: 重排顶部工具并强化按钮层级`

## 5. 精修下拉框、输入框和参数卡片

**文件：**

- 修改 `GrayscaleLayersMac/MainWindow.cs`
- 修改 `GrayscaleLayersMac/UiTheme.cs`
- 修改 `GrayscaleLayersMac.Tests/UiControlStyleTests.cs`

**步骤：**

1. 将现有 ComboBox、TextBox、NumericUpDown 和复合路径字段接入共享样式，不修改字段顺序、绑定值或事件。
2. 为下拉框应用统一箭头、选中对勾、菜单项背景、弹出层圆角和键盘焦点。
3. 统一标签、单位、输入和路径选择按钮基线；长路径保留完整值 Tooltip。
4. 继续使用现有纵向 Expander；只调整标题背景、字重、内边距、折叠箭头和卡片间距。
5. 保持所有参数组原有默认展开状态和可同时展开行为。
6. 保存下拉菜单打开、输入框焦点、禁用、错误以及卡片展开/折叠状态截图。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiControlStyle"
python3 -m unittest tests/test_pipeline_independent_steps.py
```

**提交：** `ui: 精修参数输入与折叠卡片`

## 6. 精修预览工具、分段控件和日志

**文件：**

- 修改 `GrayscaleLayersMac/GrayscaleLayerPreviewControl.cs`
- 修改 `GrayscaleLayersMac/DxfPreviewHost.cs`
- 修改 `GrayscaleLayersMac/LogPanelView.cs`
- 修改 `GrayscaleLayersMac/CollapseHandle.cs`
- 修改 `GrayscaleLayersMac/MainWindow.cs`
- 修改 `GrayscaleLayersMac.Tests/DxfPreviewHostTests.cs`
- 修改 `GrayscaleLayersMac.Tests/LogPanelViewTests.cs`
- 修改 `GrayscaleLayersMac.Tests/CollapseHandleTests.cs`

**步骤：**

1. 先扩充测试，固定上一层、下一层、缩放、适合窗口、实际尺寸、清空和折叠的原动作语义。
2. 将“纹理 / DXF”做成带统一外壳的胶囊分段控件，保留原位置和 Click 行为。
3. 将“−、+、适应窗口、100%”等小操作替换为统一图标按钮；必要时保留短文字或 Tooltip，不改变顺序。
4. 纹理与 DXF 预览共用同一套工具按钮尺寸、间距、悬停、按下、焦点和禁用状态。
5. 日志清空和折叠把手接入图标系统与自动化名称；日志位置、高度和持久化行为不变。
6. 保存纹理、DXF、日志展开和日志折叠状态的浅深主题截图。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~DxfPreviewHost|FullyQualifiedName~LogPanelView|FullyQualifiedName~CollapseHandle"
```

**提交：** `ui: 统一预览与日志工具控件`

## 7. 补齐减少动态效果与辅助功能

**文件：**

- 新增 `GrayscaleLayersMac/MotionPreferences.cs`
- 新增 `GrayscaleLayersMac.Tests/MotionPreferencesTests.cs`
- 修改 `GrayscaleLayersMac/UiTheme.cs`
- 修改 `GrayscaleLayersMac/CollapseHandle.cs`
- 修改 `GrayscaleLayersMac/LogPanelView.cs`
- 修改 `GrayscaleLayersMac/GrayscaleLayerPreviewControl.cs`
- 修改 `GrayscaleLayersMac/DxfPreviewHost.cs`
- 修改 `GrayscaleLayersMac/MainWindow.cs`

**步骤：**

1. 先写失败测试，描述正常模式和减少动态效果模式下允许安装的 Transition 类型。
2. 集中读取平台运动偏好；若 Avalonia/macOS 无法可靠提供该值，使用可替换接口并默认采用克制模式。
3. 正常模式：按钮 100–140ms，折叠和日志 180–220ms，进入与退出路径对称。
4. 减少动态效果模式：不安装位移、旋转或缩放动画，只保留即时切换或短颜色/透明度变化。
5. 为外观菜单、图标按钮、GridSplitter、Expander、日志把手、分段控件和主执行按钮设置自动化名称。
6. 用键盘逐项验证 Tab 顺序、Enter/Space 激活、方向键或原生下拉操作、分割菜单和 GridSplitter。
7. 用 VoiceOver 核对名称、状态和操作结果，不从截图推断可访问性完成。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~MotionPreferences|FullyQualifiedName~UiControlStyle"
```

**提交：** `ui: 支持减少动态效果与辅助技术`

## 8. 清理硬编码状态色并完成主题回退

**文件：**

- 修改 `GrayscaleLayersMac/MainWindow.cs`
- 修改 `GrayscaleLayersMac/GrayscaleLayerPreviewControl.cs`
- 修改 `GrayscaleLayersMac/DxfPreviewControl.cs`
- 修改 `GrayscaleLayersMac/GrayscaleLayerPreviewCanvas.cs`
- 修改 `GrayscaleLayersMac/App.cs`
- 修改 `GrayscaleLayersMac.Tests/UiThemeTokenTests.cs`
- 修改 `GrayscaleLayersMac.Tests/AppAppearanceResolverTests.cs`

**步骤：**

1. 先加失败测试或静态断言，禁止通用错误、警告和完成状态继续直接使用 `Brushes.OrangeRed` 或主题相关 RGB。
2. 将错误、警告、成功、信息、禁用和焦点状态替换为语义画刷。
3. 保留 DXF 图层和加工方向数据颜色，但验证它们在两种画布上可辨认。
4. 补充系统主题变化只影响 `System` 模式的测试，手动浅色或深色不被系统变化覆盖。
5. 系统主题无法解析时回退深色并写入非阻塞日志；样式或图标异常不得中断业务流程。
6. 验证主题切换不重建窗口、不清空表单、不改变预览类别、滚动位置和分栏比例。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiTheme|FullyQualifiedName~AppAppearance"
rg -n "Brushes\.OrangeRed|Color\.From" GrayscaleLayersMac --glob '*.cs'
```

**提交：** `ui: 完成双主题状态覆盖与回退`

## 9. 完整视觉验收与交付

**文件：**

- 新增 `design-evidence/ui-polish-v2/` 下的最终截图与对比图
- 按发现的问题修改本计划涉及的 UI 文件和测试

**步骤：**

1. 在 1440×940 和 1080×720 下分别捕获浅色、深色默认状态。
2. 捕获下拉菜单、外观菜单、按钮悬停/按下/焦点/禁用、输入焦点/错误、卡片展开/折叠、日志展开/折叠。
3. 使用真实或安全测试数据捕获导入完成、执行中、失败和完成状态，以及长路径和长中文错误文本。
4. 将基线与最终截图按相同尺寸并排比较；逐项检查工具排列、下拉框、按钮、输入、卡片、分段控件、菜单和图标是否无需放大即可看出变化。
5. 检查默认和最小窗口中的裁切、滚动、基线、间距、圆角、边框、焦点环与菜单位置。
6. 完成键盘、VoiceOver、主题跟随、手动覆盖、减少动态效果和主题切换状态保持测试。
7. 运行完整 .NET、Python、Release 构建和 macOS 应用包验证。
8. 执行 `git diff --check`，确认业务管线和参数逻辑没有无关改动。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
python3 -m unittest discover -s tests
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
./scripts/test-macos-app-bundle.sh
git diff --check
```

**完成门槛：**

- 原操作路径和业务逻辑保持兼容。
- 所有规定控件和状态均完成浅深主题改造。
- 前后截图在不放大的情况下有明确可见差异。
- 所有自动测试、交互检查、视觉检查和应用包验证通过。
- 最终交付包含可打开的 `.app`、测试结果和完整视觉证据。

**提交：** `test: 完成桌面 UI 精修验收`

## 实施顺序与停止条件

- 严格按 0–9 顺序执行；任何阶段的目标测试或截图检查失败，先修复当前阶段，不进入下一阶段。
- 每个阶段只提交该阶段相关文件，保留用户现有未跟踪文件和无关改动。
- 若某个共享样式导致业务控件不可操作，立即回退该控件到 Fluent 默认样式并记录缺口，不能以视觉效果换取功能退化。
- 若新的图标依赖与 Avalonia 11.3.18 或 macOS 打包不兼容，停止采用该依赖，改用 Avalonia 官方 `PathIcon` 与公开 Fluent 图标资源，并保留相同 `UiIcons` 接口。
