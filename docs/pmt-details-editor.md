# PMT 单元详情面板可编辑化

> Worktree: `codex/pmt-details-editor`
> 关联模块：第 4 步 LaserPMT 矩阵预览中的单元详情条；`PmtPreviewControl` 选中事件触发的右侧摘要。

## 背景

目前选中某个 PMT 单元时，`MainWindow.UpdatePmtSelectionDetails()`
（`MainWindow.cs:1849-1864`）会向 `_pipelinePmtDetails`（一个只读 TextBlock）写入两行文字：

```text
pmt_0001 · 第 1 行 / 第 1 列 · 左上 (2.5, 27.5) mm · 层间进给 3 μm
pmt_0001machine.json · power=20 · frequency=30
```

第一行是单元标识、行列号、左上坐标、层间进给，第二行是单元文件名 + 该单元已覆盖的激光参数（来自
`LaserPmtLayout.Jobs[i].Parameters`，仅显示用户曾在 `LaserPmtPanel` 中指定为自由维度的项）。

用户反馈需要：

1. 摘要上的参数一栏可以直接编辑（不再被锁成只读字符串）；
2. 不仅显示已覆盖的参数，还要显示 `LaserPmtConfiguration.Parameters` 中**全部** 16 项；
3. 排版从横向 `power=20 · frequency=30` 改为竖向列表，每行一项；
4. 编辑后的覆盖值要能落到该单元对应的 `pmt_xxxxmachine.json`。

## 目标

1. 新增 `PmtDetailsEditor` 控件，替换 `_pipelinePmtDetails`，渲染选中单元的：
   - 标识卡（只读）+ 单元文件名（只读）；
   - 覆盖参数的竖向编辑表；
   - 保存/还原按钮。
2. `LaserPmtLayout` 支持替换 `Jobs[i].Parameters` 并重新生成 `pmt-layout.json` + 对应单元 `pmt_xxxxmachine.json`。
3. 测试覆盖：`PmtDetailsEditor` 状态机（无选中 / 有选中 / 编辑 / 保存）、磁盘落盘 round-trip、bool/int 类型校验。

## 设计要点

### UI 改造

- 新文件 `PmtDetailsEditor.cs`：基于 `UserControl`；内部三段式 `StackPanel`：
  - `HeaderCard`（只读 `TextBlock` × 2）：标识卡 + JSON 文件名；
  - `ParameterEditor`（`ScrollViewer` 包 `StackPanel`）：每行一个 `Grid`，列布局 `*,Auto` 分别是参数显示名 + 值编辑器；
  - `ActionBar`（两按钮）：「保存覆盖」「还原基础」。
- 顶部一段仍使用现有 `Border` 容器（`MakePmtPreviewContent` 行 1055-1063），把内容换成 `PmtDetailsEditor`；编辑器固定高度改为 `MinHeight=260`，避免覆盖按钮行被挤掉。
- 选中状态变更（`PmtPreviewControl.SelectionChanged`）时调用 `PmtDetailsEditor.LoadJob(job)`：
  - 标识卡填空；
  - 编辑器按 `LaserPmtConfiguration.Parameters` 顺序构建；每行值取自 `job.Parameters[definition.Name]`（不存在则为空，提示"沿用基础加工参数"）。
- 编辑器控件选择：
  - `IsBoolean=true` → `CheckBox`；
  - 其它 → `NumericUpDown`，约束 `Minimum`/`Maximum` 与 `LaserPmtParameterDefinition` 一致；
  - 数值类型不可解析时回退到 0 并用 `UiTheme.SetInputError` 给出红框 + 错误说明。
- 「保存覆盖」按下后：
  - 用编辑器值生成新的 `Dictionary<string, string>`，跳过值为空 / 等于基础值的项；
  - 触发 `JobParametersSaved` 事件；
  - `MainWindow` 订阅事件，调用新增的 `LaserPmtLayoutWriter.WriteJobsAsync(layout, outputPath)`：
    - 校验 `outputPath` 存在且包含 `pmt-layout.json`；
    - 写回 `pmt-layout.json`（保留其它字段、`parameter_order`、`machine_translation` 等）；
    - 对变更的每个 `job` 重新生成 `pmt_xxxxmachine.json`：读基础目录 `machine.json`，按该 `job.parameters` 覆盖 `laser_params[0]`，再走 `dxf_to_machine_file.build_machine_document(...)` 的等价契约（不调 Python，保持语言一致）。

### 关键不变量

- `LaserPmtLayout.Jobs[i].Parameters` 仍是 `IReadOnlyDictionary<string, string>`；修改走 `WithParameters(...)`（保留 record 不变性），每次返回新实例。
- 写回 `pmt-layout.json` 时严格保留 `format_version`、`coordinate_system`、`workpiece`、`unit`、`matrix`、`numbering`、`parameter_order` 五个顶层字段的语义与顺序；变更只针对 `jobs[i].parameters` 与 `jobs[i].json_file` 指向的文件。
- 对 `pmt_xxxxmachine.json` 的写入采用原子写：临时文件 + `File.Move(..., overwrite: true)`，避免重写期间被读取走旧值。
- 编辑器不删除任何定义项；删除参数意味着"恢复为沿用"，不是"不再传入"。

### 不在本次改动范围

- `LaserPmtPanel` 中"自定义参数值"笛卡尔积配置逻辑保持不变（与本任务解耦）。
- `dxf_to_machine_file.py` 不修改（其 `validate_machine_directory` 仍以 `first_laser_params` 校验单组，恰好对应每单元基础参数）。

## 验收

1. `dotnet test` 全通过。
2. 选中 PMT 单元后，右侧详情条显示标识卡 + 16 个参数的竖向表；空值显示"沿用基础加工参数"占位。
3. 修改 `power`/`frequency`/勾选布尔项 → 保存后：
   - `pmt-layout.json` 中该 `job.parameters` 仅包含用户改动的项；
   - `pmt_xxxxmachine.json` 中 `laser_params[0]` 对应字段被更新，其它字段（`galvo_offset`、`machine_cycle[0]` 等）保持不变。
4. 还原基础 → 内存 LaserPmtLayout 该 job 的 parameters 回到生成时的字典。
