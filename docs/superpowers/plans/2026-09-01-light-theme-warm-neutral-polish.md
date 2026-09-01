# 浅色主题暖中性配色精修实施计划

**目标：** 在不改变深色主题、布局和交互行为的前提下，把浅色模式统一为已确认的暖中性灰方向，并通过自动化对比度测试与应用截图验证结果。

**设计规范：** `docs/superpowers/specs/2026-09-01-light-theme-warm-neutral-polish-design.md`

**架构：** 保持 `UiTheme` 的现有语义令牌与共享画刷数据流，只替换 `LightPalette` 的表面、边框和中性状态值。控件继续消费共享画刷，`ApplyScheme` 的行为及画刷实例稳定性保持不变。

**技术栈：** C# 13、.NET 10、Avalonia 11.3.x、MSTest、Avalonia Headless。

## 任务 1：锁定浅色主题视觉契约

**文件：**

- 修改 `GrayscaleLayersMac.Tests/UiThemeTokenTests.cs`
- 修改 `GrayscaleLayersMac.Tests/UiThemeContrastTests.cs`

**步骤：**

1. 增加浅色主题表面色温契约，要求窗口、顶部栏、检查器、卡片、工具条和下沉表面符合暖中性目标。
2. 固定表面明度关系，保证卡片与顶部栏高于窗口底，检查器与下沉表面低于内容卡片。
3. 增加交互中性色契约，覆盖边框、Ghost、禁用背景与选中背景，防止冷蓝灰通过状态色重新出现。
4. 保留既有文字、主按钮、焦点环和强边界对比度阈值。
5. 运行聚焦测试并记录预期失败，确认测试能够识别旧浅色调色板。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiThemeTokenTests|FullyQualifiedName~UiThemeContrastTests"
```

## 任务 2：实现暖中性浅色调色板

**文件：**

- 修改 `GrayscaleLayersMac/UiTheme.cs`
- 按需要微调 `GrayscaleLayersMac.Tests/UiThemeTokenTests.cs`

**步骤：**

1. 将 `Root`、`Header`、`Panel`、`Card`、`Bar`、`Sunken` 和 `Popup` 更新为设计规范中的暖中性色组。
2. 轻微中性化弱化与禁用文字，保持主文字和次要文字清晰度。
3. 将轻、中、强边框及 Ghost 状态的底色从蓝灰改为低饱和暖灰，并保持透明度层级。
4. 同步更新 Handle、禁用背景、选择背景和图标禁用态，使静态与交互状态一致。
5. 保持蓝色强调色、语义状态色、深色调色板和共享画刷实例不变。
6. 运行聚焦测试，按对比度结果对浅色值做最小调整。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiThemeTokenTests|FullyQualifiedName~UiThemeContrastTests|FullyQualifiedName~AppAppearanceResolverTests"
```

## 任务 3：完整回归与视觉验证

**文件：**

- 修改必要的主题测试
- 新增 `design-evidence/light-theme-warm-neutral-polish/` 下的最终截图

**步骤：**

1. 运行完整 C# 测试套件，确认主题切换、控件样式、布局和业务 UI 合约无回归。
2. 构建 macOS 应用并运行应用包验证脚本。
3. 启动应用并切换到浅色模式，在与参考图相近的窗口尺寸捕获最终截图。
4. 检查顶部栏、预览画布、参数检查器、卡片、输入框、禁用项和底部执行区的色温与层级。
5. 切换到深色模式做回归检查，确认深色调色板未发生变化。
6. 运行差异与工作区检查，确保没有散落主题 RGB、格式问题或无关改动。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
git diff --check
git status --short
```

## 建议提交顺序

1. `docs(ui): plan warm neutral light theme polish`
2. `test(ui): define warm neutral light theme contract`
3. `ui: polish light theme with warm neutral palette`
4. `test(ui): verify light theme visual regressions`
