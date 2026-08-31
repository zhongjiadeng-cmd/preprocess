# 预览模块布局优化实施计划

**目标：** 在不改变纹理、DXF 与 PMT 预览行为的前提下，建立统一的预览顶部区域，减少工具栏拥挤，并把 PMT 详情栏改为更易扫读的分区式布局。

**设计规范：** `docs/superpowers/specs/2026-09-01-preview-layout-polish-design.md`

**架构：** 纹理与 DXF 预览控件继续拥有各自的画布、图层导航和行为，但将“高频视图工具”和“当前视图上下文工具”作为明确的可组合控件暴露给主窗口。`MainWindow` 统一排列标签、当前高频工具、上下文工具和预览正文，并在切换标签时同步可见性。PMT 画布逻辑不变，`PmtDetailsEditor` 只重排内部摘要、参数和固定底部操作区。

**技术栈：** C# 13、.NET 10、Avalonia 11.3.x、MSTest、Avalonia Headless。

## 全局约束

- 保留纹理、DXF 与 PMT 的加载、选择、缩放、平移、视角和保存事件语义。
- 保留现有图层侧栏、折叠状态持久化、滚轮策略和“切层保持视图”。
- 不修改预览渲染算法、业务数据模型、文件格式、管线执行或工作区分栏比例。
- 继续复用 `UiTheme`、`UiIcons`、现有自动化名称和工具提示。
- 不引入新运行时依赖，不创建新的通用预览基类。
- 每个阶段先增加失败测试，再实现最小改动并运行聚焦测试。

## 任务 1：建立布局契约与行为护栏

**文件：**

- 修改 `GrayscaleLayersMac.Tests/UiStructureContractTests.cs`
- 修改 `GrayscaleLayersMac.Tests/DxfPreviewHostTests.cs`
- 修改 `GrayscaleLayersMac.Tests/PmtDetailsEditorTests.cs`
- 新增 `GrayscaleLayersMac.Tests/GrayscaleLayerPreviewControlTests.cs`

**步骤：**

1. 增加失败契约，要求共享预览顶部第一行同时包含左侧标签组和右侧当前视图工具组。
2. 增加失败契约，要求上下文工具独立于画布正文，并可按当前标签显隐。
3. 固定纹理与 DXF 的上一层、下一层、缩小、放大、适应窗口、实际尺寸、滚轮模式和保持视图入口。
4. 固定 DXF 顶视图、等轴测、纹理、填充线、方向箭头和透明度入口及可用性规则。
5. 增加 PMT 详情栏布局契约：目标宽度、摘要/滚动参数/固定操作三段结构、图标加文字按钮、完整参数数量与顺序。
6. 运行聚焦测试并记录预期布局契约失败，确认既有行为测试仍通过。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStructureContract|FullyQualifiedName~DxfPreviewHost|FullyQualifiedName~PmtDetailsEditor|FullyQualifiedName~GrayscaleLayerPreviewControl"
```

## 任务 2：拆分纹理与 DXF 工具组

**文件：**

- 修改 `GrayscaleLayersMac/GrayscaleLayerPreviewControl.cs`
- 修改 `GrayscaleLayersMac/DxfPreviewHost.cs`
- 修改对应测试文件

**步骤：**

1. 将每个预览控件的高频视图操作整理为独立控件组：缩小、稳定宽度的倍率、放大、适应窗口、实际尺寸。
2. 将图层导航、滚轮模式和保持视图整理为上下文工具组；DXF 的顶视图、等轴测和叠加控制也进入其上下文区。
3. 让预览控件正文只保留图层侧栏、画布卡片与状态区，工具控件通过只读属性供组合层使用。
4. 使用 `WrapPanel` 或分组容器保证上下文工具按语义组换行，不拆散透明度标签与滑块。
5. 保持所有按钮事件、启用状态、缩放倍率更新、Tooltip 和自动化名称不变。
6. 更新测试，使其从明确的工具组属性检查入口，而不是依赖工具必须位于控件正文的旧结构。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~DxfPreviewHost|FullyQualifiedName~GrayscaleLayerPreviewControl|FullyQualifiedName~GrayscalePreviewViewMath"
```

