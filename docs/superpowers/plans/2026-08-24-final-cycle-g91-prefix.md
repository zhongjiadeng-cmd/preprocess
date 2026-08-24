# Final Cycle G91 Prefix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every newly generated final machine cycle explicitly enter `G91` before its relative motion while retaining the trailing `G90`.

**Architecture:** Keep the existing `machine_cycle` structure and relative-delta calculations unchanged. Update the document builder and the independent vendor grammar simulator to share the new first/final mode-guard contract, then update exact-string regression expectations across package and CLI tests.

**Tech Stack:** Python 3, `unittest`, NumPy, .NET/Avalonia build verification.

## Global Constraints

- A multi-cycle final command is exactly `G91G00X...Y...Z...F40G90`.
- A single-cycle command remains exactly one `G91` prefix: `G91G00X...Y...Z...F40G90`.
- Middle commands remain `G00X...Y...Z...F40` without a redundant `G91`.
- Relative X/Y/Z calculations, patch contents, references, and the trailing `G90` do not change.
- Existing generated `machine.json` and patch files must not be modified.

---

### Task 1: Final-cycle mode guard and strict parser

**Files:**
- Modify: `tests/test_dxf_to_machine_file.py:240-480`
- Modify: `dxf_to_machine_file.py:207-343`

**Interfaces:**
- Consumes: `build_machine_document(placements: list[PatchPlacement], layer_step_um: float, first_laser_params: dict[str, object]) -> dict[str, object]`
- Produces: final commands prefixed with `G91`; `_simulate_vendor_machine_cycles(machine_cycles: object) -> list[tuple[Decimal, Decimal, Decimal]]` requiring that prefix on the final cycle.

- [ ] **Step 1: Add a focused failing builder regression test**

Add to `MachineDocumentTests`:

```python
def test_reasserts_relative_mode_on_final_multi_patch_cycle(self) -> None:
    document = build_machine_document(
        [PatchPlacement(0, 1.0, 2.0), PatchPlacement(0, 4.0, 6.0)],
        3,
        dict(DEFAULT_LASER_PARAMS[0]),
    )

    self.assertEqual(
        [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
        [
            "G91G00X1.000Y2.000Z0.000F40",
            "G91G00X3.000Y4.000Z0.000F40G90",
        ],
    )
```

- [ ] **Step 2: Run the regression test and verify RED**

Run:

```bash
python3 -m unittest tests.test_dxf_to_machine_file.MachineDocumentTests.test_reasserts_relative_mode_on_final_multi_patch_cycle -v
```

Expected: FAIL because the actual final command starts with `G00`, not `G91G00`.

- [ ] **Step 3: Implement the minimal builder change**

In `build_machine_document`, replace the first-cycle-only prefix condition with:

```python
if patch_index == 0 or patch_index == len(placements) - 1:
    command = "G91" + command
if patch_index == len(placements) - 1:
    command += "G90"
```

The single-cycle case passes through the prefix condition once, so it cannot produce `G91G91`.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2.

Expected: PASS.

- [ ] **Step 5: Add a focused failing strict-parser regression**

Change `VendorCommandSimulatorTests.test_executes_final_motion_before_trailing_g90_in_vendor_lexical_order` so its final command is:

```python
{"galvo_0": [0, "G91G00X-2.000Y3.000Z-0.006F40G90", [1, 0]]}
```

The expected cumulative state remains `(8.000, 8.000, -0.006)`.

- [ ] **Step 6: Run the parser regression and verify RED**

Run:

```bash
python3 -m unittest tests.test_dxf_to_machine_file.VendorCommandSimulatorTests.test_executes_final_motion_before_trailing_g90_in_vendor_lexical_order -v
```

Expected: ERROR with an unsupported or misplaced `G91` token on the final cycle.

- [ ] **Step 7: Implement the strict final-prefix grammar**

In `_simulate_vendor_machine_cycles`, replace the first-cycle-only prefix parsing with:

```python
if cycle_index == 0 or cycle_index == final_index:
    cursor = _consume_vendor_literal(command, cursor, "G91")
    absolute_mode = False
```

Keep motion execution at `F40` and trailing-final `G90` parsing unchanged. This makes a missing final `G91` fail through the same restricted grammar.

- [ ] **Step 8: Update exact command expectations**

In `tests/test_dxf_to_machine_file.py`, update every multi-cycle final command expectation and valid simulator fixture from `G00...F40G90` to `G91G00...F40G90`. Preserve single-cycle strings, first-cycle strings, middle-cycle strings, and deliberately invalid fixtures whose purpose is to prove that a missing final `G91` is rejected.

- [ ] **Step 9: Run the focused builder and simulator classes**

Run:

```bash
python3 -m unittest \
  tests.test_dxf_to_machine_file.MachineDocumentTests \
  tests.test_dxf_to_machine_file.VendorCommandSimulatorTests -v
```

Expected: all tests PASS with no errors.

- [ ] **Step 10: Commit the behavior change**

```bash
git add dxf_to_machine_file.py tests/test_dxf_to_machine_file.py
git commit -m "fix: reassert relative mode on final machine cycle"
```

---

### Task 2: Package-level regression and full verification

**Files:**
- Modify: `tests/test_dxf_to_machine_file.py:900-970`
- Modify: `tests/test_dxf_to_machine_file.py:1500-1820`

**Interfaces:**
- Consumes: `generate_machine_file(...) -> Path` and the final-cycle contract from Task 1.
- Produces: package and CLI regression coverage proving newly generated files contain the final `G91` prefix.

- [ ] **Step 1: Confirm package and CLI expectations use the new contract**

Ensure generated-package tests assert examples such as:

```python
"G91G00X3.000Y4.000Z0.000F40G90"
```

and:

```python
"G91G00X0.000Y0.000Z-0.005F40G90"
```

Do not open or rewrite either existing `machine_file_*/machine.json` directory.

- [ ] **Step 2: Run the complete Python test suite**

Run:

```bash
python3 -m unittest tests.test_dxf_to_machine_file tests.test_texture_to_hatch_dxf -v
```

Expected: all tests PASS with no failures or errors.

- [ ] **Step 3: Build the macOS application**

Run:

```bash
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Verify scope and generated artifacts remain untouched**

Run:

```bash
git status --short
git diff --check
git diff --name-only HEAD -- machine_file_20260820_194502 machine_file_20260824_091304
```

Expected: no output from the machine-file diff command; no whitespace errors; only intended source, test, and plan/spec changes are present apart from pre-existing untracked user files.

- [ ] **Step 5: Commit package-level test adjustments if not included in Task 1**

```bash
git add tests/test_dxf_to_machine_file.py
git commit -m "test: cover final cycle relative mode guard"
```
