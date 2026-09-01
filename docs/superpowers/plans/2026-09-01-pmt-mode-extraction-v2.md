# PMT Mode Extraction v2 Implementation Plan

**Goal:** Extract PMT from pipeline step 4 into an explicit live-draft editing mode with a full-width canvas, multiple true-scale machine-package sources, node and direct parameter editing, keyboard interaction, and an explicit atomic save boundary.

**Design:** `docs/superpowers/specs/2026-09-01-pmt-mode-extraction-v2-design.md`

**Stack:** C# 13 / .NET 10, Avalonia 11.3.x, MSTest, Python 3.9+, NumPy, pytest.

## Global constraints

- Start from `main`; do not cherry-pick the old `codex/pmt-mode-extraction` implementation.
- Preserve existing Texture, DXF, pipeline import, node workflow, timestamp, package-locking, and patch-deduplication behaviour unless this design explicitly changes it.
- Keep `MainWindow` as composition code. New PMT state, validation, saving, and input rules live in focused classes.
- All live edits affect only `PmtDraftSession`; only `PmtSaveService` may write PMT output.
- Use immutable snapshots and pure domain operations where practical.
- Add focused failing tests before each behaviour change, then implement the smallest passing change.
- Do not add `.workbuddy/`, `design-audit/`, generated processing files, pasted images, or `.superpowers/` browser-session files to commits.
- Run `git diff --check` and inspect `git status --short` before every checkpoint commit.

## Task 1: Establish the baseline and remove PMT from pipeline execution

**Files:**

- Modify `GrayscaleLayersMac/MainWindow.cs`.
- Modify `GrayscaleLayersMac/PipelineReadiness.cs`.
- Modify `GrayscaleLayersMac.Tests/PipelineReadinessTests.cs`.
- Modify `GrayscaleLayersMac.Tests/UiStructureContractTests.cs` and other pipeline contract tests that assert four steps.

**Steps:**

- [ ] Run the current focused PMT, pipeline, and UI contract tests and record the baseline.
- [ ] Add/adjust tests proving `Run All` means exactly three steps and the single-step menu has no PMT step.
- [ ] Remove `LaserPmtOnly` from `PipelineRunMode` and remove `needsPmt` from `RunPipelineAsync`.
- [ ] Extract any reusable PMT-generation body out of `RunPipelineAsync`; do not leave an unreachable fourth-step branch.
- [ ] Keep step 3's ability to report its generated machine directory without starting PMT generation.
- [ ] Replace active “four steps”, “step 4”, and rerun-step-4 UI strings with three-step/PMT-mode language.
- [ ] Run the focused pipeline and UI contract tests.

**Checkpoint:** `feat(pmt): extract PMT from pipeline execution`

## Task 2: Define multi-source metadata and source catalogue contracts

**Files:**

- Add `GrayscaleLayersMac/PmtSourceCatalog.cs`.
- Extend or replace `GrayscaleLayersMac/LaserPmtBaseMetadata.cs` with a source-focused immutable model.
- Add `GrayscaleLayersMac.Tests/PmtSourceCatalogTests.cs`.
- Extend `GrayscaleLayersMac.Tests/LaserPmtBaseMetadataTests.cs` as needed.

**Steps:**

- [ ] Define `PmtSourceId`, `PmtSourceMetadata`, native bounds, base parameters, display name, colour token, short mark, directory, and fingerprint.
- [ ] Parse a machine package without writing it; validate non-empty regular `machine.json` and all required patch files.
- [ ] Compute native design bounds from machining coordinates with the same rules as the Python loader.
- [ ] Generate stable display marks and colour assignments that remain deterministic across reloads and are not the only identifier.
- [ ] Implement immutable add, batch-add, remove, relocate, reload, and active-source operations.
- [ ] Return per-directory import errors while accepting valid packages in the same batch.
- [ ] Detect missing or externally changed packages by fingerprint.
- [ ] Test duplicate imports, equal folder names, invalid JSON, missing patches, changed files, relocation, and deterministic marks.

**Checkpoint:** `feat(pmt): add multi-source machine catalogue`

## Task 3: Upgrade the workflow domain for per-target sources, size locks, and overrides

**Files:**

- Modify `GrayscaleLayersMac/LaserPmtWorkflow.cs`.
- Modify `GrayscaleLayersMac/LaserPmtWorkflowEditor.cs`.
- Modify `GrayscaleLayersMac/LaserPmtWorkflowCompiler.cs`.
- Modify related `GrayscaleLayersMac.Tests/LaserPmtWorkflow*Tests.cs`.

**Steps:**

