# Machine File Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the macOS Avalonia pipeline so each TIFF/DXF batch also produces a sibling machine-file directory containing compatible `machine.json` and little-endian `float32` NPY patches, with a configurable fixed layer descent and editable first laser-parameter group.

**Architecture:** A new standalone Python module owns DXF discovery/parsing, patch construction, JSON construction, atomic directory generation, validation, and its CLI. The Avalonia application only owns controls, validation at the UI boundary, process invocation, progress/log output, cancellation, and opening the result. The existing TIFF and Hatch DXF algorithms remain unchanged.

**Tech Stack:** Python 3, NumPy, standard-library `argparse/json/pathlib/tempfile/unittest`; C# 13 / .NET 10; Avalonia 11.3.18.

## Global Constraints

- Preserve the reference directory contract: one `machine.json` plus `patches/<zero-based-index>_0.npy`.
- Every patch is little-endian `float32`, shape `(N, 6)`, with rows `[x1, y1, z1, x2, y2, z2]`.
- The default fixed descent is exactly `3 μm`; patch `i` uses `-i * step` and cycle `i` moves to `-(i + 1) * step`.
- The machine-file directory is a sibling of the selected DXF directory, never a child of it.
- Only `laser_params[0]` is editable; `laser_params[1]`, `laser_params[2]`, `galvo_offset`, and `F40` remain the reference values.
- Do not add `ezdxf`, `pyvista`, or any new runtime/package dependency.
- Do not overwrite an existing final machine-file directory.
- Generate in the final name's deterministic hidden sibling `.<output-name>.building` and rename only after validation; reject a pre-existing temp path, let Python clean normal failures, and let C# clean that exact path after forced cancellation.
- The input boundary is the minimal ASCII DXF emitted by `texture_to_hatch_dxf.py`; arbitrary third-party or binary DXF support is out of scope.
- The current workspace is not a Git repository. Replace commit steps with explicit changed-file checkpoints; do not initialize Git without user authorization.

---

## File Structure

- Create `dxf_to_machine_file.py`: public conversion API, DXF parser, constants, atomic generator, output validator, and CLI.
- Create `tests/test_dxf_to_machine_file.py`: Python unit and integration tests using temporary directories and minimal DXF fixtures.
- Modify `GrayscaleLayersMac/MainWindow.cs`: machine-export controls, first laser-group controls, third pipeline stage, logging, result path, and open-folder behavior.
- Modify `GrayscaleLayersMac/GrayscaleLayersMac.csproj`: copy `dxf_to_machine_file.py` into build and publish output.
- Modify `GrayscaleLayersMac/README.md`: document the three-step workflow, output layout, default descent, and editable laser group.

### Task 1: Parse Layered DXF into Typed Patch Arrays

**Files:**
- Create: `tests/test_dxf_to_machine_file.py`
- Create: `dxf_to_machine_file.py`

**Interfaces:**
- Produces: `extract_layer_number(path: Path) -> int`
- Produces: `discover_layer_dxf_files(dxf_dir: Path) -> list[Path]`
- Produces: `read_dxf_lines(path: Path) -> numpy.ndarray`
- Produces: `make_patch(lines: numpy.ndarray, patch_index: int, layer_step_um: float) -> numpy.ndarray`
- Consumes: current DXF naming convention `layer_<positive integer>_*.dxf` and LINE group codes `10/20/30/11/21/31`.

- [ ] **Step 1: Create a minimal failing test for DXF parsing and direction preservation**

Add a `write_dxf()` helper and this test to `tests/test_dxf_to_machine_file.py`:

