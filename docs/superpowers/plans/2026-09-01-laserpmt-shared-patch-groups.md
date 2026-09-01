# LaserPMT 共享二维 Patch 组实现计划

**目标：** 将 LaserPMT patch 引用从展平的 `[global_index, 0]` 改为可共享的 `[group_index, local_index]`，并确保完全相同的生成后 patch 组只写出一次。

**设计规格：** `docs/superpowers/specs/2026-09-01-laserpmt-shared-patch-groups-design.md`

## 任务 1：先固定新输出契约

- 修改 `tests/test_laser_pmt.py`。
- 把现有生成测试的预期改为二维引用和去重后的文件集合。
- 增加相同 NPY 组共享、不同层进给产生不同组、首次出现顺序稳定的覆盖。
- 运行 `python3 -m pytest -q tests/test_laser_pmt.py`，确认新测试在旧实现上按预期失败。

## 任务 2：实现内容组生成与复用

- 修改 `laser_pmt.py`。
- 为单个 PMT 先构建完整的生成后数组组。
- 使用 dtype、shape 和 `numpy.array_equal` 做逐数组精确比较。
- 按首次出现顺序分配内容组编号，仅为新内容组写出 `patches/x_y.npy`。
- 将独立 JSON、`allmachine.json` 和布局元数据统一改为二维引用。

## 任务 3：升级生成包校验与 CSV

- 修改 `laser_pmt.py`。
- 根据布局中的二维引用推导精确文件集合。
- 校验 JSON 引用、数组内容、Z、局部/全局运动及共享关系。
- 将 CSV 中的引用序列化为 `x_y;x_y`。

## 任务 4：文档和回归验证

- 更新 `GrayscaleLayersMac/README.md` 的输出说明和示例。
- 运行 `python3 -m pytest -q tests/test_laser_pmt.py tests/test_dxf_to_machine_file.py`。
- 运行 `python3 -m py_compile laser_pmt.py`。
- 检查 `git diff --check` 和最终差异，只提交本功能相关文件。
