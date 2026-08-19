# Relative Block-Center Motion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate relative-mode machine G-code and, when requested, split every layer into block-local patches while moving machine XY by the relative difference between consecutive Voronoi block centers.

**Architecture:** The Hatch exporter writes a versioned `*.blocks.json` sidecar from the exact block order it writes into each DXF. The machine exporter turns all layers and sidecars into one ordered patch plan; that same plan drives local-coordinate NPY generation, relative `machine_cycle` commands, and post-write validation. The Avalonia pipeline exposes a default-enabled checkbox and passes the choice explicitly to the Python CLI.

**Tech Stack:** Python 3.9+, NumPy, standard-library `json`/`dataclasses`/`unittest`, C# with Avalonia on .NET 10.

## Global Constraints

- First G-code command starts with `G91`; the last ends with `G90`; no return-to-origin move is added.
- All machine moves are relative, use millimetres, retain three decimals, and end with `F40`.
- A block patch stores local XY (`global XY - block center`) but retains layer-absolute Z (`-layer_index × layer_step`).
- Four optional DXF inspection-border LINE entities remain in DXF and are excluded only from block-centre machine patches.
- Empty blocks generate neither patches nor machine moves.
- Missing, malformed, or inconsistent block metadata fails generation before final-directory publication.
- Without block-centre positioning, preserve one patch per layer and the current treatment of DXF lines, but change G-code Z to relative per-layer steps.
- Do not add third-party Python dependencies or change the final `machine.json` plus `patches/*.npy` directory contract.

---

## File Structure

- `texture_to_hatch_dxf.py`: own block metadata creation because this stage knows exact centers and output order.
- `dxf_to_machine_file.py`: own metadata validation, ordered patch planning, local coordinate conversion, relative G-code, publication, and final validation.
- `tests/test_texture_to_hatch_dxf.py`: verify sidecar contents and border/block ordering at the producer boundary.
- `tests/test_dxf_to_machine_file.py`: verify the patch plan, G-code sequence, failure cases, CLI, and generated artifacts.
- `GrayscaleLayersMac/MainWindow.cs`: expose the setting, synchronize its enabled state with the block-count control, and pass the CLI flag.
- `GrayscaleLayersMac/README.md`: document sidecars, relative movement, block-local patches, and checkbox behavior.

### Task 1: Produce exact block metadata beside each DXF

**Files:**
- Modify: `texture_to_hatch_dxf.py:25-45,610-841,844-998`
- Test: `tests/test_texture_to_hatch_dxf.py`

**Interfaces:**
- Produces: `block_metadata_path(dxf_path: Path) -> Path`
- Produces: `build_block_metadata(voronoi_blocks: list[VoronoiBlock], block_order: list[int], ordered_block_counts: list[int], border_line_count: int) -> dict[str, object]`
- Produces: `write_block_metadata(path: Path, document: dict[str, object]) -> None`
- Extends: `export_horizontal_hatch_dxf(..., block_metadata_output: Path | None = None) -> tuple[int, list[int]]`
- Consumed later: exact sidecar schema `{"version": 1, "border_line_count": int, "blocks": [...]}`

- [ ] **Step 1: Write failing producer tests**

Add imports for `json`, `PIL.Image`, `VoronoiBlock`, `block_metadata_path`, and `convert_texture_to_dxf`, then add tests that use deterministic hand-built blocks so assertions do not depend on random Voronoi generation:

```python
class BlockMetadataTests(unittest.TestCase):
    def test_writes_centers_counts_in_actual_dxf_block_order(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "layer_01.dxf"
            metadata = block_metadata_path(output)
            blocks = [
                VoronoiBlock(0, -2.0, -1.0, ((-5, -5), (0, -5), (0, 5), (-5, 5)), 50),
                VoronoiBlock(1, 2.0, 1.0, ((0, -5), (5, -5), (5, 5), (0, 5)), 50),
            ]
            _, counts = export_horizontal_hatch_dxf(
                np.ones((10, 10), dtype=bool), output,
                10, 10, 1, 1, 1,
                voronoi_blocks=blocks,
                block_metadata_output=metadata,
            )

            document = json.loads(metadata.read_text(encoding="utf-8"))
            self.assertEqual(document["version"], 1)
            self.assertEqual(document["border_line_count"], 0)
            self.assertEqual([block["line_count"] for block in document["blocks"]], counts)
            self.assertEqual(
                [(block["center_x"], block["center_y"]) for block in document["blocks"]],
                [(2.0, 1.0), (-2.0, -1.0)],
            )

    def test_records_four_border_lines_before_block_lines(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "layer_01.dxf"
            metadata = block_metadata_path(output)
            blocks = create_constrained_voronoi_blocks(10, 10, 2, random_seed=7)
            export_horizontal_hatch_dxf(
                np.ones((10, 10), dtype=bool), output,
                10, 10, 1, 1, 1,
                include_border=True,
                voronoi_blocks=blocks,
                block_metadata_output=metadata,
            )
            document = json.loads(metadata.read_text(encoding="utf-8"))
            self.assertEqual(document["border_line_count"], 4)
            self.assertEqual(
                4 + sum(block["line_count"] for block in document["blocks"]),
                len(read_line_coordinates(output)),
            )

    def test_convert_writes_sidecar_only_when_blocks_are_enabled(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            input_path = root / "texture.tiff"
            Image.fromarray(np.zeros((10, 10), dtype=np.uint8)).save(
                input_path, dpi=(25.4, 25.4)
            )
            blocked = root / "blocked.dxf"
            plain = root / "plain.dxf"
            common = dict(
                target_width_mm=10,
                target_height_mm=10,
                hatch_spacing_mm=1,
                tile_mode="repeat",
                min_block_area_mm2=0,
                max_block_area_mm2=100,
            )
            convert_texture_to_dxf(
                input_path, blocked, voronoi_block_count=2, **common
            )
            convert_texture_to_dxf(
                input_path, plain, voronoi_block_count=0, **common
            )
            self.assertTrue(block_metadata_path(blocked).is_file())
            self.assertFalse(block_metadata_path(plain).exists())
```

- [ ] **Step 2: Run the producer tests and verify RED**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.BlockMetadataTests -v
```

Expected: import or call failure because `block_metadata_path` and `block_metadata_output` do not exist.

- [ ] **Step 3: Implement the metadata model and sidecar writer**

Add:

```python
def block_metadata_path(dxf_path: Path) -> Path:
    return dxf_path.with_suffix(".blocks.json")


def build_block_metadata(
    voronoi_blocks: list[VoronoiBlock],
    block_order: list[int],
    ordered_block_counts: list[int],
    border_line_count: int,
) -> dict[str, object]:
    return {
        "version": 1,
        "border_line_count": border_line_count,
        "blocks": [
            {
                "block_index": voronoi_blocks[index].index,
                "center_x": voronoi_blocks[index].seed_x,
                "center_y": voronoi_blocks[index].seed_y,
                "line_count": count,
            }
            for index, count in zip(block_order, ordered_block_counts)
        ],
    }
```

Before the comprehension, reject unequal `block_order` and `ordered_block_counts` lengths. `write_block_metadata` must serialize with `ensure_ascii=False`, `allow_nan=False`, `indent=4`, write to a sibling temporary file opened with exclusive creation, flush and `os.fsync`, then use `os.replace` for the individual sidecar. `export_horizontal_hatch_dxf` writes the sidecar only after the DXF stream closes successfully. `convert_texture_to_dxf` supplies `block_metadata_path(output_path)` only when `voronoi_blocks` is non-empty and logs the sidecar path.

- [ ] **Step 4: Run focused and full producer tests**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.BlockMetadataTests -v
python3 -m unittest tests.test_texture_to_hatch_dxf -v
```

Expected: all tests pass with no warnings or leftover temporary files.

- [ ] **Step 5: Commit Task 1**

```bash
git add texture_to_hatch_dxf.py tests/test_texture_to_hatch_dxf.py
git commit -m "feat: export hatch block metadata"
```

### Task 2: Model patch placement and generate relative G-code

**Files:**
- Modify: `dxf_to_machine_file.py:20-125,203-285`
- Test: `tests/test_dxf_to_machine_file.py:127-229`

**Interfaces:**
- Produces: immutable `PatchPlacement(layer_index: int, center_x: float, center_y: float)`
- Produces: immutable `PlannedPatch(placement: PatchPlacement, lines: np.ndarray)`; `lines` remain source/global DXF coordinates.
- Produces: `build_machine_document(placements: list[PatchPlacement], layer_step_um: float, first_laser_params: dict[str, object]) -> dict[str, object]`
- Changes: `make_patch(lines: np.ndarray, layer_index: int, layer_step_um: float, center_x: float = 0.0, center_y: float = 0.0) -> np.ndarray`
- Changes: the existing unblocked `generate_machine_file` path creates one zero-centre `PlannedPatch` per layer.
- Changes: `validate_machine_directory` receives the expected plan rather than assuming patch index equals layer index.

