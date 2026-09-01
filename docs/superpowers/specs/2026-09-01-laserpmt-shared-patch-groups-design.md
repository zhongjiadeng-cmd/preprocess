# LaserPMT 共享二维 Patch 组设计

## 目标

让 `allmachine.json` 和每个独立 PMT JSON 使用普通 `machine.json` 的二维 patch 引用形式 `[x, y]`，对应 `patches/x_y.npy`。对生成后完全相同的整组 NPY 自动去重，使多个 PMT 可以引用同一组文件，同时允许未来内容不同的 PMT 使用不同的 NPY 组。

## 输出契约

- `allmachine.json` 保持 `laser_params`、`galvo_offset`、`machine_cycle` 三个顶层字段及其既有顺序。
- 每个加工循环保持 `{"galvo_0": [laser_param_index, command, [x, y]]}` 结构。
- `x` 是 NPY 内容组编号，按不同内容组首次出现的 PMT 顺序从 `0` 连续编号。
- `y` 是组内 patch 序号，保持基础 `machine.json` 的 patch 加工顺序，从 `0` 连续编号。
- 每个引用 `[x, y]` 必须存在对应文件 `patches/x_y.npy`。
- 每个 `pmt_xxxxmachine.json` 与 `allmachine.json` 对同一 PMT 使用完全相同的 patch 引用。
- `allmachine.json` 的 `laser_params` 仍可包含多个 PMT 参数组。其结构与普通 `machine.json` 相同；参数组数量不限制为三个，因为一次执行需要在不同 PMT 参数之间切换。

## 内容组判定

先按每个 PMT 的参数生成其完整 patch 数组，包括由 `layerFeedUm` 计算并写入的 Z 列，然后对整组内容进行比较。

两个 PMT 仅在以下条件全部满足时共享一个 `x`：

- patch 数量相同；
- 对应 patch 的 dtype 相同；
- 对应 patch 的 shape 相同；
- 对应 patch 的所有元素逐位相同。

因此，仅激光参数不同但 NPY 内容相同的 PMT 会共享文件；层进给等导致任一数组内容变化时，会产生新的内容组。采用逐数组精确比较，不以参数组合近似推断，也不使用容差比较。

## 生成流程

1. 按原有笛卡尔积和矩阵顺序处理 PMT。
2. 为当前 PMT 生成完整的内存 patch 组。
3. 按首次出现顺序与已有内容组逐数组精确比较。
4. 若完全相同，复用已有组编号；否则追加新组并写出其全部 `x_y.npy`。
5. 当前 PMT 的独立 JSON 和 `allmachine.json` 循环都引用该组的 `[x, y]`。
6. PMT 的运动坐标和激光参数索引仍各自独立，不因共享 NPY 而共享加工参数或位置。

## 元数据调整

- `pmt-layout.json` 中每个 job 的 `patch_indices` 改为二维引用列表，例如 `[[0, 0], [0, 1]]`。
- `parameter-map.csv` 的 `patch_indices` 使用无歧义的 `x_y` 序列，例如 `0_0;0_1`。
- 字段名暂时保持 `patch_indices`，避免为同一概念增加迁移范围；其值的形态由整数列表升级为二维引用列表。

## 校验与错误处理

生成完成、原子重命名前执行以下检查：

- `patches/` 仅包含实际内容组所需的 `x_y.npy`，无缺失和额外文件；
- 所有 JSON 引用均能解析到对应文件；
- 每个 PMT 的引用数量和顺序与基础 patch 一致；
- 独立 JSON 与 `allmachine.json` 对相同 PMT 的引用一致；
- 文件 dtype、shape、XY 和预期 Z 均正确；
- 共享同一 `x` 的 PMT，其生成后 patch 组确实逐数组完全相同；
- 局部及全局运动终点继续满足现有坐标校验。

任何校验失败都沿用现有临时目录清理与不覆盖正式输出的机制。

## 测试范围

- 多个 PMT 的 NPY 完全相同：只生成一个 `x` 组，所有 PMT 共享引用。
- 激光参数不同但不影响 NPY：仍共享同一组。
- `layerFeedUm` 不同导致 Z 不同：分配不同的 `x`。
- 部分 PMT 相同、部分不同：按首次出现顺序稳定分组。
- 文件名、JSON 引用、`pmt-layout.json` 和 CSV 互相一致。
- 原有局部运动、全局运动、参数切换、冲突保护和 CLI 测试继续通过。

## 不在本次范围内

- 不改变 PMT 参数展开顺序、布局、编号或运动路径。
- 不把不同激光参数合并成同一参数索引。
- 不使用近似浮点比较、跨输出目录缓存或硬链接。
- 不修改基础加工目录中的 `machine.json` 或 NPY 文件。
