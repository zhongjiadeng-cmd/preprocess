# LaserPMT 节点工作流实现计划

**目标：** 将 LaserPMT 从笛卡尔积参数表单升级为可持久化的节点工作流，支持基础参数继承、单参数多值编号分线、PMT 独立新增/移动/删除，以及可加工的多时间戳元素。

**设计规格：** `docs/superpowers/specs/2026-09-01-laserpmt-node-workflow-design.md`

**技术栈：** C# 13 / .NET 10、Avalonia 11.3.x、NUnit、Python 3.9+、NumPy、pytest。

## 全局约束

- 保留现有 Texture、DXF 和前三步流水线行为。
- `pmt-layout.json` 格式版本 2 是节点工作流的唯一真实配置；生成请求必须来自不可变工作流快照的编译结果。
- 不再用参数笛卡尔积决定 PMT 数量。
- 每个单参数节点只表示一个参数名；一个稳定端口可以连接多个目标。
- 单参数连接覆盖基础同名项；从基础节点移除的参数必须在每个目标上通过连线补齐。
- PMT 删除后不重排、不复用编号；新增 PMT 使用从未使用过的新编号。
- 所有 PMT 先按编号加工，随后按创建顺序加工全部时间戳。
- 时间戳不生成独立机器 JSON。
- 时间戳轮廓和水平填充必须确定性生成，不添加新运行时依赖。
- 继续使用现有 NPY 内容组精确去重、所有权锁、无覆盖和原子发布机制。
- 每个任务先增加或调整聚焦测试，使其在旧实现上按预期失败，再实现最小行为并运行对应测试。
- 不提交工作区内既有的 `.workbuddy/`、`1/`、`design-audit/` 或粘贴图片。

## 任务 1：定义版本 2 工作流领域模型

**文件：**

- 新建 `GrayscaleLayersMac/LaserPmtWorkflow.cs`。
- 新建 `GrayscaleLayersMac.Tests/LaserPmtWorkflowTests.cs`。
- 修改 `GrayscaleLayersMac/LaserPmtConfiguration.cs`。

- [ ] 为基础节点、单参数节点、稳定端口、连线、PMT、时间戳、画布视口和下一个编号定义不可变记录类型。
- [ ] 使用不区分 UI 控件的稳定字符串 ID；构造时拒绝空 ID、重复 ID、悬空端口和悬空目标。
- [ ] 将基础 machine 第一组的全部可编辑参数规范化为有序字典，并记录用户移除的基础项。
- [ ] 为时间戳保存 8 位文本、创建序号、物理位置和物理宽高。
- [ ] 为 PMT 保存编号、物理边界和 `WasManuallyMoved`。
- [ ] 增加工作流级不变量测试：唯一基础节点、唯一目标/端口/连线 ID、单目标同参数单输入、合法编号状态。
- [ ] 运行 `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter LaserPmtWorkflowTests`。

## 任务 2：实现单参数端口稳定性和参数编译器

**文件：**

- 新建 `GrayscaleLayersMac/LaserPmtWorkflowCompiler.cs`。
- 新建 `GrayscaleLayersMac.Tests/LaserPmtWorkflowCompilerTests.cs`。
- 修改 `GrayscaleLayersMac/LaserPmtConfiguration.cs`。

- [ ] 复用现有整数/布尔参数元数据，解析单参数节点的逗号值，拒绝空项、重复值、非法类型和越界值。
- [ ] 实现按“原位置＋规范化值”匹配的稳定端口更新；增加、删除和重排值时仅让无法匹配的端口失效。
- [ ] 删除端口时返回受影响连线和目标，供 UI 显示提示。
- [ ] 允许一个端口连接多个目标；拒绝同一目标同一参数的第二条输入。
- [ ] 编译每个 PMT/时间戳的完整最终参数，执行“基础参数 → 单参数覆盖”。
- [ ] 基础项被移除且目标未补齐时，产生带目标 ID、参数名和定位信息的阻断错误。
- [ ] 编译结果按 PMT 编号和时间戳创建顺序分别稳定排序。
- [ ] 运行 `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter LaserPmtWorkflowCompilerTests`。

## 任务 3：实现 PMT 生命周期、手动布局和几何校验

**文件：**

