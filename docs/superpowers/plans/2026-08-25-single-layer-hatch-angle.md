# Single-Layer Hatch Angle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a one-layer pipeline use the configured “层间角度递进” value as its Hatch angle while preserving the existing zero-based angle sequence for multi-layer pipelines.

**Architecture:** Keep the behavior at the existing C# orchestration boundary where each layer's `--angle` argument is calculated. Add one explicit single-layer branch to that calculation; do not change the Python Hatch generator or the independent texture-to-DXF workflow.

**Tech Stack:** C# 13, .NET 10, Avalonia 11.3.18, Python 3 standard-library `unittest` source-contract tests.

## Global Constraints

- A one-layer pipeline uses the configured step value modulo `180°`.
- A pipeline with more than one layer remains `0°`, one step, two steps, and so on, modulo `180°`.
- Only the three-step pipeline changes; the independent texture-to-Hatch-DXF page and Python Hatch algorithm remain unchanged.
- Do not add a new setting or rename the existing UI label.
- Preserve unrelated tracked and untracked workspace changes.

## File Structure

- `tests/test_texture_to_hatch_dxf.py`: add a focused source-contract regression test for the C# orchestration formula, following the test suite's existing pattern.
- `GrayscaleLayersMac/MainWindow.cs`: change the single angle calculation that supplies the per-layer `--angle` argument.
- `GrayscaleLayersMac/README.md`: document the single-layer exception and the unchanged multi-layer sequence.

---

### Task 1: Apply the single-layer angle exception

**Files:**
- Modify: `tests/test_texture_to_hatch_dxf.py`
- Modify: `GrayscaleLayersMac/MainWindow.cs:1189`
- Modify: `GrayscaleLayersMac/README.md:12`

**Interfaces:**
- Consumes: `layerFiles.Length`, the zero-based loop variable `index`, and the configured decimal `hatchAngleStep` already present in `RunPipelineAsync`.
- Produces: decimal local variable `layerHatchAngle`, passed unchanged to the existing `--angle` command-line argument.

- [ ] **Step 1: Write the failing source-contract test**

Add this class immediately before `AngledHatchTests` in `tests/test_texture_to_hatch_dxf.py`:

```python
class AvaloniaHatchAngleSourceContractTests(unittest.TestCase):
    def test_single_layer_uses_step_while_multiple_layers_keep_zero_based_sequence(self) -> None:
        source = (
            Path(__file__).resolve().parents[1]
            / "GrayscaleLayersMac"
            / "MainWindow.cs"
        ).read_text(encoding="utf-8")
        calculation_start = source.index("var layerHatchAngle")
        calculation_end = source.index("AppendPipelineLog", calculation_start)
        calculation = source[calculation_start:calculation_end]

        self.assertIn("layerFiles.Length == 1 ? 1 : index", calculation)
        self.assertIn("* hatchAngleStep", calculation)
        self.assertIn("% 180m", calculation)
```

The production change that makes this test pass is the addition of `layerFiles.Length == 1 ? 1 : index` to the current angle formula.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.AvaloniaHatchAngleSourceContractTests -v
```

Expected: FAIL because the current formula contains only `index * hatchAngleStep` and does not contain `layerFiles.Length == 1 ? 1 : index`.

- [ ] **Step 3: Implement the minimal C# behavior change**

Replace the current angle calculation in `GrayscaleLayersMac/MainWindow.cs` with:

```csharp
var layerHatchAngle = ((layerFiles.Length == 1 ? 1 : index) * hatchAngleStep) % 180m;
```

This maps the only layer to multiplier `1`, but retains multipliers `0, 1, 2, ...` whenever more than one layer exists.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.AvaloniaHatchAngleSourceContractTests -v
```

Expected: PASS.

- [ ] **Step 5: Update the behavior documentation**

Replace the angle-rule sentences in `GrayscaleLayersMac/README.md` with wording that states:

```text
主流程可设置“层间角度递进”：只生成一层时，该层直接使用设置的递进角度；生成多层时，第 1 层固定为水平 0°，后续各层依次增加一个递进角度。所有角度均按 180° 循环。
```

Keep the surrounding Voronoi, bidirectional-Hatch, preview, and area-description text unchanged.

- [ ] **Step 6: Run the relevant Python regression suite**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf -v
```

Expected: all tests PASS with no errors or failures.

- [ ] **Step 7: Build the Avalonia application**

Run:

```bash
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
```

Expected: build succeeds with `0 Error(s)`. If assets are not restored locally, rerun without `--no-restore`; request network permission only if dependency restoration requires it.

- [ ] **Step 8: Inspect the scoped diff**

Run:

```bash
git diff --check -- tests/test_texture_to_hatch_dxf.py GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac/README.md
git diff -- tests/test_texture_to_hatch_dxf.py GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac/README.md
```

Expected: no whitespace errors, and the diff contains only the regression test, one C# formula change, and the README clarification.

- [ ] **Step 9: Commit the implementation**

```bash
git add tests/test_texture_to_hatch_dxf.py GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac/README.md
git commit -m "fix: use configured hatch angle for single layer"
```