```python
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import numpy as np

import dxf_to_machine_file as machine


def write_dxf(path: Path, rows: list[tuple[float, float, float, float, float, float]]) -> None:
    chunks = ["0\nSECTION\n2\nENTITIES\n"]
    for x1, y1, z1, x2, y2, z2 in rows:
        chunks.append(
            "0\nLINE\n"
            f"10\n{x1}\n20\n{y1}\n30\n{z1}\n"
            f"11\n{x2}\n21\n{y2}\n31\n{z2}\n"
        )
    chunks.append("0\nENDSEC\n0\nEOF\n")
    path.write_text("".join(chunks), encoding="ascii")


class DxfParsingTests(unittest.TestCase):
    def test_read_dxf_lines_preserves_entity_and_endpoint_order(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "layer_01_gray_lt_255.dxf"
            write_dxf(path, [(5, -1, 0, 2, -1, 0), (-3, 4, 0, 7, 4, 0)])

            actual = machine.read_dxf_lines(path)

        np.testing.assert_array_equal(
            actual,
            np.array([[5, -1, 0, 2, -1, 0], [-3, 4, 0, 7, 4, 0]], dtype=np.float64),
        )
```

- [ ] **Step 2: Run the focused test and verify the module is missing**

Run:

```bash
cd /Users/ccc/Desktop/preprocess
python3 -m unittest tests.test_dxf_to_machine_file.DxfParsingTests -v
```

Expected: FAIL with `ModuleNotFoundError: No module named 'dxf_to_machine_file'`.

- [ ] **Step 3: Implement streaming DXF group-pair parsing**

Create `dxf_to_machine_file.py` with `iter_group_pairs()` and `read_dxf_lines()`. Parse one code/value pair at a time, start a record on group code `0`, flush only `LINE` records, require all six coordinate codes, and return `float64` before the patch conversion:

```python
COORDINATE_CODES = ("10", "20", "30", "11", "21", "31")


def iter_group_pairs(path: Path):
    with path.open("r", encoding="utf-8", errors="strict", newline=None) as stream:
        while True:
            code = stream.readline()
            if code == "":
                return
            value = stream.readline()
            if value == "":
                raise ValueError(f"DXF 组码缺少对应值：{path}")
            yield code.strip(), value.strip()


def read_dxf_lines(path: Path) -> np.ndarray:
    rows: list[list[float]] = []
    entity_type: str | None = None
    fields: dict[str, str] = {}

    def flush() -> None:
        if entity_type != "LINE":
            return
        missing = [code for code in COORDINATE_CODES if code not in fields]
        if missing:
            raise ValueError(f"DXF LINE 缺少组码 {', '.join(missing)}：{path.name}")
        rows.append([float(fields[code]) for code in COORDINATE_CODES])

    for code, value in iter_group_pairs(path):
        if code == "0":
            flush()
            entity_type = value
            fields = {}
        elif entity_type == "LINE" and code in COORDINATE_CODES:
            fields[code] = value
    flush()
    if not rows:
        raise ValueError(f"DXF 不包含 LINE 实体：{path.name}")
    result = np.asarray(rows, dtype=np.float64)
    if not np.isfinite(result).all():
        raise ValueError(f"DXF 包含非有限坐标：{path.name}")
    return result
```

- [ ] **Step 4: Run the parsing test and verify it passes**

Run the command from Step 2.

Expected: one test passes.

- [ ] **Step 5: Add failing tests for numeric layer sorting and gaps**

Add tests that create `layer_10_x.dxf`, `layer_02_x.dxf`, and `layer_01_x.dxf`, assert the returned names are `01, 02, 10`, then separately assert `01, 03` raises `ValueError` containing `层号不连续`. Also assert duplicate logical layer numbers such as `layer_1_a.dxf` and `layer_01_b.dxf` raise `ValueError` containing `层号重复`.

```python
class LayerDiscoveryTests(unittest.TestCase):
    def test_discovers_layers_in_numeric_order(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name in ("layer_10_x.dxf", "layer_02_x.dxf", "layer_01_x.dxf"):
                write_dxf(root / name, [(0, 0, 0, 1, 0, 0)])
            paths = machine.discover_layer_dxf_files(root, require_contiguous=False)
        self.assertEqual([p.name for p in paths], ["layer_01_x.dxf", "layer_02_x.dxf", "layer_10_x.dxf"])

    def test_rejects_gap_in_layer_numbers(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name in ("layer_01_x.dxf", "layer_03_x.dxf"):
                write_dxf(root / name, [(0, 0, 0, 1, 0, 0)])
            with self.assertRaisesRegex(ValueError, "层号不连续"):
                machine.discover_layer_dxf_files(root)
```

- [ ] **Step 6: Implement strict file discovery**

