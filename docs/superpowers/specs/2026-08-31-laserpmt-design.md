# LaserPMT Parameter Matrix Design

## Goal

Add a fourth pipeline step, LaserPMT, after the existing machine-file export. LaserPMT expands user-supplied machining parameter values into a Cartesian product, lays the resulting jobs out as a physical matrix on a workpiece, and exports both independently runnable numbered machine JSON files and a separately generated `allmachine.json` that processes the complete matrix in number order.

The existing texture and DXF views and their behavior remain unchanged. A third PMT view is added to show a lightweight wireframe of the workpiece and the numbered machining jobs placed on it.

## Scope

LaserPMT supports:

- Any number of simultaneously varying parameters.
- Explicit, user-entered value lists rather than start/end/step ranges.
- Every currently editable field in the first laser parameter group.
- Boolean value lists for `scan_ahead` and `sky_writing`.
- Layer feed in micrometres as an additional matrix parameter.
- A maximum of 1,000 Cartesian-product combinations in the first release.
- Automatic two-dimensional layout from a user-selected column count and workpiece dimensions.
- Importing an existing valid machine-file directory and running LaserPMT without rerunning steps 1–3.

Texture size, Hatch spacing, Hatch angle, grayscale settings, and Voronoi/block-generation settings are not matrix parameters. Every matrix item uses the same source geometry, block order, fill-line order, and layer order.

## Pipeline Integration

The main workflow becomes:

1. Generate grayscale layers.
2. Generate DXF files.
3. Generate the base machine-file directory.
4. Generate LaserPMT.

The main split button behaves as follows:

- **Run all** executes steps 1 through 4 in order.
- The step menu gains **Step 4: Generate LaserPMT**.
- The import menu gains an action for importing a machine-file directory containing a valid `machine.json` and `patches/` directory.

Step 4 defaults to the result of step 3. An explicitly imported machine-file directory may be used instead. The operation uses the existing cancellable pipeline progress overlay and reports progress for combination expansion, numbered-file generation, all-file generation, and final validation.

## Architecture

### Python generation engine

Add a focused `laser_pmt.py` module. It reuses public or extracted reusable primitives from `dxf_to_machine_file.py` for machine JSON construction, patch loading/writing, vendor-motion semantics, and strict validation. It owns only LaserPMT concerns:

- Parsing and validating a LaserPMT request.
- Expanding parameter value lists into an ordered Cartesian product.
- Computing the workpiece layout.
- Regenerating per-job patches when layer feed varies.
- Generating each numbered machine JSON from local coordinates.
- Independently generating global `allmachine.json` motion.
- Writing the parameter map and preview layout manifest.
- Validating and atomically publishing the complete package.

The ordinary machine-file command continues to behave as it does today.

### C# configuration domain

Add focused C# types outside `MainWindow.cs` for:

- Parameter definitions and type/range validation.
- Explicit value-list parsing.
- Cartesian-product sizing and ordering.
- Number formatting.
- Workpiece layout calculation.
- Generation readiness and error reporting.

These types must not depend on Avalonia controls, so their behavior can be tested without constructing the UI.

### PMT preview

Add a dedicated PMT preview model and Avalonia control. The control consumes the same typed layout model that is serialized to and loaded from `pmt-layout.json`, and draws only:

- The workpiece outline.
- Each machining job's rectangular footprint.
- Job number labels.
- The selected-job highlight.
- Dimension and spacing annotations where legible.

It does not parse or render texture pixels, DXF entities, or NPY patch contents.

## Parameter Model and Combination Order

The parameter editor contains zero or more rows. Each row selects one unique parameter and accepts a comma-separated list of explicit values, such as:

```text
power: 20, 30, 40
scanSpeed: 1000, 1500
frequency: 300, 350
layerFeedUm: 2, 3
```

Whitespace around values is ignored. Empty values, repeated values within one list, duplicate parameter rows, invalid numeric values, and invalid booleans are rejected with clear errors. Values are never silently deduplicated because that would change the user's requested matrix.

Combination order follows the visible parameter-row order. The last parameter changes fastest. For parameter lists of lengths 3, 2, 2, and 2, the generator produces 24 jobs.

Fields not present in the matrix keep the values from the base machine file. A combination's layer feed affects patch Z coordinates and is not written into `laser_params`.

## Numbering

The user configures:

- A filename prefix.
- A starting integer.
- A positive integer increment.
- A zero-padding width.

Numbers increase in matrix placement order, from left to right and then top to bottom. For example, prefix `test_`, start `1`, increment `1`, and padding `4` produces `test_0001`, `test_0002`, and so on.