- 新建 `GrayscaleLayersMac/LaserPmtWorkflowEditor.cs`。
- 新建 `GrayscaleLayersMac.Tests/LaserPmtWorkflowEditorTests.cs`。
- 修改 `GrayscaleLayersMac/GrayscalePreviewViewMath.cs` 或新建聚焦的 `LaserPmtGeometry.cs`。

- [ ] 从独立“PMT 数量”和“每行数量”创建初始等间距布局。
- [ ] 增加数量时分配从未使用的新编号，并寻找首个边界内且不重叠的自动位置。
- [ ] 减少数量时从当前编号最大的 PMT 开始删除。
- [ ] 单独删除 PMT 时同步删除目标连线、数量减一、其他编号和位置保持不变。
- [ ] 单独移动 PMT 后设置 `WasManuallyMoved=true`。
- [ ] 修改每行数量不得移动既有 PMT；显式自动重排覆盖位置但保留编号和连线。
- [ ] 校验 PMT–PMT、PMT–时间戳、时间戳–时间戳的正面积相交；允许边缘接触。
- [ ] 使用机器输出精度执行边界和相交终检，拒绝舍入后越界或重叠。
- [ ] 运行 `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter LaserPmtWorkflowEditorTests`。

## 任务 4：定义并验证版本 2 `pmt-layout.json`

**文件：**

- 修改 `GrayscaleLayersMac/LaserPmtLayout.cs`。
- 新建 `GrayscaleLayersMac/LaserPmtWorkflowSerializer.cs`。
- 修改 `GrayscaleLayersMac/LaserPmtLayoutWriter.cs` 或以新序列化器替代其版本 2 职责。
- 修改 `GrayscaleLayersMac.Tests/LaserPmtLayoutTests.cs`。
- 修改 `GrayscaleLayersMac.Tests/LaserPmtLayoutWriterTests.cs`。

- [ ] 固定格式版本 2 的字段、顺序、稳定 ID、视口、Hatch spacing、节点、端口、连线、目标、编号状态和编译结果契约。
- [ ] 保持有界读取、重复键拒绝、非有限数拒绝和严格字段类型校验。
- [ ] 实现工作流序列化往返，确保节点/目标位置、端口 ID、编号空缺、创建顺序和连线不变。
- [ ] 加载时重新编译源工作流并对比保存的最终参数、目标边界、顺序和输出引用。
- [ ] 保留格式版本 1 的只读解析；构造等价预览模型，不原地写回。
- [ ] 版本 1 首次编辑必须要求新输出位置，版本 2 才允许正常保存。
- [ ] 运行 `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "LaserPmtLayoutTests|LaserPmtLayoutWriterTests"`。

## 任务 5：升级 Python 请求为显式目标计划

**文件：**

- 修改 `laser_pmt.py`。
- 修改 `tests/test_laser_pmt.py`。

- [ ] 添加格式版本 2 请求夹具，包含非连续 PMT 编号、共享端口值和多个时间戳。
- [ ] 用显式 PMT/时间戳目标列表替代笛卡尔积展开入口；保留旧纯函数仅用于格式版本 1 兼容测试，不再进入新生成路径。
- [ ] 严格校验每个目标的稳定 ID、类型、最终参数、边界、顺序和基础目录身份。
- [ ] 按 PMT 编号生成独立文件名，允许编号空缺但拒绝重复和文件名碰撞。
- [ ] 使现有 PMT NPY 内容组去重、局部运动和全局运动逻辑从显式目标计划读取参数与位置。
- [ ] 更新 CLI JSON 请求边界和错误信息，拒绝混用版本 1 与版本 2 字段。
- [ ] 运行 `python3 -m pytest -q tests/test_laser_pmt.py`。

## 任务 6：实现确定性的时间戳字形和水平填充 NPY

**文件：**

- 新建 `laser_timestamp.py` 或在 `laser_pmt.py` 中增加一个可独立测试的聚焦模块；优先新建文件以隔离字形几何。
- 新建 `tests/test_laser_timestamp.py`。
- 修改 `laser_pmt.py`。
- 修改 `GrayscaleLayersMac/GrayscaleLayersMac.csproj` 及打包脚本中明确列举的 Python 资源。