- [ ] **Step 1: Write failing relative-motion tests**

Replace cumulative-cycle expectations and add exact block movement tests:

```python
def test_builds_relative_layer_cycles_with_mode_guards(self) -> None:
    placements = [PatchPlacement(index, 0.0, 0.0) for index in range(3)]
    document = build_machine_document(placements, 6, dict(DEFAULT_LASER_PARAMS[0]))
    self.assertEqual(
        [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
        [
            "G91G00X0.000Y0.000Z0.000F40",
            "G00X0.000Y0.000Z-0.006F40",
            "G00X0.000Y0.000Z-0.006F40G90",
        ],
    )

def test_moves_by_center_deltas_and_descends_only_on_layer_change(self) -> None:
    placements = [
        PatchPlacement(0, 10.0, 5.0),
        PatchPlacement(0, 18.0, 2.0),
        PatchPlacement(1, -4.0, 7.0),
    ]
    document = build_machine_document(placements, 6, dict(DEFAULT_LASER_PARAMS[0]))
    self.assertEqual(
        [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
        [
            "G91G00X10.000Y5.000Z0.000F40",
            "G00X8.000Y-3.000Z0.000F40",
            "G00X-22.000Y5.000Z-0.006F40G90",
        ],
    )

def test_single_patch_enters_and_leaves_relative_mode(self) -> None:
    document = build_machine_document(
        [PatchPlacement(0, 2.0, -3.0)], 6, dict(DEFAULT_LASER_PARAMS[0])
    )
    self.assertEqual(
        document["machine_cycle"][0]["galvo_0"][1],
        "G91G00X2.000Y-3.000Z0.000F40G90",
    )

def test_relative_deltas_accumulate_to_each_three_decimal_target(self) -> None:
    placements = [
        PatchPlacement(0, 0.0004, -0.0),
        PatchPlacement(0, 0.0008, -0.0001),
    ]
    document = build_machine_document(placements, 6, dict(DEFAULT_LASER_PARAMS[0]))
    self.assertEqual(
        [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
        [
            "G91G00X0.000Y0.000Z0.000F40",
            "G00X0.001Y0.000Z0.000F40G90",
        ],
    )

def test_make_patch_converts_xy_to_block_local_but_keeps_layer_z(self) -> None:
    lines = np.array([[11.0, 7.0, 0.0, 14.0, 3.0, 0.0]])
    patch = make_patch(lines, layer_index=2, layer_step_um=6, center_x=10, center_y=5)
    np.testing.assert_array_equal(
        patch,
        np.array([[1.0, 2.0, -0.012, 4.0, -2.0, -0.012]], dtype="<f4"),
    )
```

Also test rejection of decreasing layer indices, skipped layer indices, non-finite centers, booleans, and an empty placement list. Layers may repeat for multiple blocks but must start at `0` and advance only by `1`.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```bash
python3 -m unittest tests.test_dxf_to_machine_file.MakePatchTests tests.test_dxf_to_machine_file.MachineDocumentTests -v
```

Expected: failures because `PatchPlacement` is absent and current cycles use cumulative absolute Z without `G91`/`G90`.

- [ ] **Step 3: Implement placement validation, local patch conversion, and cycle construction**

Add the frozen dataclass and validate all placement values. Construct each command from state:

```python
previous_commanded_x = previous_commanded_y = 0.0
previous_layer = 0
for patch_index, placement in enumerate(placements):
    target_x = float(f"{placement.center_x:.3f}")
    target_y = float(f"{placement.center_y:.3f}")
    delta_x = target_x - previous_commanded_x
    delta_y = target_y - previous_commanded_y
    delta_z = 0.0 if placement.layer_index == previous_layer else -step_mm
    command = f"G00X{delta_x:.3f}Y{delta_y:.3f}Z{delta_z:.3f}F40"
    if patch_index == 0:
        command = "G91" + command
    if patch_index == len(placements) - 1:
        command += "G90"
```

After each command, update `previous_commanded_x/y` to the rounded target, not the unrounded metadata center. Normalize any value that formats to negative zero so the emitted text is always `0.000`, never `-0.000`. This ensures cumulative relative commands land on each center's three-decimal machine target instead of accumulating per-delta rounding drift.