The generator rejects filename components that are unsafe, ambiguous, duplicated, or not representable within the configured numeric rules.

## Automatic Workpiece Layout

The workpiece's upper-left corner is the physical start position and layout origin. X increases to the right. The preview's Y coordinate increases downward; the generator performs an explicit conversion to the machine controller's coordinate convention rather than writing preview coordinates directly into motion commands.

The user supplies:

- Workpiece width and height.
- Number of jobs per row.

Effective columns are `min(configured_columns, job_count)`. Rows are computed as `ceil(job_count / effective_columns)`. The unit width and height come from the validated footprint of the base machining geometry.

For a full row, horizontal free space is distributed equally across the left margin, every inter-unit gap, and the right margin:

```text
horizontal_gap = (workpiece_width - effective_columns * unit_width) / (effective_columns + 1)
```

Vertical free space is distributed equally across the top margin, every inter-row gap, and the bottom margin:

```text
vertical_gap = (workpiece_height - rows * unit_height) / (rows + 1)
```

The final incomplete row uses the same unit positions and horizontal gap as earlier rows; it remains left-aligned to the established column grid rather than being independently recentered.

Negative free space is an error. Zero gap is valid. All computed bounds must remain finite and inside the workpiece after the same coordinate rounding used for emitted machine movement.

## Output Package

The package has this logical structure:

```text
LaserPMT_YYYYMMDD_HHMMSS/
├── patches/
│   ├── 0_0.npy
│   ├── 1_0.npy
│   ├── 2_0.npy
│   └── ...
├── test_0001machine.json
├── test_0002machine.json
├── ...
├── allmachine.json
├── parameter-map.csv
└── pmt-layout.json
```

Every numbered job receives its own non-overlapping range of controller-compatible numeric patch identifiers. Patch filenames remain `<global_patch_index>_0.npy`, and machine cycles reference `[global_patch_index, 0]`, matching the existing target-controller format. Patch sets are not deduplicated even when two jobs have the same layer feed. `pmt-layout.json` records the exact ordered patch range owned by each job.

`parameter-map.csv` is the human-readable lookup table. It records at least the number, row, column, physical placement, laser-parameter index, layer feed, all varied values, numbered JSON filename, and owned patch identifiers.

`pmt-layout.json` is the machine-readable manifest used for preview and cross-artifact validation. It records format version, workpiece dimensions, unit bounds, row and column counts, calculated gaps, coordinate convention, numbering rules, ordered parameter definitions, and every job's ordered parameters, footprint, filenames, and patch ownership.

## Numbered JSON Motion

Each numbered JSON is built independently and is runnable on its own. It is not extracted from `allmachine.json`.

For every numbered JSON:

- Motion begins from the local origin expected by the existing base machine-file format.
- The job is not moved to its matrix position.
- The base job's patch order, layer order, Hatch/fill-line order, block-center movement, layer descent, and final modal restoration are preserved.
- Its selected laser values occupy the editable first parameter group. The existing immutable reference groups remain in their controller-compatible form.
- Its machine cycles reference only that number's owned patch files.

This means running `test_0002machine.json` alone processes one base-sized job at the operator's current local start position, not at matrix cell 2.

## `allmachine.json` Motion

`allmachine.json` is generated from the validated job plans and global placements. It is not produced by concatenating or slicing numbered JSON documents.

- The machine begins at the workpiece's upper-left start position. Matrix X offsets are positive to the right and matrix Y offsets are negative downward (`machine_y = -preview_y`); local patch coordinates retain their existing convention.
- Jobs execute strictly in increasing number order: left to right, then the next row from its leftmost occupied cell.
- No serpentine reordering is used.
- Within each job, the source patch order, fill-line order, layer order, and block-to-block movement remain unchanged.
- Global targets combine the job's matrix placement with the source job's local patch placement.
- Relative movement is recomputed from the previous globally commanded target, including transitions between jobs and transitions to a new row.
- Every job has a corresponding `laser_params` entry and cycle parameter index. Duplicate laser parameter values are allowed when combinations differ only by layer feed, because job-to-index traceability is more important than deduplication.
- Layer feed is represented by that job's owned patch Z values and layer movement, not by a laser-parameter field.
- The two existing immutable reference laser groups are appended as unused trailing entries after all job parameter groups for controller compatibility. Job indices occupy `0..job_count-1`; the reference groups occupy `job_count` and `job_count+1` and are never selected by a matrix cycle.

The independent numbered sequence and the all-matrix sequence are both validated by simulating the supported vendor command grammar from their respective starting states.