- [ ] 定义随应用发布、仅覆盖数字 `0–9` 的确定性轮廓数据，不读取系统字体。
- [ ] 校验时间戳恰好为 8 位 ASCII 数字。
- [ ] 将 8 个字形按目标宽高映射到物理边界，保持固定字间距规则。
- [ ] 使用项目 Hatch spacing 与字形轮廓求交，生成从左到右的水平扫描线；奇数交点、零长度线段或非有限坐标必须报错。
- [ ] 把扫描线编码为现有控制器兼容 NPY 结构，并验证 dtype、shape、XY 边界和 Z 语义。
- [ ] 将时间戳 patch 组纳入现有逐数组精确比较和二维 `[group, local]` 引用。
- [ ] 测试缩放、合法/非法文本、线距、孔洞数字、确定性输出、内容组复用及打包资源解析。
- [ ] 运行 `python3 -m pytest -q tests/test_laser_timestamp.py tests/test_laser_pmt.py`。
- [ ] 运行 `python3 -m py_compile laser_timestamp.py laser_pmt.py`；若未新建模块，仅编译实际文件。

## 任务 7：扩展全局运动、CSV 和完整包校验

**文件：**

- 修改 `laser_pmt.py`。
- 修改 `tests/test_laser_pmt.py`。

- [ ] 生成顺序固定为全部 PMT 按编号升序，随后全部时间戳按创建序号升序。
- [ ] 每个 PMT 继续生成独立 JSON；时间戳不得出现在任何独立 PMT JSON，也不得生成自己的 JSON。
- [ ] 在 `allmachine.json` 中为时间戳使用其最终激光参数、全局位置和 NPY 引用，连续重算相对运动。
- [ ] 扩展 `parameter-map.csv`：目标类型/ID、显示编号或文本、顺序、位置、尺寸、最终值、来源节点、稳定端口 ID、可见编号、JSON 文件名和 patch 引用。
- [ ] 更新完整包精确文件集合校验，确保没有时间戳独立 JSON、悬空 NPY 或意外文件。
- [ ] 交叉验证布局、CSV、独立 JSON、`allmachine.json` 和 NPY 的参数、顺序、位置与引用。
- [ ] 保持冲突、锁、取消、路径身份和原子发布测试通过。
- [ ] 运行 `python3 -m pytest -q tests/test_laser_pmt.py tests/test_dxf_to_machine_file.py`。

## 任务 8：构建节点工作流画布的渲染和视图数学

**文件：**

- 新建 `GrayscaleLayersMac/LaserPmtWorkflowCanvas.cs`。
- 新建 `GrayscaleLayersMac/LaserPmtWorkflowViewMath.cs`。
- 新建 `GrayscaleLayersMac.Tests/LaserPmtWorkflowViewMathTests.cs`。
- 修改或逐步替代 `GrayscaleLayersMac/PmtPreviewControl.cs`。

- [ ] 为画布坐标、工件物理坐标和屏幕坐标建立可逆变换测试。
- [ ] 实现缩放锚定、平移、适应全部、适应工件和有界最小/最大缩放。
- [ ] 绘制工件、基础参数节点、单参数节点、端口、PMT、时间戳、总线、贝塞尔连线及线中编号。
- [ ] 为节点标题栏、端口、连线、PMT、时间戳和缩放柄实现独立命中测试。
- [ ] 选择、缺参、非法节点、越界和重叠状态必须使用现有主题令牌并满足明暗主题可辨识度。
- [ ] 保留现有 PMT 预览加载入口，格式版本 1 使用只读模式，格式版本 2 使用工作流画布。
- [ ] 运行 `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter LaserPmtWorkflowViewMathTests`。

## 任务 9：实现画布编辑命令和右侧检查器

**文件：**

- 修改 `GrayscaleLayersMac/LaserPmtWorkflowCanvas.cs`。
- 新建 `GrayscaleLayersMac/LaserPmtWorkflowInspector.cs`。
- 新建 `GrayscaleLayersMac.Tests/LaserPmtWorkflowInteractionTests.cs`。
- 修改或替代 `GrayscaleLayersMac/PmtDetailsEditor.cs`。

