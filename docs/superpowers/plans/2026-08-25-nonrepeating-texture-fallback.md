# Nonrepeating Texture Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make default `unit` conversion fall back to the complete input grayscale image when no reliable two-axis repeat period exists.

**Architecture:** Introduce a narrow `RepeatPeriodNotFoundError` subtype for the expected period-detection miss. Catch only that type at the conversion boundary, reuse the existing full-image `repeat` fitting path, and report the effective processing mode in stdout; all unrelated failures continue to propagate.

**Tech Stack:** Python 3, NumPy, Pillow, `unittest`

## Global Constraints

- Reliable periodic inputs retain the existing minimum-repeat-unit behavior.
- Only “no reliable horizontal/vertical minimum repeat period” triggers fallback.
- Explicit `repeat` and `mirror` modes remain unchanged.
- Fallback uses the existing `fit_texture_to_size(..., tile_mode="repeat")` behavior.
- DPI, invalid dimensions, invalid parameters, and other processing errors must not be swallowed.
- No new UI option or scaling algorithm is introduced.

---

### Task 1: Detect and handle a missing repeat period

**Files:**
- Modify: `texture_to_hatch_dxf.py:994-1027`
- Modify: `texture_to_hatch_dxf.py:1923-2007`
- Test: `tests/test_texture_to_hatch_dxf.py`

**Interfaces:**
- Produces: `RepeatPeriodNotFoundError(ValueError)`, raised only when `_detect_axis_period` exhausts all reliable candidates.
- Consumes: existing `fit_texture_to_size(source, target_width_px, target_height_px, *, crop_anchor, tile_mode)`.
- Observable output: fallback conversion prints `处理方式: 未识别到重复周期，使用完整输入图` and `拼接模式: 完整输入图周期填充`.

- [ ] **Step 1: Add failing tests for the dedicated error and full-image fallback**

Add a `NonrepeatingTextureFallbackTests` class to `tests/test_texture_to_hatch_dxf.py`. Use a deterministic nonperiodic 6×6 binary image, save it at 25.4 DPI, call `convert_texture_to_dxf` with `tile_mode="unit"`, `voronoi_block_count=0`, and capture stdout. Assert that the DXF exists and is non-empty, stdout contains the two fallback messages, and stdout does not contain `处理方式: 自动识别最小重复单元`.

Also assert that direct axis detection raises the dedicated type:

```python
def test_axis_without_reliable_period_raises_dedicated_error(self) -> None:
    source = np.eye(6, dtype=bool)
    with self.assertRaises(hatch.RepeatPeriodNotFoundError):
        hatch._detect_axis_period(source, axis=1)
```

Add a periodic control case so the existing successful path cannot regress:

```python
def test_periodic_input_still_uses_minimum_repeat_unit(self) -> None:
    unit = np.array([[0, 255], [255, 0]], dtype=np.uint8)
    pixels = np.tile(unit, (3, 3))
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        input_path = root / "periodic.tiff"
        output_path = root / "periodic.dxf"
        Image.fromarray(pixels).save(input_path, dpi=(25.4, 25.4))
        stdout = io.StringIO()
        with redirect_stdout(stdout):
            convert_texture_to_dxf(
                input_path,
                output_path,
                6,
                6,
                1,
                tile_mode="unit",
                voronoi_block_count=0,
            )

        log = stdout.getvalue()
        self.assertIn("处理方式: 自动识别最小重复单元", log)
        self.assertIn("识别周期: 2 × 2 px", log)
        self.assertNotIn("未识别到重复周期", log)
```

Use this integration test body:

```python
def test_unit_mode_falls_back_to_complete_nonperiodic_input(self) -> None:
    pixels = np.array(
        [
            [0, 255, 255, 255, 255, 255],
            [255, 0, 255, 255, 255, 255],
            [255, 255, 0, 255, 255, 255],
            [255, 255, 255, 0, 255, 255],
            [255, 255, 255, 255, 0, 255],
            [255, 255, 255, 255, 255, 0],
        ],
        dtype=np.uint8,
    )
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        input_path = root / "nonperiodic.tiff"
        output_path = root / "nonperiodic.dxf"
        Image.fromarray(pixels).save(input_path, dpi=(25.4, 25.4))
        stdout = io.StringIO()
        with redirect_stdout(stdout):
            convert_texture_to_dxf(
                input_path,
                output_path,
                6,
                6,
                1,
                tile_mode="unit",
                voronoi_block_count=0,
            )

        self.assertGreater(output_path.stat().st_size, 0)
        log = stdout.getvalue()
        self.assertIn("处理方式: 未识别到重复周期，使用完整输入图", log)
        self.assertIn("拼接模式: 完整输入图周期填充", log)
        self.assertNotIn("处理方式: 自动识别最小重复单元", log)
```