## 任务 3：组装统一预览顶部区域

**文件：**

- 修改 `GrayscaleLayersMac/MainWindow.cs`
- 修改 `GrayscaleLayersMac.Tests/UiStructureContractTests.cs`
- 修改 `GrayscaleLayersMac.Tests/SharedPreviewSelectionTests.cs`（仅在可见性协调需要新的纯状态时）

**步骤：**

1. 扩充 `SharedPreviewView`，记录三种视图的正文、高频工具和上下文工具。
2. 将“纹理 / DXF / PMT”分段切换移到第一行左侧，把当前视图高频工具放到同一行右侧。
3. 在第二行放当前视图的上下文工具；没有上下文工具时整行不参与测量。
4. 为 PMT 创建与纹理/DXF 顺序一致的缩小、倍率、放大和适应窗口工具组，并监听 `ViewChanged` 更新倍率。
5. 更新 `SelectSharedPreview`，一次同步正文、工具组和上下文行的可见性，不改变 `SharedPreviewSelection` 的自动选择规则。
6. 让三种正文使用一致的画布间距和最低高度，状态及操作提示靠近各自画布下方。
7. 确认切换标签不会重新创建控件、清空内容或重置视图。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStructureContract|FullyQualifiedName~SharedPreviewSelection|FullyQualifiedName~PmtPreviewControl"
```

## 任务 4：重排 PMT 详情检查器

**文件：**

- 修改 `GrayscaleLayersMac/PmtDetailsEditor.cs`
- 修改 `GrayscaleLayersMac/MainWindow.cs`
- 修改 `GrayscaleLayersMac.Tests/PmtDetailsEditorTests.cs`

**步骤：**

1. 将详情栏最终固定为 220 px，并保持画布列使用剩余宽度。
2. 把顶部摘要拆为单元编号、位置摘要和弱化 JSON 文件名，保留原有信息内容。
3. 将数值参数改为单行横排：左侧标签、右侧固定宽度输入框；保持参数定义顺序与校验逻辑。
4. 将布尔参数集中到带轻量分组标题的区域，继续使用三态语义表示“沿用基础加工值”。
5. 让中部参数区独立滚动；顶部摘要和底部操作不随列表滚动。
6. 将状态文字移到按钮上方；保存和还原使用纯图标按钮，并保留 Tooltip 及“保存覆盖”“还原基础”自动化名称。
7. 把界面内所有“预扫描”和“空写”统一替换为 `scanahead` 与 `skywritting`，但保留底层 `scan_ahead`、`sky_writing` 字段兼容性。
8. 增加源码与视觉树契约，确保用户可见文案不再包含旧中文名称，同时序列化字段不变。
9. 验证空状态、载入单元、切换单元、非法输入、保存成功和还原后的按钮与状态行为不变。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~PmtDetailsEditor|FullyQualifiedName~LaserPmtConfiguration"
```

## 任务 5：回归验证与视觉检查

**文件：**

- 修改必要的聚焦测试
- 新增 `design-evidence/preview-layout-polish/` 下的最终截图

**步骤：**

1. 运行完整 C# 测试套件并修复与布局契约相关的回归。
2. 构建 macOS 应用，确认没有 Avalonia 逻辑树重复挂载、测量循环或绑定错误。
3. 启动应用，在常规窗口与较窄窗口下依次检查纹理、DXF、PMT 三个标签。
4. 检查深色与浅色主题中的标签选中态、工具分组、画布边框、PMT 字段、焦点和禁用状态。
5. 使用已有或生成的多层数据检查图层栏展开/收起、切层、缩放、DXF 叠加与 PMT 参数保存。
6. 确认第二行只按语义组换行，不出现重叠、裁切或透明度滑块与标签分离。
7. 保存最终界面截图作为视觉证据，并运行差异与工作区检查。

**验证：**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
git diff --check
git status --short
```

## 建议提交顺序

1. `test(ui): define preview layout contracts`
2. `refactor(ui): expose contextual preview tool groups`
3. `ui: unify shared preview header layout`
4. `ui: reorganize PMT detail inspector`
5. `test(ui): verify preview layout regressions`