Use `re.fullmatch(r"layer_(\d+)_.*\.dxf", path.name, re.IGNORECASE)`. Reject a missing/non-directory input, no matching files, duplicates after integer conversion, and any sequence not equal to `range(min_layer, max_layer + 1)`. Return paths sorted by extracted integer rather than lexical order. Provide the optional keyword-only `require_contiguous: bool = True` solely so the numeric-order test can isolate ordering.

- [ ] **Step 7: Add and pass patch dtype/Z tests**

Test `make_patch(lines, patch_index=2, layer_step_um=3)` and assert:

```python
self.assertEqual(patch.dtype, np.dtype("<f4"))
np.testing.assert_allclose(patch[:, 2], -0.006, rtol=0, atol=1e-8)
np.testing.assert_allclose(patch[:, 5], -0.006, rtol=0, atol=1e-8)
np.testing.assert_array_equal(patch[:, [0, 1, 3, 4]], lines[:, [0, 1, 3, 4]].astype("<f4"))
```

Implement `make_patch()` by validating `layer_step_um` is finite and positive, copying the six columns as `np.asarray(lines, dtype="<f4").copy()`, and replacing columns 2 and 5 with `np.float32(-patch_index * layer_step_um / 1000.0)`.

- [ ] **Step 8: Run Task 1 tests and record the changed-file checkpoint**

Run:

```bash
cd /Users/ccc/Desktop/preprocess
python3 -m unittest tests.test_dxf_to_machine_file -v
python3 -m py_compile dxf_to_machine_file.py tests/test_dxf_to_machine_file.py
```

Expected: all Task 1 tests pass and compilation exits 0. Record changed files `dxf_to_machine_file.py` and `tests/test_dxf_to_machine_file.py`; Git commit is unavailable in this workspace.

### Task 2: Generate and Validate the Atomic Machine-File Package

**Files:**
- Modify: `dxf_to_machine_file.py`
- Modify: `tests/test_dxf_to_machine_file.py`

**Interfaces:**
- Consumes: `discover_layer_dxf_files()`, `read_dxf_lines()`, and `make_patch()` from Task 1.
- Produces: `DEFAULT_LASER_PARAMS: tuple[dict[str, object], ...]`
- Produces: `DEFAULT_GALVO_OFFSET: dict[str, list[int]]`
- Produces: `build_machine_document(layer_count: int, layer_step_um: float, first_laser_params: dict[str, object]) -> dict[str, object]`
- Produces: `resolve_output_name(output_name: str | None, now: datetime | None = None) -> str`
- Produces: `validate_machine_directory(path: Path, layer_count: int, layer_step_um: float) -> None`
- Produces: `generate_machine_file(dxf_dir: Path, output_name: str | None, layer_step_um: float, first_laser_params: dict[str, object]) -> Path`
- Produces CLI positional arguments `dxf_dir`, `output_name` and named arguments matching every editable first-group field.

- [ ] **Step 1: Write failing tests for reference constants and Z cycles**

Assert the three parameter dictionaries exactly match the supplied sample. The immutable second and third groups are:

```python
{
    "frequency": 100, "power": 10, "pulseWidthIdx": 3,
    "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
    "scan_ahead": True, "accScale": 50, "cornerScale": 100,
    "endScale": 100, "sky_writing": False, "timeLag": 100,
    "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
}
```

```python
{
    "power": 20, "frequency": 350, "pulseWidthIdx": 4,
    "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
    "scan_ahead": True, "accScale": 50, "cornerScale": 100,
    "endScale": 100, "sky_writing": True, "timeLag": 100,
    "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
}
```

Call `build_machine_document(3, 3, custom_first)` and assert the command strings are exactly:

```python
[
    "G00X0.000Y0.000Z-0.003F40",
    "G00X0.000Y0.000Z-0.006F40",
    "G00X0.000Y0.000Z-0.009F40",
]
```

Assert references are `[0, 0]`, `[1, 0]`, `[2, 0]`, and only `laser_params[0]` equals `custom_first`.

- [ ] **Step 2: Implement document construction with copies of constants**