- [ ] Replace workflow-wide `BaseMachineIdentity` assumptions with a source table and one base node per source.
- [ ] Add source ID, native width/height, current bounds, and size-lock state to each PMT target.
- [ ] Add immutable direct per-target parameter overrides.
- [ ] Preserve parameter nodes, ports, timestamps, connections, viewport, numbering history, and stable IDs.
- [ ] Enforce one batch-node input per target/parameter; applying another replaces the old batch connection.
- [ ] Compile final values as source base → batch node → direct override.
- [ ] Restore inheritance by deleting the direct override rather than copying a lower-level value.
- [ ] Implement source reassignment that preserves the centre and applies the new native size to locked targets.
- [ ] Implement unlock, independent X/Y size edits, and both relock behaviours.
- [ ] Update geometry validation for heterogeneous target sizes, rounding precision, overlap, and workpiece bounds.
- [ ] Keep deletion non-compacting and non-renumbering; keep explicit spatial renumber.
- [ ] Add tests for mixed sources, precedence, reassignment, lock transitions, duplicate inputs, and invalid geometry.

**Checkpoint:** `feat(pmt): model heterogeneous sourced targets`

## Task 4: Add the authoritative draft session and command layer

**Files:**

- Add `GrayscaleLayersMac/PmtDraftSession.cs`.
- Add `GrayscaleLayersMac/PmtDraftCommands.cs` if command types would otherwise crowd the session.
- Add `GrayscaleLayersMac.Tests/PmtDraftSessionTests.cs`.

**Steps:**

- [ ] Define immutable draft snapshots containing catalogue, workflow, output name, selection, validation, saved revision, and current revision.
- [ ] Implement a transient matrix-hover preview that does not increment revision or dirty the draft.
- [ ] Implement matrix commit using the active source, complete rows × columns, and default 1..N row-major numbering.
- [ ] Route workpiece, geometry, source assignment, delete, renumber, auto-arrange, node, connection, override, and output-name edits through session commands.
- [ ] Publish one change notification per committed command.
- [ ] Implement single and Command-click multi-selection.
- [ ] Implement spatial nearest-neighbour selection for arrow keys.
- [ ] Implement 0.1 mm Command-arrow and 1 mm Shift-Command-arrow nudges.
- [ ] Ensure save completion marks only the saved revision; edits made during a save remain dirty.
- [ ] Test no-op commands, invalid commands, preview cancellation, dirty-state transitions, selection after deletion, and edits during save.

**Checkpoint:** `feat(pmt): add live draft session`

## Task 5: Define and persist the multi-source workflow/request format

**Files:**

- Modify `GrayscaleLayersMac/LaserPmtWorkflowSerializer.cs`.
- Modify `GrayscaleLayersMac/LaserPmtLayout.cs`.
- Modify `GrayscaleLayersMac/LaserPmtLayoutWriter.cs` or retire its direct-edit responsibility.
- Modify `GrayscaleLayersMac.Tests/LaserPmtWorkflowSerializerTests.cs`.
- Modify `GrayscaleLayersMac.Tests/LaserPmtLayoutTests.cs` and `LaserPmtLayoutWriterTests.cs`.

**Steps:**

- [ ] Introduce a new strict format version with a source table and per-target source ID, native size, current bounds, scale, lock state, batch inputs, and direct overrides.
- [ ] Preserve deterministic field ordering, bounded reads, duplicate-key rejection, stable IDs, and non-finite-number rejection.
- [ ] Serialize one base node per source and validate source/target/node cross-references.
- [ ] Round-trip heterogeneous workflows without losing marks, custom locks, numbering holes, selection-independent state, or parameter provenance.
- [ ] Keep version 2 single-source layouts readable through an explicit migration into a one-source draft.
- [ ] Prevent the legacy layout writer from mutating generated files during live editing.
- [ ] Test malformed source references, missing base nodes, conflicting parameter inputs, old-layout migration, and deterministic serialization.

**Checkpoint:** `feat(pmt): persist multi-source draft format`

## Task 6: Upgrade Python generation to multiple sources and true scaling

**Files:**

- Modify `laser_pmt.py`.
- Modify `tests/test_laser_pmt.py`.

**Steps:**

- [ ] Add failing request fixtures with at least two machine packages of different native dimensions.
- [ ] Parse and strictly validate the new source table, fingerprints, target source IDs, native sizes, bounds, and X/Y scales.
- [ ] Load every unique source package once and verify its measured bounds against request metadata.
- [ ] Transform every source machining coordinate and patch coordinate using the target's independent X/Y scale and translation.
- [ ] Apply resolved final parameters per target while preserving source-local layer and cycle order.
- [ ] Generate each independent PMT machine JSON from its own source rather than one global template.
- [ ] Build combined `allmachine.json` across heterogeneous targets in PMT-number order and recompute all relative global moves.
- [ ] Reuse generated patch groups only when dtype, shape, and array contents are equal after transformation.
- [ ] Extend layout and CSV metadata with source ID/name, native size, scale, lock state, and parameter provenance.
- [ ] Verify the exact output file set and cross-check JSON, layout, CSV, and NPY references.
- [ ] Keep single-source version 2 request compatibility tests passing where required for old-layout reads.
- [ ] Run focused pytest and `python3 -m py_compile laser_pmt.py`.