- [ ] 实现节点标题拖动、PMT/时间戳拖动、时间戳四角缩放和视口手势的指针状态机。
- [ ] 实现从编号端口拖到目标参数入口的连线创建，以及选择线/端口后 Delete 删除连接。
- [ ] 实现基础参数项删除/恢复、单参数节点添加/删除、参数名选择和值列表编辑。
- [ ] 实现 PMT 单独删除、数量增减、每行数量修改和显式自动重排确认入口。
- [ ] 实现多个时间戳的添加、8 位文本编辑、移动、宽高编辑、缩放和删除；默认文本在创建时按本地 `MMddHHmm` 固定。
- [ ] 检查器显示基础值、覆盖值、来源节点/端口和最终值。
- [ ] 所有编辑操作通过领域编辑器产生新快照；控件不得直接修改生成请求或磁盘文件。
- [ ] 运行 `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter LaserPmtWorkflowInteractionTests`。

## 任务 10：集成 LaserPMT 面板、保存和生成流程

**文件：**

- 修改 `GrayscaleLayersMac/LaserPmtPanel.cs`。
- 修改 `GrayscaleLayersMac/MainWindow.cs`。
- 修改 `GrayscaleLayersMac/PipelineReadiness.cs`。
- 修改相关 `GrayscaleLayersMac.Tests/UiStructureContractTests.cs`、`PipelineReadinessTests.cs` 和 `PipelineImportFlowContractTests.cs`。

- [ ] 用 PMT 数量、每行数量、节点工具栏、错误汇总和工作流画布替换旧动态参数笛卡尔积表。
- [ ] 基础 machine 导入后构造基础参数节点、初始 PMT 和版本 2 工作流；更换基础目录时重新核对身份并明确提示失效状态。
- [ ] 生成时冻结当前内存工作流快照，原子写出临时版本 2 请求；成功输出中的 `pmt-layout.json` 持久化同一源工作流和生成后的引用。
- [ ] 点击错误汇总项时选择、居中并聚焦相应节点、连线或目标。
- [ ] 只有当前工作流能够重新编译且无阻断错误时才允许执行第 4 步；不要求用户先执行额外保存操作。
- [ ] 从编译结果构建格式版本 2 JSON 请求并调用 Python；生成成功后加载版本 2 布局但保留当前视口和选择状态。
- [ ] 旧版布局只读预览；首次编辑要求选择新输出位置，不覆盖历史目录。
- [ ] 确保清空缓存、取消、窗口关闭和导入切换释放画布状态且不删除用户文件。
- [ ] 运行相关 NUnit 测试，并运行 `dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore`。

## 任务 11：文档、完整回归和视觉 QA

**文件：**

- 修改 `GrayscaleLayersMac/README.md`。
- 修改实施过程中发现需要更新的聚焦契约测试。

- [ ] 记录节点类型、基础继承、删除基础项后的补齐规则、端口编号、PMT 编号空缺、时间戳格式和加工顺序。
- [ ] 记录版本 1 只读与版本 2 保存/生成的兼容边界。
- [ ] 运行 `python3 -m pytest -q` 并要求零失败。
- [ ] 运行 `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore` 并要求零失败。
- [ ] 运行 `dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore` 并要求成功。
- [ ] 运行相关 macOS 打包与 Python 资源验证脚本。
- [ ] 启动应用，用包含编号空缺、一个端口多目标、基础参数缺失修复和两个时间戳的代表性工作流完成生成。
- [ ] 视觉检查缩放、平移、拖动、连线命中、线中编号、明暗主题、错误定位、时间戳缩放和旧布局只读模式。
- [ ] 检查独立 PMT JSON、`allmachine.json`、CSV、版本 2 布局和 NPY，确认参数来源、坐标、顺序和引用一致。
- [ ] 运行 `git diff --check`，检查 `git status --short`，只提交本功能相关文件。

## 建议提交检查点

1. `test(pmt): define workflow domain contracts`
2. `feat(pmt): compile parameter node workflows`
3. `feat(pmt): support movable removable pmt targets`
4. `feat(pmt): persist workflow layout version two`
5. `feat(pmt): generate explicit workflow targets`
6. `feat(pmt): generate timestamp hatch patches`
7. `feat(pmt): validate workflow output packages`
8. `feat(pmt): add node workflow canvas`
9. `feat(pmt): add workflow editing interactions`
10. `feat(pmt): integrate node workflow generation`
11. `docs(pmt): document node workflow`