Do not derive patch Z from global patch number; use `layer_index`. Subtract `center_x` from columns `0` and `3`, and `center_y` from columns `1` and `4`, before casting/writing little-endian float32. Reject a transformed patch containing non-finite values.

Migrate the existing unblocked generator in the same step: create one `PlannedPatch(PatchPlacement(index, 0.0, 0.0), read_dxf_lines(layer_file))` per layer, pass each placement and source lines to `make_patch`, build the document from the plan's placements, and validate against that same plan. Validator recomputes every expected NPY array with `make_patch`, compares it to the reloaded array, and derives expected cycles from the plan's placements.

- [ ] **Step 4: Run focused and full machine tests**

Run:

```bash
python3 -m unittest tests.test_dxf_to_machine_file.MakePatchTests tests.test_dxf_to_machine_file.MachineDocumentTests -v
python3 -m unittest tests.test_dxf_to_machine_file -v
```

Expected: every machine-file test passes. Existing package and CLI assertions now expect the relative-mode sequence, including `G91` on the first command, a fixed one-step Z delta on later layers, and `G90` on the last command.

- [ ] **Step 5: Commit Task 2**

```bash
git add dxf_to_machine_file.py tests/test_dxf_to_machine_file.py
git commit -m "feat: generate relative machine motion"
```

### Task 3: Consume sidecars and build validated block-local packages

**Files:**
- Modify: `dxf_to_machine_file.py:125-365,440-500`
- Test: `tests/test_dxf_to_machine_file.py:290-825`

**Interfaces:**
- Produces: immutable `BlockDefinition(block_index: int, center_x: float, center_y: float, line_count: int)`
- Produces: immutable `BlockMetadata(border_line_count: int, blocks: tuple[BlockDefinition, ...])`
- Produces: `read_block_metadata(dxf_path: Path) -> BlockMetadata`
- Produces: `build_patch_plan(layer_files: list[Path], block_center_positioning: bool) -> list[PlannedPatch]`
- Changes: `generate_machine_file(..., block_center_positioning: bool = False) -> Path`
- Consumes: `validate_machine_directory(..., expected_plan: list[PlannedPatch]) -> None` from Task 2
- Adds CLI: `--block-center-positioning` / `--no-block-center-positioning`, default `False` for direct CLI backward compatibility.

- [ ] **Step 1: Write failing metadata-validation and patch-plan tests**

Add a helper that writes exact sidecars and tests these real behaviors:

```python
def write_block_metadata(path: Path, border: int, blocks: list[dict[str, object]]) -> None:
    path.with_suffix(".blocks.json").write_text(
        json.dumps({"version": 1, "border_line_count": border, "blocks": blocks}),
        encoding="utf-8",
    )


def test_block_plan_excludes_border_skips_empty_and_localizes_xy(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        dxf = Path(directory) / "layer_1_a.dxf"
        write_dxf(dxf, [
            (-5, -5, 0, 5, -5, 0), (5, -5, 0, 5, 5, 0),
            (5, 5, 0, -5, 5, 0), (-5, 5, 0, -5, -5, 0),
            (11, 7, 0, 14, 3, 0), (20, 8, 0, 23, 9, 0),
        ])
        write_block_metadata(dxf, 4, [
            {"block_index": 4, "center_x": 10.0, "center_y": 5.0, "line_count": 1},
            {"block_index": 7, "center_x": 15.0, "center_y": 6.0, "line_count": 0},
            {"block_index": 2, "center_x": 20.0, "center_y": 8.0, "line_count": 1},
        ])
        plan = build_patch_plan([dxf], block_center_positioning=True)
        self.assertEqual([item.placement for item in plan], [
            PatchPlacement(0, 10.0, 5.0), PatchPlacement(0, 20.0, 8.0)
        ])
        patch = make_patch(
            plan[0].lines,
            layer_index=plan[0].placement.layer_index,
            layer_step_um=6,
            center_x=plan[0].placement.center_x,
            center_y=plan[0].placement.center_y,
        )
        np.testing.assert_array_equal(patch[0, [0, 1, 3, 4]], [1, 2, 4, -2])
```

Add subtests proving rejection of: missing sidecar, malformed JSON, non-regular path, version other than integer `1`, missing/extra fields, boolean counts, negative counts, duplicate block indices, NaN/Infinity centers, total LINE mismatch, and all-empty blocks.

- [ ] **Step 2: Run patch-plan tests and verify RED**

Run:

```bash
python3 -m unittest tests.test_dxf_to_machine_file.BlockMetadataTests tests.test_dxf_to_machine_file.PatchPlanTests -v
```

Expected: failures because the reader and plan builder do not exist.

- [ ] **Step 3: Implement strict metadata parsing and patch planning**

Use exact-key checks at every JSON object level. Resolve the sidecar exclusively as `dxf_path.with_suffix(".blocks.json")`; open it without following directories, parse with standard `json`, validate types using `type(value) is int` and finite-number checks that reject booleans. Slice LINE arrays by `border_line_count` and each `line_count`, then verify the final cursor equals `len(lines)`.

For unblocked planning, create one `PlannedPatch(PatchPlacement(layer_index, 0, 0), all_lines)` per layer and never open sidecars. For blocked planning, retain each nonempty source/global LINE slice with its layer index and center; `make_patch` remains the single place that performs local-coordinate conversion.

- [ ] **Step 4: Write failing package, validation, and CLI tests**

Add a two-layer/two-block integration fixture with different centers on the second layer. Assert:

```python
self.assertEqual(sorted(path.name for path in patches.iterdir()), [
    "0_0.npy", "1_0.npy", "2_0.npy", "3_0.npy"
])
self.assertEqual(commands, [
    "G91G00X10.000Y5.000Z0.000F40",
    "G00X8.000Y-3.000Z0.000F40",
    "G00X-22.000Y5.000Z-0.006F40",
    "G00X3.000Y4.000Z0.000F40G90",
])
```

Update existing unblocked expectations to include `G91`, fixed per-layer Z deltas, and final `G90`. Add a CLI test invoking `--block-center-positioning`, and a negative CLI test proving missing metadata returns nonzero and publishes no final directory.

- [ ] **Step 5: Run integration tests and verify RED**

Run:

```bash
python3 -m unittest tests.test_dxf_to_machine_file.GenerateMachineFileTests tests.test_dxf_to_machine_file.ValidateMachineDirectoryTests tests.test_dxf_to_machine_file.CliTests -v
```

Expected: blocked-package and CLI tests fail because `generate_machine_file` does not yet accept or use the new block-centre option; existing unblocked relative-mode tests remain green.

- [ ] **Step 6: Integrate the patch plan into generation and validation**

Build the plan once before writing files. For each `PlannedPatch`, call `make_patch(item.lines, item.placement.layer_index, layer_step_um, item.placement.center_x, item.placement.center_y)` and write `patches/<global_index>_0.npy`. Pass `[item.placement for item in plan]` to `build_machine_document` and pass the full plan to `validate_machine_directory`.

Validation must check exact patch filenames, shape/dtype/finite values, exact reloaded patch contents against a freshly recomputed patch from the plan, document cycles against the placement-derived expected document, and each `[patch_index, 0]` reference. It must not require adjacent patches to differ in Z because same-layer block patches correctly have identical Z.

Add the BooleanOptionalAction CLI flag and forward it into `generate_machine_file`. Print both layer count and patch count so block mode is visible in logs.

- [ ] **Step 7: Run the complete Python suite**

Run:

```bash
python3 -m unittest discover -s tests -v
```

Expected: all Python tests pass with zero failures and zero errors.

- [ ] **Step 8: Commit Task 3**

```bash
git add dxf_to_machine_file.py tests/test_dxf_to_machine_file.py
git commit -m "feat: package block-local machine patches"
```

### Task 4: Wire the Avalonia setting and document operator behavior

**Files:**
- Modify: `GrayscaleLayersMac/MainWindow.cs:85-135,380-470,1040-1095,1300-1385,1737-1760`
- Modify: `GrayscaleLayersMac/README.md:8-45`

**Interfaces:**
- Adds UI field: `_pipelineBlockCenterMotionBox: CheckBox`
- Adds helper: `UpdateBlockCenterMotionAvailability()`
- Passes CLI argument: `--block-center-positioning` or `--no-block-center-positioning`

- [ ] **Step 1: Add an operator-level source contract check and run it RED**

Before editing C#, verify the required setting and argument are absent:

```bash
rg -n "按加工块中心移动 XY|block-center-positioning" GrayscaleLayersMac/MainWindow.cs
```

Expected: exit code `1` and no matches. This is a deliberate UI wiring gate; Python behavior is already covered by executable tests in Tasks 1–3.

- [ ] **Step 2: Add the checkbox and availability synchronization**

Declare:

```csharp
private readonly CheckBox _pipelineBlockCenterMotionBox = new()
{
    Content = "按加工块中心移动 XY",
    IsChecked = true
};
```

Place it in the “机器加工文件” section near layer descent. Subscribe to `_pipelineBlocksBox.ValueChanged` after controls are created, and implement:

```csharp
private void UpdateBlockCenterMotionAvailability()
{
    _pipelineBlockCenterMotionBox.IsEnabled = (_pipelineBlocksBox.Value ?? 0) > 0;
}
```

Call the helper once during window construction. Do not overwrite `IsChecked` while disabling it; this preserves the user's choice if block count changes from positive to `0` and back.

- [ ] **Step 3: Pass the explicit effective choice to Python**

Compute:

```csharp
var useBlockCenterMotion =
    (_pipelineBlocksBox.Value ?? 0) > 0 &&
    _pipelineBlockCenterMotionBox.IsChecked == true;
```

Append exactly one of these after all fixed machine arguments:

```csharp
machineInfo.ArgumentList.Add(
    useBlockCenterMotion
        ? "--block-center-positioning"
        : "--no-block-center-positioning");
```

Add a pipeline log line stating whether block-centre XY positioning is enabled.

- [ ] **Step 4: Verify source wiring and compile the app**

Run:

```bash
rg -n "按加工块中心移动 XY|block-center-positioning|UpdateBlockCenterMotionAvailability" GrayscaleLayersMac/MainWindow.cs
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
```

Expected: the setting, both effective-state branches, and helper are present; build exits `0` with no errors.

- [ ] **Step 5: Update operator documentation**

Document these exact behaviors in `GrayscaleLayersMac/README.md`:

- Blocked DXFs have `*.blocks.json` sidecars used only during machine packaging.
- The checkbox defaults checked and is unavailable when block count is `0`.
- Block patches use local XY; machine XY uses consecutive-center relative deltas.
- Machine Z descends once per layer, not once per block.
- Inspection borders remain in DXF but are excluded from block patches.
- Processing begins in `G91`, ends by restoring `G90`, and does not return to origin.

- [ ] **Step 6: Run final automated verification**

Run fresh:

```bash
python3 -m unittest discover -s tests -v
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
git diff --check
```

Expected: all Python tests pass, the Avalonia app builds with zero errors, and Git reports no whitespace errors.

- [ ] **Step 7: Run a real two-layer blocked smoke test**

Use the existing TIFF input and CLI tools in a temporary directory. Generate two DXFs with two deterministic blocks but different seeds, then generate the machine package with `--block-center-positioning`. Check with a short read-only Python command that:

- four or fewer patches exist depending only on genuinely empty blocks;
- patch count equals total nonempty metadata blocks;
- cumulative XY deltas equal each processed block center within `0.001 mm` formatting tolerance;
- only the first patch of layer two has `Z=-step`, while other same-layer block moves have `Z0.000`;
- the first command begins `G91`, the last ends `G90`;
- no block-local patch contains any of the four full-frame border lines.

- [ ] **Step 8: Commit Task 4**

```bash
git add GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac/README.md
git commit -m "feat: expose block center machine motion"
```

### Task 5: Review the implementation against the approved specification

**Files:**
- Review: `docs/superpowers/specs/2026-08-19-relative-block-center-motion-design.md`
- Review: all files changed in Tasks 1–4

**Interfaces:**
- Consumes: the approved design and fresh verification results.
- Produces: an evidence-backed completion report with any remaining gaps stated explicitly.

- [ ] **Step 1: Compare every specification requirement to code and tests**

Read the approved design from top to bottom and map each requirement to a production path and either an automated assertion or the bounded manual smoke test. Fix any uncovered requirement through a new RED/GREEN cycle before proceeding.

- [ ] **Step 2: Inspect the final diff for unintended scope**

Run:

```bash
git diff 935b522..HEAD --stat
git diff 935b522..HEAD -- texture_to_hatch_dxf.py dxf_to_machine_file.py GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac/README.md tests
git status --short
```

Expected: only feature, test, and documentation changes are present; the user's untracked `machine_file_20260819_090133.zip` remains untouched.

- [ ] **Step 3: Invoke verification-before-completion and rerun final commands**

Run fresh in the same turn that reports completion:

```bash
python3 -m unittest discover -s tests -v
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
git diff --check
```

Only report success if all three commands exit `0`; otherwise report the actual failure and continue the relevant RED/GREEN cycle.