**Checkpoint:** `feat(pmt): generate heterogeneous source packages`

## Task 7: Implement the exclusive atomic save service

**Files:**

- Add `GrayscaleLayersMac/PmtSaveService.cs`.
- Add `GrayscaleLayersMac.Tests/PmtSaveServiceTests.cs`.
- Modify `GrayscaleLayersMac/MainWindow.cs` only to call the service.

**Steps:**

- [ ] Define injectable boundaries for Python discovery/process execution, filesystem checks, progress, logging, and clock/owner tokens.
- [ ] Snapshot one draft revision and validate sources, fingerprints, geometry, numbering, parameters, output name, and collisions before launching Python.
- [ ] Write the request to an owned temporary file with UTF-8 without BOM.
- [ ] Generate into a uniquely owned building directory and preserve existing no-overwrite lock semantics.
- [ ] Verify generated layout target count, source mapping, bounds, files, and combined output before success.
- [ ] Atomically promote completed output and mark exactly the saved revision.
- [ ] Keep the draft intact on validation, cancellation, process, verification, or promotion failure.
- [ ] Ensure no other C# component writes PMT layout or per-target parameter files during editing.
- [ ] Test output collisions, cancellation, stale sources, save-while-editing, invalid generated packages, and successful retry after failure.

**Checkpoint:** `feat(pmt): add explicit atomic PMT save`

## Task 8: Extend icon vocabulary and PMT toolbar controls

**Files:**

- Modify `GrayscaleLayersMac/UiIcons.cs`.
- Add or modify `GrayscaleLayersMac.Tests/UiIconsTests.cs`.
- Add focused PMT toolbar component files rather than placing construction in `MainWindow.cs`.

**Steps:**

- [ ] Add static 24×24 outline paths for matrix, sources/import, assign source, renumber, auto-arrange, lock/unlock, save, and chevron actions.
- [ ] Record compatible upstream icon provenance/licence when adapting paths.
- [ ] Use consistent optical size and 1.5–2 px visual weight in both themes.
- [ ] Build an icon-first PMT toolbar with the compact grid-icon + `PMT` exception.
- [ ] Provide tooltip, automation name, focus visual, disabled state, and hit target for every action.
- [ ] Implement save as a split button with `设置 PMT 加工文件名…` in its flyout.
- [ ] Display unsaved state and validation-error count without adding verbose permanent labels.
- [ ] Add structure/accessibility tests for the controls.

**Checkpoint:** `ui(pmt): add icon-first PMT toolbar`

## Task 9: Build the WPS-style matrix, source, workpiece, and target popovers

**Files:**

- Add `GrayscaleLayersMac/PmtMatrixPopover.cs`.
- Add `GrayscaleLayersMac/PmtSourceMenu.cs`.
- Add `GrayscaleLayersMac/PmtWorkpiecePopover.cs`.
- Add `GrayscaleLayersMac/PmtTargetPopover.cs`.
- Add focused tests for non-visual state and interaction contracts.

**Steps:**

- [ ] Render a keyboard-accessible expandable matrix grid with hover highlight and `R 行 × C 列 · N 个 PMT` status.
- [ ] Dispatch hover preview separately from click commit; provide no Apply/OK button.
- [ ] Build source import, active-source selection, legend, relocation, and multi-target assignment UI.
- [ ] Show colour, short mark, and full source name together.
- [ ] Build live workpiece width/height editing anchored to the workpiece border.
- [ ] Build selected-target geometry and final-parameters sections anchored near the PMT.
- [ ] Add lock/unlock and both relock choices; disable size inputs when locked.
- [ ] Show base, batch, and direct parameter provenance; implement restore-inherited action per direct override.
- [ ] Update on every valid value change and preserve edits when the popover closes.
- [ ] Keep Delete out of the target popover.

**Checkpoint:** `ui(pmt): add live editing popovers`

## Task 10: Upgrade the canvas for source styling, multi-selection, and keyboard editing

**Files:**

- Modify `GrayscaleLayersMac/LaserPmtWorkflowCanvas.cs`.
- Modify `GrayscaleLayersMac/LaserPmtWorkflowViewMath.cs` if additional pure geometry helpers are needed.
- Modify `GrayscaleLayersMac/LaserPmtWorkflowInspector.cs` for multi-source base nodes and batch application.
- Extend workflow view/interaction tests.

