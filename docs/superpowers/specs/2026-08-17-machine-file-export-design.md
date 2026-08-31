# macOS 工具机器加工文件导出设计

日期：2026-08-17

## 目标

在现有 `GrayscaleLayersMac` 的“灰度分层 → Hatch DXF”流程中增加第三步，将逐层 DXF 打包为与参考样例完全相同结构的机器加工文件目录。

本功能保持参考文件协议不变，仅将层间下降算法改为固定步进。页面默认每层下降 3 μm，并允许用户修改。页面同时允许修改 `machine.json` 中第一组激光参数；第二、第三组激光参数及其他固定设置继续沿用参考样例。

## 用户流程

现有两步处理改为三步：

1. 生成分层 TIFF。
2. 逐层生成 Hatch DXF。
3. 将本次生成的逐层 DXF 打包成机器加工文件。

第三步在现有页面增加以下设置：

- `层间进给（μm）`：数字输入框，默认值为 `3`。
- `加工文件名`：文本输入框；默认自动生成 `machine_file_YYYYMMDD_HHMMSS`，用户可以修改。
- `第一组激光参数`：可折叠参数区，开放参考样例 `laser_params[0]` 的全部字段。

加工文件不放在 DXF 目录内。程序取得 DXF 输出目录的父目录，并在该父目录中建立与 DXF 目录平级的加工文件目录。例如：

```text
共同父目录/
├── 30X30-40C-240u-FK_dxf/
│   ├── layer_01_....dxf
│   ├── layer_02_....dxf
│   └── ...
└── machine_file_YYYYMMDD_HHMMSS/
    ├── machine.json
    └── patches/
        ├── 0_0.npy
        ├── 1_0.npy
        └── ...
```

生成完成后，运行日志显示实际层数、线段总数、Z 范围及输出路径，并启用打开加工文件目录的按钮。

## 页面参数

第一组激光参数的默认值来自参考样例：

| 页面名称 | JSON 字段 | 类型 | 默认值 |
|---|---|---:|---:|
| 功率 | `power` | 整数 | 38 |
| 频率 | `frequency` | 整数 | 350 |
| 脉宽索引 | `pulseWidthIdx` | 整数 | 3 |
| 扫描速度 | `scanSpeed` | 整数 | 2100 |
| 跳转速度 | `jump_vel` | 整数 | 6000 |
| 跳转延迟 | `jump_delay` | 整数 | 50 |
| 扫描预读 | `scan_ahead` | 布尔 | true |
| 加速度比例 | `accScale` | 整数 | 50 |
| 拐角比例 | `cornerScale` | 整数 | 100 |
| 结束比例 | `endScale` | 整数 | 100 |
| 空中书写 | `sky_writing` | 布尔 | true |
| 时间滞后 | `timeLag` | 整数 | 100 |
| 开光偏移 | `laserOnShift` | 整数 | 18 |
| 关光延迟 | `delaseroff` | 整数 | 32 |
| 开光延迟 | `delaseron` | 整数 | 0 |

整数字段使用不带小数的数字输入控件，布尔字段使用复选框。生成时仅使用页面值替换 `laser_params[0]`。`laser_params[1]`、`laser_params[2]`、`galvo_offset` 和机器循环中的 `F40` 保持参考样例值不变。

## 组件设计

新增独立脚本 `dxf_to_machine_file.py`。脚本负责：

1. 从 DXF 文件名提取层号并按层号升序排列。
2. 读取当前应用生成的 ASCII DXF，提取所有 `LINE` 实体。
3. 保持 DXF 实体的原始顺序和每条线段的起终点方向。
4. 为各层线段写入 Z 坐标并转换为 NumPy `float32` 数组。
5. 写出 `patches/<patch_index>_0.npy`。
6. 根据实际层数、下降深度及激光参数生成 `machine.json`。
7. 校验完整输出，然后将临时目录改为最终加工文件名。

脚本只需要现有 NumPy 依赖，不新增 `ezdxf` 或 `pyvista`。本功能的输入边界是当前三步流程生成的极简 ASCII DXF，不承诺打包任意第三方二进制 DXF 或包含其他实体类型的 DXF。

Avalonia 页面负责收集参数、启动脚本、转发日志、处理取消操作及展示结果，不在 C# 中重复实现 DXF 或 NPY 编解码。

## Patch 数据格式

每个 `.npy` 文件保存一个二维数组：