## UI and Interaction

The LaserPMT inspector section contains:

- Base machine-directory selector and status.
- Workpiece width and height.
- Jobs per row.
- Prefix, starting number, increment, and padding width.
- A dynamic explicit-value parameter table with add/remove actions.
- Live job count, row/column count, source unit size, computed horizontal/vertical gap, and estimated patch count.
- Inline validation messages and the generated output path.

The generation action is disabled while the configuration is invalid or the Cartesian product exceeds 1,000 jobs.

The shared preview switch gains a **PMT** entry beside **Texture** and **DXF**. Existing Texture and DXF components, state transitions, rendering, and interactions remain unchanged. After successful LaserPMT generation, the app loads `pmt-layout.json` and switches to PMT.

PMT supports zoom, pan, and fit-to-window. Selecting a job highlights its footprint and displays its number, row, column, physical position, numbered JSON name, layer feed, and full parameter combination. The preview is informational and never mutates generation inputs.

## Validation and Failure Handling

Before generation, the system validates:

- The imported/generated base directory, JSON, patch references, and patch files.
- Parameter uniqueness, explicit values, types, and supported ranges.
- A non-empty Cartesian product no larger than 1,000 jobs.
- Positive workpiece dimensions and column count.
- Safe numbering and collision-free output filenames.
- Finite unit geometry and a non-negative automatic gap in both axes.
- A new output name with no existing final directory, build directory, or lock file.

Generation uses the existing atomic-publication safety model: create an owner lock, build in a private hidden directory, validate every emitted artifact by reloading it, and publish without replacement only after validation succeeds. The published directory is never partially visible.

Cancellation and failure follow the project's existing path-identity and ownership rules. The UI must not recursively remove a path whose identity cannot be proven. Any retained build directory or lock is reported with its exact path for manual inspection after the generator has stopped.

Final validation verifies:

- Exact expected filenames and absence of unexpected artifacts.
- Patch dtype, shape, finite values, geometry, ownership, and Z values.
- Independent local motion for every numbered JSON.
- Global parameter indices, numbering order, placement, and cumulative motion in `allmachine.json`.
- Preservation of source patch/layer/fill/block order.
- Consistency among CSV, layout manifest, JSON references, and patch files.
- Every rounded job footprint remains within the workpiece.

## Testing

Python tests cover:

- Explicit value parsing for integer and boolean fields.
- Ordered multi-parameter Cartesian products.
- Duplicate parameters and invalid values.
- The 1,000-job limit.
- Full and incomplete final rows.
- Equal outer and inner gap calculations.
- Workpiece-too-small rejection.
- Safe numbering and filename collision detection.
- Per-job patch ownership without deduplication.
- Layer-feed-specific Z regeneration.
- Independent numbered JSON motion versus separately computed all-matrix motion.
- Preservation of base fill, layer, patch, and block order.
- Parameter-index selection, including duplicate laser values with different layer feeds.
- Manifest/CSV/package validation, cancellation, output conflicts, and atomic publication.

C# tests cover:

- Parameter-row parsing and readiness state.
- Combination-count and layout summaries.
- Imported machine-directory state.
- The fourth pipeline entry point and dependency rules.
- PMT selection state without regressions to Texture/DXF selection.
- PMT view transforms, hit testing, number labels, selection details, fit, zoom, and pan.

The two currently failing Python source-contract tests in `tests/test_pipeline_independent_steps.py` are an acknowledged pre-existing baseline caused by assertions for the removed progress-window implementation. They are not evidence of a LaserPMT regression, but the implementation plan should update those stale contracts when it touches the four-step pipeline behavior.

## Acceptance Criteria

The feature is complete when:

1. A user can enter arbitrary explicit values for any supported number of parameters, including layer feed, and see the correct ordered combination count.
2. The app automatically places the combinations on the specified workpiece using equal outer and inner gaps.
3. Every numbered JSON runs one local job independently and references only its owned patches.
4. `allmachine.json` starts at the workpiece upper-left position and processes all jobs in number order with correctly recomputed global relative motion.
5. A job's internal machining order is identical to the base file's normal order.
6. The package includes the numbered JSON files, independent patch sets, `allmachine.json`, `parameter-map.csv`, and `pmt-layout.json` and passes a full reload validation.
7. The PMT wireframe accurately shows the workpiece, every job position, and job numbers, while the existing Texture and DXF views remain behaviorally unchanged.
8. Step 4 works after step 3 and from an imported valid machine-file directory.
9. Cancellation, conflicts, invalid input, and insufficient workpiece size fail safely without publishing a partial package.