**Steps:**

- [ ] Render each source's base node and targets with the same stable colour/mark/name language.
- [ ] Render heterogeneous targets at one exact millimetre scale.
- [ ] Keep parameter nodes, ports, connections, timestamps, and existing drag/edit interactions.
- [ ] Add Command-click multi-selection and clear selected/hover/focus styling.
- [ ] Connect pointer selection to the target and workpiece popovers.
- [ ] Implement arrow selection, two nudge increments, and Delete/Backspace through draft commands.
- [ ] Suppress canvas shortcuts whenever an editor/menu/popover owns focus.
- [ ] Keep source reassignment centre-preserving and deletion non-compacting.
- [ ] Add invalid overlap, out-of-bounds, missing-source, and changed-source badges.
- [ ] Support applying a parameter unit node to multiple selected targets and replacing same-parameter batch connections deterministically.
- [ ] Test hit targets, spatial navigation, mixed-size rendering math, multi-selection, and keyboard focus suppression.

**Checkpoint:** `ui(pmt): add true-scale multi-source canvas editing`

## Task 11: Integrate instant full-width PMT mode and guarded transitions

**Files:**

- Add `GrayscaleLayersMac/PmtModeController.cs`.
- Modify `GrayscaleLayersMac/MainWindow.cs`.
- Modify `GrayscaleLayersMac/LaserPmtPanel.cs` or replace it with focused composition.
- Modify relevant UI structure and window-layout tests.

**Steps:**

- [ ] Compose draft session, source catalogue, canvas, popovers, toolbar, progress, and save service through `PmtModeController`.
- [ ] Enter PMT mode by immediately hiding the right pipeline inspector and expanding the canvas; restore it immediately on exit.
- [ ] Add a structural regression test forbidding PMT mode translation transitions and delayed hide/show logic.
- [ ] Preserve the draft when switching preview kind or leaving PMT mode.
- [ ] Prompt Save/Discard/Cancel only before window close or an operation that would replace edited draft content.
- [ ] Add step 3 output to the source catalogue without generating a matrix or PMT output.
- [ ] Import multiple machine-package directories without auto-running processing.
- [ ] Remove direct `LaserPmtLayoutWriter` mutation from target-detail save events.
- [ ] Ensure clearing preview caches does not delete disk files or silently discard an unsaved draft.
- [ ] Keep `MainWindow` changes limited to construction, event forwarding, global prompts, and progress display.

**Checkpoint:** `feat(pmt): integrate standalone PMT mode`

## Task 12: Documentation, full regression, packaging, and visual QA

**Files:**

- Modify `GrayscaleLayersMac/README.md`.
- Modify packaging scripts/resources if request or helper files change.
- Modify focused tests discovered during regression only when behaviour is intentionally changed.

**Steps:**

- [ ] Document the three-step pipeline, PMT draft/save boundary, multi-source import, source assignment, locks, node/direct precedence, shortcuts, and output format.
- [ ] Run `python3 -m pytest -q` with zero failures.
- [ ] Run `python3 -m py_compile laser_pmt.py`.
- [ ] Run `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore` with zero failures.
- [ ] Run `dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore` successfully.
- [ ] Run the macOS packaging/resource validation scripts.
- [ ] Launch the app and exercise at least two source packages with visibly different native dimensions.
- [ ] Verify matrix hover/commit, deletion holes, renumber, auto-arrange, source reassignment, nodes, direct overrides, locks, non-uniform scaling, keyboard navigation, nudging, invalid-state repair, save failure, and successful retry.
- [ ] Inspect generated independent JSON, `allmachine.json`, CSV, layout, and NPY files against the canvas source, dimensions, scale, order, and parameters.
- [ ] Visually verify dark/light themes, icon optical consistency, focus states, tooltips, source differentiation without colour, full-width mode switching, and absence of the right-panel slide animation.
- [ ] Run `git diff --check`; inspect `git status --short`; commit only feature-related files.

**Checkpoint:** `docs(pmt): document standalone multi-source mode`

## Recommended checkpoint order

1. `feat(pmt): extract PMT from pipeline execution`
2. `feat(pmt): add multi-source machine catalogue`
3. `feat(pmt): model heterogeneous sourced targets`
4. `feat(pmt): add live draft session`
5. `feat(pmt): persist multi-source draft format`
6. `feat(pmt): generate heterogeneous source packages`
7. `feat(pmt): add explicit atomic PMT save`
8. `ui(pmt): add icon-first PMT toolbar`
9. `ui(pmt): add live editing popovers`
10. `ui(pmt): add true-scale multi-source canvas editing`
11. `feat(pmt): integrate standalone PMT mode`
12. `docs(pmt): document standalone multi-source mode`