Define the sample first group with the 15 values documented in the design, the exact second/third dictionaries above, and `DEFAULT_GALVO_OFFSET = {"galvo_0": [0, 0, 0, 0]}`. Validate the first group has exactly the same keys and that every non-boolean value is an `int` but not a `bool`. Build cycles as:

```python
command = f"G00X0.000Y0.000Z{-((index + 1) * step_mm):.3f}F40"
cycle = {"galvo_0": [0, command, [index, 0]]}
```

Use `copy.deepcopy` for all constant structures so one invocation cannot mutate another.

- [ ] **Step 3: Write a failing end-to-end package test**

Create a parent directory with sibling `sample_dxf`, write two minimal DXFs, call:

```python
output = machine.generate_machine_file(
    dxf_dir,
    "sample_machine",
    3,
    dict(machine.DEFAULT_LASER_PARAMS[0], power=41),
)
```

Assert `output == parent / "sample_machine"`, both patch files exist, no temp directory remains, JSON reports power 41, and `np.load(..., allow_pickle=False)` returns `<f4` arrays with Z `0.0` and `-0.003`.

- [ ] **Step 4: Implement atomic generation and output validation**

Validate or resolve the name before filesystem writes:

```python
def resolve_output_name(output_name: str | None, now: datetime | None = None) -> str:
    name = (output_name or "").strip()
    if not name:
        name = (now or datetime.now()).strftime("machine_file_%Y%m%d_%H%M%S")
    if name in {".", ".."} or Path(name).name != name or "/" in name or "\\" in name:
        raise ValueError("加工文件名只能是名称，不能包含路径")
    return name
```

Set `final_path = dxf_dir.parent / resolved_name` and `temp_path = dxf_dir.parent / f".{resolved_name}.building"`; fail before creation when either exists. Create `temp_path/patches`, write each patch with `np.save`, write `machine.json` with `encoding="utf-8"`, `ensure_ascii=False`, `allow_nan=False`, and `indent=4`, call `validate_machine_directory`, then atomically rename `temp_path` to `final_path`. Wrap generation in `try/finally` and remove only this exact `temp_path` with `shutil.rmtree(temp_path)` if it still exists. This deterministic path lets the C# parent clean the same path if forced process termination prevents Python's `finally` from running.

`validate_machine_directory()` must reload every expected file with `allow_pickle=False`, check dtype/shape/finite values/uniform Z, compare adjacent Z values with `np.isclose`, load JSON, and verify cycle count plus every `[index, 0]` reference.

- [ ] **Step 5: Add failure-path tests**

Add individual tests that assert:

- Existing `parent / output_name` raises `FileExistsError` and its sentinel file remains unchanged.
- Names `../bad`, `a/b`, `a\\b`, `.`, and `..` raise `ValueError`.
- An empty name resolves to `machine_file_` plus a deterministic injected datetime.
- A layer without LINE raises `ValueError` and leaves neither the final directory nor `.<output-name>.building`.
- A non-positive or non-finite step raises `ValueError` before output creation.
- A first laser group with a missing key, extra key, boolean in an integer field, or float in an integer field raises `ValueError`.

- [ ] **Step 6: Add the CLI and test it as a subprocess**

Use positional `dxf_dir` and optional positional `output_name`, `--layer-step-um` defaulting to `3`, integer flags for all 13 numeric first-group fields, and Boolean optional actions for `--scan-ahead/--no-scan-ahead` and `--sky-writing/--no-sky-writing`. Set defaults from `DEFAULT_LASER_PARAMS[0]`.

The CLI prints parseable summary lines:

```text
加工文件生成完成
层数: 40
线段总数: 123456
Z 范围: -0.117000 ～ 0.000000 mm
输出目录: /absolute/path/machine_file_name
```

Add a subprocess test invoking `sys.executable`, the absolute script path, a two-layer fixture directory, `sample_machine`, `--layer-step-um 3`, and `--power 41`. Assert exit code 0, stdout includes the absolute sibling path, and JSON contains power 41.

- [ ] **Step 7: Run all Python tests and a real-data smoke conversion**

Run:

```bash
cd /Users/ccc/Desktop/preprocess
python3 -m unittest tests.test_dxf_to_machine_file -v
python3 dxf_to_machine_file.py 30X30-40C-240u-FK_dxf machine_file_plan_smoke --layer-step-um 3
```

Expected: all tests pass; the smoke command reports 40 layers and creates `/Users/ccc/Desktop/preprocess/machine_file_plan_smoke` as a sibling of the DXF directory. Load its first and last NPY and its JSON in a read-only verification command. After verification, move the smoke output to the macOS Trash rather than recursively deleting it.

- [ ] **Step 8: Record the changed-file checkpoint**

Record updated files `dxf_to_machine_file.py` and `tests/test_dxf_to_machine_file.py`, plus the smoke-test result and line/patch counts. Git commit is unavailable in this workspace.

### Task 3: Integrate Machine Export into the Avalonia Pipeline

**Files:**
- Modify: `GrayscaleLayersMac/MainWindow.cs`
- Modify: `GrayscaleLayersMac/GrayscaleLayersMac.csproj`
- Test: `tests/test_dxf_to_machine_file.py` remains the converter regression suite.

**Interfaces:**
- Consumes CLI from Task 2: `dxf_to_machine_file.py <dxf_dir> <output_name> --layer-step-um <decimal>` plus the 15 first-group laser flags.
- Produces: a third stage in `RunPipelineAsync()` and `_lastMachineOutputPath` used by the open-folder button.
- Produces: UI controls with default step `3` and the exact 15 sample defaults.

- [ ] **Step 1: Verify the baseline application builds before edits**

Run:

```bash
cd /Users/ccc/Desktop/preprocess/GrayscaleLayersMac
dotnet build
```

Expected: build succeeds. If it does not, stop and report the pre-existing failure rather than mixing it with this feature.

- [ ] **Step 2: Add the converter to application build and publish output**

Add this content item beside the two existing Python entries in `GrayscaleLayersMac.csproj`:

```xml
<Content Include="../dxf_to_machine_file.py"
         Link="dxf_to_machine_file.py"
         CopyToOutputDirectory="PreserveNewest"
         CopyToPublishDirectory="PreserveNewest" />
```

Build, then assert `bin/Debug/net10.0/dxf_to_machine_file.py` exists and matches the source checksum.

- [ ] **Step 3: Add machine-export controls with exact defaults**

At the other pipeline field declarations in `MainWindow.cs`, add:

```csharp
private readonly NumericUpDown _pipelineLayerStepUmBox = MakeNumberBox(3, 0.001m, 100000);
private readonly TextBox _pipelineMachineNameBox = new()
{
    Watermark = "留空则自动生成 machine_file_时间戳"
};
private readonly NumericUpDown _machinePowerBox = MakeNumberBox(38, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineFrequencyBox = MakeNumberBox(350, 1, int.MaxValue, 0);
private readonly NumericUpDown _machinePulseWidthIdxBox = MakeNumberBox(3, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineScanSpeedBox = MakeNumberBox(2100, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineJumpVelocityBox = MakeNumberBox(6000, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineJumpDelayBox = MakeNumberBox(50, 1, int.MaxValue, 0);
private readonly CheckBox _machineScanAheadBox = new() { Content = "扫描预读", IsChecked = true };
private readonly NumericUpDown _machineAccScaleBox = MakeNumberBox(50, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineCornerScaleBox = MakeNumberBox(100, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineEndScaleBox = MakeNumberBox(100, 1, int.MaxValue, 0);
private readonly CheckBox _machineSkyWritingBox = new() { Content = "空中书写", IsChecked = true };
private readonly NumericUpDown _machineTimeLagBox = MakeNumberBox(100, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineLaserOnShiftBox = MakeNumberBox(18, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineDelayLaserOffBox = MakeNumberBox(32, 1, int.MaxValue, 0);
private readonly NumericUpDown _machineDelayLaserOnBox = MakeNumberBox(0, 1, int.MaxValue, 0);
private string? _lastMachineOutputPath;
```

The existing `MakeNumberBox` helper fixes `Minimum = 0`; use it unchanged for these non-negative device parameters. All laser integers use increment 1, zero decimal places, and maximum `int.MaxValue`.