```text
dtype: little-endian float32
shape: (线段数量, 6)
row:   [x1, y1, z1, x2, y2, z2]
```

每个值占 4 字节，因此每条线段的原始数值数据占 24 字节。文件由 `numpy.save` 生成，并使用 `allow_pickle=False` 读取验证。

对于从零开始的 patch 索引 `i`：

```text
layer_step_mm = layer_step_um / 1000
patch_z       = -i × layer_step_mm
cycle_move_z  = -(i + 1) × layer_step_mm
```

默认每层下降 3 μm 时：

```text
0_0.npy: Z =  0.000 mm；第一条 machine_cycle 移动到 -0.003 mm
1_0.npy: Z = -0.003 mm；第二条 machine_cycle 移动到 -0.006 mm
2_0.npy: Z = -0.006 mm；第三条 machine_cycle 移动到 -0.009 mm
```

`machine_cycle` 中的 G-code Z 值按参考样例保留三位小数格式，patch 内的 Z 使用 `float32` 保存实际计算值。

## machine.json 生成规则

顶层字段及顺序保持参考样例：

1. `laser_params`
2. `galvo_offset`
3. `machine_cycle`

`laser_params` 固定保留三组。第一组取页面值，第二、第三组逐字段复制参考样例。`galvo_offset` 逐字段复制参考样例。

每个 patch 生成一个 `machine_cycle` 项，引用 `[patch_index, 0]`。循环中的振镜名称、参数索引结构、XY 值和 `F40` 格式均沿用参考样例，仅依据固定层间步进重新计算 Z。

JSON 使用 UTF-8 编码、四空格缩进，不输出 NaN 或 Infinity。

## 校验与错误处理

开始生成前执行以下检查：

- 加工文件名留空时，在开始生成的瞬间自动填入 `machine_file_YYYYMMDD_HHMMSS`；解析后的名称不得包含路径分隔符，也不得为 `.` 或 `..`。
- 层间进给必须为大于零的有限数值。
- 第一组激光参数的整数字段不得为空或包含小数；布尔字段必须为布尔值。
- DXF 输出目录必须存在，并至少包含一个符合当前分层命名规则的 DXF。
- 层号必须唯一且连续。
- 每层必须至少包含一个 `LINE` 实体。
- 最终加工文件目录不得已经存在，现有目录不会被覆盖。

输出先写入目标父目录中与最终名称一一对应的隐藏临时目录 `.<加工文件名>.building`。启动前要求最终目录和该临时目录都不存在；只有全部 patch、JSON 和交叉引用验证通过后才将临时目录重命名为最终名称。正常失败由 Python 删除临时目录；用户取消并强制终止 Python 后，由 C# 仅删除本次已知的临时目录。所有失败路径都保留此前已成功生成的 TIFF 与 DXF。

验证条件包括：

- 每个 patch 的 dtype 为小端 `float32`，shape 为 `(N, 6)` 且 `N > 0`。
- 同一个 patch 中所有起点和终点 Z 相同。
- 相邻 patch 的 Z 差等于页面指定的固定下降深度。
- patch 数量、DXF 层数和 `machine_cycle` 数量一致。
- `machine_cycle` 引用的每个 `[patch_index, 0]` 都有对应文件。
- `laser_params[0]` 与页面值一致，另外两组与参考常量一致。

## 测试设计

Python 自动化测试覆盖：

- DXF `LINE` 的六个坐标按原顺序写入数组。
- 文件名层号排序不受字典序影响，例如第 2 层排在第 10 层之前。
- 默认 3 μm 和自定义下降深度的 patch Z、循环 Z 计算正确。
- NPY dtype、shape 和数值符合协议。
- JSON 三组激光参数、循环数量和 patch 引用正确。
- 无 LINE、重复层号、不连续层号、非法名称和已存在目标目录均安全失败。
- 中途失败不会留下最终名称的半成品目录。

C# 侧验证包括参数传递、日志展示、取消处理、成功后按钮状态，以及构建检查。最后使用当前工作区的一组真实分层 DXF 进行端到端生成，并通过独立读取脚本核对全部 patch 和 `machine.json`。

## 不在本次范围内

- 修改第二或第三组激光参数。
- 修改 `galvo_offset` 或 `F40`。
- 导入和打包任意第三方 DXF 格式。
- 改变参考加工文件的目录结构、JSON 字段或 NPY 行结构。
- 覆盖或合并已有加工文件目录。