- [ ] **Step 2: Add a failing test that unrelated errors still propagate**

Patch `detect_repeating_unit` to raise an ordinary `ValueError` and assert it escapes unchanged:

```python
def test_unit_mode_does_not_swallow_unrelated_value_error(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        input_path = root / "texture.tiff"
        Image.fromarray(np.zeros((6, 6), dtype=np.uint8)).save(
            input_path, dpi=(25.4, 25.4)
        )
        with (
            mock.patch.object(
                hatch,
                "detect_repeating_unit",
                side_effect=ValueError("unexpected unit extraction failure"),
            ),
            self.assertRaisesRegex(ValueError, "unexpected unit extraction failure"),
        ):
            convert_texture_to_dxf(
                input_path,
                root / "output.dxf",
                6,
                6,
                1,
                tile_mode="unit",
                voronoi_block_count=0,
            )
```

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.NonrepeatingTextureFallbackTests -v
```

Expected: the dedicated-error test fails because `RepeatPeriodNotFoundError` does not exist, and the integration test fails with the current “无法可靠识别横向最小重复周期” `ValueError`.

- [ ] **Step 4: Add the dedicated missing-period exception**

Add near the module constants:

```python
class RepeatPeriodNotFoundError(ValueError):
    """图片在指定方向上没有可可靠识别的重复周期。"""
```

Change only the final failure in `_detect_axis_period` from `raise ValueError(...)` to:

```python
raise RepeatPeriodNotFoundError(
    f"无法可靠识别{direction}最小重复周期；"
    "图片中至少需要包含两个重复单元，且重复单元应基本一致。"
)
```

Keep the existing “纹理尺寸太小” `ValueError` unchanged.

- [ ] **Step 5: Implement the narrow full-image fallback**

Initialize `used_full_source_fallback = False` beside `repeat_info`. In the `tile_mode == "unit"` branch, wrap only repeat detection and complete-unit fitting as follows:

```python
try:
    (
        unit,
        period_width,
        period_height,
        unit_x,
        unit_y,
        repeat_similarity,
        seam_score,
    ) = detect_repeating_unit(source)
except RepeatPeriodNotFoundError:
    fitted = fit_texture_to_size(
        source,
        target_width_px,
        target_height_px,
        crop_anchor=crop_anchor,
        tile_mode="repeat",
    )
    used_full_source_fallback = True
else:
    fitted, unit_columns, unit_rows = fit_complete_units_to_size(
        unit,
        target_width_px,
        target_height_px,
        crop_anchor=crop_anchor,
    )
    repeat_info = (
        period_width,
        period_height,
        unit_x,
        unit_y,
        repeat_similarity,
        seam_score,
        unit_columns,
        unit_rows,
    )
```

Replace the processing and mode log selection with values based on `used_full_source_fallback`:

```python
if used_full_source_fallback:
    print("处理方式: 未识别到重复周期，使用完整输入图")
elif tile_mode == "unit":
    print("处理方式: 自动识别最小重复单元")
else:
    print("处理方式: 传统纹理拼接")

mode_name = (
    "完整输入图周期填充"
    if used_full_source_fallback
    else mode_names[tile_mode]
)
print(f"拼接模式: {mode_name}")
```

- [ ] **Step 6: Run the focused tests and verify GREEN**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.NonrepeatingTextureFallbackTests -v
```

Expected: all four tests pass.

- [ ] **Step 7: Run the complete Python test suite**

Run:

```bash
python3 -m unittest discover -s tests -v
```

Expected: all tests pass with no errors or failures.

- [ ] **Step 8: Build the macOS app to refresh the bundled script**

Run:

```bash
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj
```

Expected: build succeeds, and `cmp -s texture_to_hatch_dxf.py GrayscaleLayersMac/bin/Debug/net10.0/texture_to_hatch_dxf.py` exits 0.

- [ ] **Step 9: Verify the reported TIFF end to end**

Read its physical dimensions from DPI, invoke `texture_to_hatch_dxf.py` in default `unit` mode with `--blocks 0` and a temporary output, and verify the command exits 0, creates a non-empty DXF, and prints both fallback messages. Keep this diagnostic output outside the repository.

- [ ] **Step 10: Commit the implementation**

```bash
git add texture_to_hatch_dxf.py tests/test_texture_to_hatch_dxf.py
git commit -m "fix: fill nonrepeating textures from complete input"
```