- [ ] **Step 4: Add the third-stage panel to the pipeline inspector**

Insert `MakeInspectorSection("机器加工文件", ...)` after the Voronoi panel and before progress/actions. It contains a two-column row for `每层下降深度（μm）` and `加工文件名`, followed by an `Expander` with header `第一组激光参数`. Inside the expander, use five three-column grids for the 13 integers and a horizontal row for the two Boolean checkboxes. Labels must show the Chinese name and JSON key, for example `功率（power）` and `频率（frequency）`.

Rename visible workflow copy from two steps to three steps:

```text
灰度分层 → Hatch DXF → 加工文件
先输出灰度分层 TIFF，再逐层生成 DXF，最后打包为机器加工文件。
开始三步处理
```

Change the existing open button caption to `打开加工文件目录`; its click handler calls `OpenDirectory(_lastMachineOutputPath)`.

- [ ] **Step 5: Add UI-boundary validation and CLI argument assembly**

At the beginning of `RunPipelineAsync()`, read the step and resolved name:

```csharp
var layerStepUm = _pipelineLayerStepUmBox.Value ?? 3;
if (layerStepUm <= 0)
{
    await ShowMessageAsync("每层下降深度必须大于 0 μm。");
    return;
}
var machineName = _pipelineMachineNameBox.Text?.Trim();
if (string.IsNullOrWhiteSpace(machineName))
{
    machineName = $"machine_file_{DateTime.Now:yyyyMMdd_HHmmss}";
    _pipelineMachineNameBox.Text = machineName;
}
if (machineName is "." or ".." ||
    machineName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\']) >= 0)
{
    await ShowMessageAsync("加工文件名不能包含路径分隔符。");
    return;
}
```

Read every laser integer with a local helper that rejects null, fractions, values outside `Int32`, and negative values. Build an ordered `(flag, value)` array using exact CLI names:

```text
--power --frequency --pulse-width-idx --scan-speed --jump-vel
--jump-delay --acc-scale --corner-scale --end-scale --time-lag
--laser-on-shift --delaseroff --delaseron
```

Append either `--scan-ahead` or `--no-scan-ahead`, and either `--sky-writing` or `--no-sky-writing`.

- [ ] **Step 6: Add the third process stage**

Resolve and verify the third script with the other two:

```csharp
var machineScript = Path.Combine(AppContext.BaseDirectory, "dxf_to_machine_file.py");
```

Before running, set `_lastMachineOutputPath = null` and disable the open button. Resolve the expected final path and deterministic temporary path from the absolute DXF parent and `machineName`; reject either when it already exists. After all DXFs finish, log `步骤 3/3：开始生成机器加工文件…`, construct a Python process, and append:

```text
machineScript, dxfOutput, machineName,
--layer-step-um, Invariant(layerStepUm),
<all first-group flags and values>
```

Run through the existing `RunProcessAsync` using the same cancellation token. On exit code zero, set:

```csharp
_lastMachineOutputPath = Path.Combine(
    Directory.GetParent(Path.GetFullPath(dxfOutput))!.FullName,
    machineName);
```

Require that directory to exist before reporting success. Update all stage logs from `1/2`, `2/2`, and `两步流程完成` to `1/3`, `2/3`, `3/3`, and `三步流程完成`. Only enable the open button after the final directory is verified.

- [ ] **Step 7: Verify cancellation and error paths remain safe**

Inspect the existing `try/catch/finally` path and confirm:

- Cancelling during stage 3 kills the Python process tree.
- Python owns cleanup after ordinary exceptions. After forced cancellation, C# removes only the exact `.<machineName>.building` path that was confirmed absent before this run and created for this invocation.
- `_lastMachineOutputPath` remains null on failure/cancellation.
- Existing TIFF and DXF output is not deleted.
- A converter error is shown in the pipeline log and message dialog.
- The run button and progress bar return to their idle states in `finally`.

- [ ] **Step 8: Build and run regression tests**

Run:

```bash
cd /Users/ccc/Desktop/preprocess
python3 -m unittest tests.test_dxf_to_machine_file -v
cd /Users/ccc/Desktop/preprocess/GrayscaleLayersMac
dotnet build
test -f bin/Debug/net10.0/dxf_to_machine_file.py
```

Expected: all Python tests pass, .NET build succeeds with zero errors, and the script exists in output.

- [ ] **Step 9: Record the changed-file checkpoint**

Record modified files `GrayscaleLayersMac/MainWindow.cs` and `GrayscaleLayersMac/GrayscaleLayersMac.csproj`, build result, and any warnings. Git commit is unavailable in this workspace.

### Task 4: Documentation and End-to-End Compatibility Verification

**Files:**
- Modify: `GrayscaleLayersMac/README.md`
- Verify: `dxf_to_machine_file.py`
- Verify: `tests/test_dxf_to_machine_file.py`
- Verify: `GrayscaleLayersMac/MainWindow.cs`
- Verify: `GrayscaleLayersMac/GrayscaleLayersMac.csproj`

**Interfaces:**
- Consumes: the complete three-step workflow from Tasks 1–3.
- Produces: user-facing operating instructions and recorded compatibility evidence.

- [ ] **Step 1: Update the README with exact behavior**

Change the opening feature list to say the main workflow has three stages. Add a `机器加工文件` section documenting:

```text
- 默认每层下降 3 μm，可在页面修改。
- patch i 的 Z 为 -i × 层间下降深度。
- machine_cycle i 移动到 -(i+1) × 层间下降深度。
- 仅第一组激光参数可编辑，另外两组保持设备样例值。
- 加工文件目录与 DXF 目录平级；留空名称时使用 machine_file_时间戳。
- 已存在的同名目录不会被覆盖。
```

Include the exact directory tree from the approved design and retain the existing Python dependency statement (`numpy pillow`) because the new converter adds no dependency.

- [ ] **Step 2: Run the full automated verification suite**

Run:

```bash
cd /Users/ccc/Desktop/preprocess
python3 -m unittest discover -s tests -v
python3 -m py_compile dxf_to_machine_file.py
cd /Users/ccc/Desktop/preprocess/GrayscaleLayersMac
dotnet build --no-restore
```

Expected: all tests pass, Python compilation exits 0, and .NET build has zero errors.

- [ ] **Step 3: Generate a real 40-layer package through the CLI**

Run the converter against `/Users/ccc/Desktop/preprocess/30X30-40C-240u-FK_dxf` with output name `machine_file_verification`, step 3, and a changed first-group power of 39. Expected sibling output: `/Users/ccc/Desktop/preprocess/machine_file_verification`.

Use a read-only Python verifier to assert:

```python
assert len(machine_json["machine_cycle"]) == 40
assert machine_json["laser_params"][0]["power"] == 39
assert machine_json["laser_params"][1] == DEFAULT_LASER_PARAMS[1]
assert machine_json["laser_params"][2] == DEFAULT_LASER_PARAMS[2]
assert len(list((output / "patches").glob("*_0.npy"))) == 40
assert np.load(output / "patches/0_0.npy", allow_pickle=False).dtype == np.dtype("<f4")
assert np.isclose(np.load(output / "patches/39_0.npy", allow_pickle=False)[0, 2], -0.117)
```

Also verify every cycle reference resolves to a patch file and every array has six columns with finite values.

- [ ] **Step 4: Perform focused UI QA**

Run the app with `dotnet run` and inspect the main pipeline page:

- The third-stage section is readable without clipped labels.
- The descent defaults to 3 μm.
- All 15 first-group fields show the sample defaults.
- Empty machine name becomes a timestamp name at run time.
- A custom name produces a directory beside the DXF directory.
- The log visibly advances through 1/3, 2/3, and 3/3.
- The open button remains disabled until stage 3 succeeds and opens the generated directory afterward.
- Cancelling stage 3 leaves no final half-built directory.

- [ ] **Step 5: Remove verification artifacts recoverably and record final evidence**

Move only `/Users/ccc/Desktop/preprocess/machine_file_verification` to the macOS Trash after all checks. Do not remove source TIFF, DXF, or the user-provided reference package. Record test counts, build result, verified patch count, first/last Z, output layout, and the exact files changed. Git commit is unavailable in this workspace.
