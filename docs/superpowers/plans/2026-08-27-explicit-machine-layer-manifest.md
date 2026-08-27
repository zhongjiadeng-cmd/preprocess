# Explicit Machine Layer Manifest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the three-step pipeline export machine files only from DXFs created by the current run while ignoring historical DXFs in a reused directory.

**Architecture:** Add one Python boundary that either discovers the directory for backward compatibility or validates an explicit layer list. The Avalonia pipeline passes its existing `currentRunDxfFiles` via repeatable `--layer-dxf` arguments, so current-run state is preserved across the process boundary.

**Tech Stack:** Python 3, unittest, NumPy, C#/.NET 10, Avalonia, MSTest.

## Global Constraints

- Preserve directory discovery when no explicit manifest is supplied.
- Never modify or delete historical TIFF, DXF, PNG, or JSON artifacts.
- Validate an explicit manifest before creating the output, `.building`, or `.lock` path.
- Explicit paths must be absolute regular files and resolved direct children of the DXF directory.
- Selected numeric layer numbers must be unique, contiguous, and processed numerically.
- Do not change any artifact format.

---

### Task 1: Add the Python explicit-manifest boundary

**Files:**
- Modify: `dxf_to_machine_file.py:354-381,772-824,937-1004`
- Test: `tests/test_dxf_to_machine_file.py:126-166,890-914,1749-1818`

**Interfaces:**
- Produces: `select_layer_dxf_files(dxf_dir: Path, layer_files: list[Path] | None = None, *, require_contiguous: bool = True) -> list[Path]`
- Extends: `generate_machine_file(..., *, layer_files: list[Path] | None = None) -> Path`
- Extends CLI: repeatable `--layer-dxf PATH`

- [ ] **Step 1: Write failing selection tests**

Import `select_layer_dxf_files` and add these tests to `LayerDiscoveryTests`:

```python
def test_explicit_manifest_ignores_historical_duplicate_in_directory(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        dxf_dir = Path(directory).resolve()
        historical = dxf_dir / "layer_1_old.dxf"
        current = dxf_dir / "layer_01_current.dxf"
        historical.write_text("old", encoding="ascii")
        current.write_text("current", encoding="ascii")

        files = select_layer_dxf_files(dxf_dir, [current])

    self.assertEqual(files, [current])

def test_explicit_manifest_sorts_numeric_layers(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        dxf_dir = Path(directory).resolve()
        first = dxf_dir / "layer_01_first.dxf"
        second = dxf_dir / "layer_02_second.dxf"
        first.write_text("first", encoding="ascii")
        second.write_text("second", encoding="ascii")

        files = select_layer_dxf_files(dxf_dir, [second, first])

    self.assertEqual(files, [first, second])

def test_explicit_manifest_rejects_invalid_paths_and_numbers(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory).resolve()
        dxf_dir = root / "dxfs"
        dxf_dir.mkdir()
        outside = root / "layer_01_outside.dxf"
        outside.write_text("outside", encoding="ascii")
        nested_dir = dxf_dir / "nested"
        nested_dir.mkdir()
        nested = nested_dir / "layer_01_nested.dxf"
        nested.write_text("nested", encoding="ascii")
        first = dxf_dir / "layer_01_first.dxf"
        duplicate = dxf_dir / "layer_1_duplicate.dxf"
        third = dxf_dir / "layer_03_third.dxf"
        invalid = dxf_dir / "not-a-layer.dxf"
        for path in (first, duplicate, third, invalid):
            path.write_text("x", encoding="ascii")

        invalid_manifests = (
            [],
            [Path("layer_01_first.dxf")],
            [outside],
            [nested],
            [dxf_dir / "layer_02_missing.dxf"],
            [invalid],
            [first, duplicate],
            [first, third],
        )
        for manifest in invalid_manifests:
            with self.subTest(manifest=manifest), self.assertRaises(ValueError):
                select_layer_dxf_files(dxf_dir, manifest)
```

These tests catch accidental ambient discovery, unsafe paths, and lost numeric validation.

- [ ] **Step 2: Run the new tests and verify RED**

```bash
python3 -m unittest \
  tests.test_dxf_to_machine_file.LayerDiscoveryTests.test_explicit_manifest_ignores_historical_duplicate_in_directory \
  tests.test_dxf_to_machine_file.LayerDiscoveryTests.test_explicit_manifest_sorts_numeric_layers \
  tests.test_dxf_to_machine_file.LayerDiscoveryTests.test_explicit_manifest_rejects_invalid_paths_and_numbers -v
```

Expected: import failure because `select_layer_dxf_files` does not exist.

- [ ] **Step 3: Implement the selection helper**

Add beside `discover_layer_dxf_files`:

```python
def select_layer_dxf_files(
    dxf_dir: Path,
    layer_files: list[Path] | None = None,
    *,
    require_contiguous: bool = True,
) -> list[Path]:
    if layer_files is None:
        return discover_layer_dxf_files(
            dxf_dir,
            require_contiguous=require_contiguous,
        )
    if not layer_files:
        raise ValueError("Explicit layer DXF manifest must not be empty")

    try:
        resolved_dir = dxf_dir.resolve(strict=True)
    except (FileNotFoundError, NotADirectoryError) as exc:
        raise ValueError(
            f"DXF directory does not exist or is not a directory: {dxf_dir}"
        ) from exc
    if not resolved_dir.is_dir():
        raise ValueError(f"DXF directory does not exist or is not a directory: {dxf_dir}")

    numbered_files: list[tuple[int, Path]] = []
    for supplied_path in layer_files:
        if not supplied_path.is_absolute():
            raise ValueError(f"Explicit layer DXF path must be absolute: {supplied_path}")
        try:
            supplied_stat = os.lstat(supplied_path)
            resolved_path = supplied_path.resolve(strict=True)
        except (FileNotFoundError, NotADirectoryError) as exc:
            raise ValueError(
                f"Explicit layer DXF file does not exist: {supplied_path}"
            ) from exc
        if not stat.S_ISREG(supplied_stat.st_mode):
            raise ValueError(
                f"Explicit layer DXF path must be a regular file: {supplied_path}"
            )
        if resolved_path.parent != resolved_dir:
            raise ValueError(
                "Explicit layer DXF must be a direct child of the DXF directory: "
                f"{supplied_path}"
            )
        match = _LAYER_FILENAME_RE.fullmatch(resolved_path.name)
        if match is None:
            raise ValueError(f"Invalid layer DXF filename: {resolved_path.name}")
        numbered_files.append((int(match.group(1)), resolved_path))

    numbered_files.sort(key=lambda item: item[0])
    layer_numbers = [number for number, _ in numbered_files]
    if len(set(layer_numbers)) != len(layer_numbers):
        raise ValueError("Duplicate numeric layer numbers found")
    if require_contiguous and any(
        current != previous + 1
        for previous, current in zip(layer_numbers, layer_numbers[1:])
    ):
        raise ValueError("Layer numbers must be contiguous")
    return [path for _, path in numbered_files]
```

- [ ] **Step 4: Run Step 2 again and verify GREEN**

Expected: all three tests pass.

- [ ] **Step 5: Write failing generator and CLI tests**

Add a generation test:

```python
def test_explicit_manifest_uses_current_file_despite_historical_duplicate(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory).resolve()
        dxf_dir = root / "dxfs"
        dxf_dir.mkdir()
        historical = dxf_dir / "layer_1_old.dxf"
        current = dxf_dir / "layer_01_current.dxf"
        write_dxf(historical, [(90, 90, 0, 91, 91, 0)])
        write_dxf(current, [(1, 2, 0, 3, 4, 0)])

        result = generate_machine_file(
            dxf_dir,
            "job",
            3,
            dict(DEFAULT_LASER_PARAMS[0]),
            layer_files=[current],
        )
        patch = np.load(result / "patches" / "0_0.npy", allow_pickle=False)

    np.testing.assert_array_equal(
        patch[:, [0, 1, 3, 4]],
        np.array([[1, 2, 3, 4]], dtype="<f4"),
    )
```

Add to `CliTests`:

```python
def test_cli_repeatable_layer_dxf_uses_only_explicit_manifest(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory).resolve()
        dxf_dir = root / "dxfs"
        dxf_dir.mkdir()
        historical = dxf_dir / "layer_1_old.dxf"
        first = dxf_dir / "layer_01_current.dxf"
        second = dxf_dir / "layer_02_current.dxf"
        write_dxf(historical, [(90, 90, 0, 91, 91, 0)])
        write_dxf(first, [(1, 2, 0, 3, 4, 0)])
        write_dxf(second, [(5, 6, 0, 7, 8, 0)])

        completed = subprocess.run(
            [
                sys.executable,
                str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                str(dxf_dir), "cli-manifest",
                "--layer-dxf", str(second),
                "--layer-dxf", str(first),
            ],
            text=True,
            capture_output=True,
            check=False,
        )

        self.assertEqual(completed.returncode, 0, completed.stderr)
        self.assertIn("层数: 2", completed.stdout)
        self.assertEqual(
            sorted(path.name for path in (root / "cli-manifest" / "patches").iterdir()),
            ["0_0.npy", "1_0.npy"],
        )
```

Add a separate invalid-manifest generation test that asserts `job`, `.job.building`, and `.job.lock` remain absent.

- [ ] **Step 6: Run focused tests and verify RED**

```bash
python3 -m unittest \
  tests.test_dxf_to_machine_file.GenerateMachineFileTests.test_explicit_manifest_uses_current_file_despite_historical_duplicate \
  tests.test_dxf_to_machine_file.CliTests.test_cli_repeatable_layer_dxf_uses_only_explicit_manifest -v
```

Expected: unknown `layer_files` and `--layer-dxf`.

- [ ] **Step 7: Thread selected files through generation and CLI**

Preserve the existing positional-call compatibility of `owner_token` and
`block_center_positioning`, then add only `layer_files` as keyword-only:

```python
def generate_machine_file(
    dxf_dir: Path,
    output_name: str | None,
    layer_step_um: float,
    first_laser_params: dict[str, object],
    owner_token: str | None = None,
    block_center_positioning: bool = False,
    *,
    layer_files: list[Path] | None = None,
) -> Path:
```

Immediately after normalizing `dxf_dir`, before any output/lock mutation:

```python
selected_layer_files = select_layer_dxf_files(dxf_dir, layer_files)
```

Replace the later directory discovery with `build_patch_plan(selected_layer_files, block_center_positioning)`. Update any positional repository callers.

Add:

```python
parser.add_argument("--layer-dxf", action="append", type=Path)
```

In `main`, select once and reuse:

```python
layer_files = select_layer_dxf_files(args.dxf_dir.absolute(), args.layer_dxf)
layer_count = len(layer_files)
```

Pass `layer_files=layer_files` to `generate_machine_file`; do not rescan.

- [ ] **Step 8: Run focused and full Python tests**

```bash
python3 -m unittest tests.test_dxf_to_machine_file.LayerDiscoveryTests tests.test_dxf_to_machine_file.CliTests -v
python3 -m unittest tests.test_dxf_to_machine_file -v
```

Expected: all pass.

- [ ] **Step 9: Commit Task 1**

```bash
git add dxf_to_machine_file.py tests/test_dxf_to_machine_file.py
git commit -m "fix: accept explicit machine layer manifest"
```

---

### Task 2: Pass the current-run manifest from Avalonia

**Files:**
- Modify: `GrayscaleLayersMac/MainWindow.cs:1683-1740`
- Test: `tests/test_texture_to_hatch_dxf.py:2420-2460`

**Interfaces:**
- Consumes: repeatable CLI `--layer-dxf PATH`
- Consumes: existing validated absolute `currentRunDxfFiles`
- Produces: one CLI argument pair per current-run DXF

- [ ] **Step 1: Write failing source-contract tests**

Add to `AvaloniaArtifactValidationSourceContractTests`:

```python
def test_pipeline_passes_each_current_run_dxf_as_explicit_machine_input(self) -> None:
    source = (
        Path(__file__).resolve().parents[1] / "GrayscaleLayersMac" / "MainWindow.cs"
    ).read_text(encoding="utf-8")
    start = source.index("var machineInfo = CreatePythonProcess(python)")
    end = source.index("var machineExitCode = await RunProcessAsync", start)
    setup = source[start:end]
    self.assertIn("foreach (var layerDxfPath in currentRunDxfFiles)", setup)
    self.assertIn('machineInfo.ArgumentList.Add("--layer-dxf")', setup)
    self.assertIn("machineInfo.ArgumentList.Add(layerDxfPath)", setup)

def test_pipeline_ignores_historical_dxfs_but_revalidates_current_manifest(self) -> None:
    source = (
        Path(__file__).resolve().parents[1] / "GrayscaleLayersMac" / "MainWindow.cs"
    ).read_text(encoding="utf-8")
    start = source.index("var pathComparer = StringComparer.OrdinalIgnoreCase")
    end = source.index("步骤 3/3：开始生成机器加工文件", start)
    manifest = source[start:end]
    self.assertIn("expectedDxfFiles", manifest)
    self.assertIn("!IsRegularNonEmptyFile(path)", manifest)
    self.assertNotIn("unexpectedDxfFiles", manifest)
    self.assertNotIn("actualDxfFiles", manifest)
```

- [ ] **Step 2: Run the tests and verify RED**

```bash
python3 -m unittest \
  tests.test_texture_to_hatch_dxf.AvaloniaArtifactValidationSourceContractTests.test_pipeline_passes_each_current_run_dxf_as_explicit_machine_input \
  tests.test_texture_to_hatch_dxf.AvaloniaArtifactValidationSourceContractTests.test_pipeline_ignores_historical_dxfs_but_revalidates_current_manifest -v
```

Expected: the argument loop is absent and whole-directory comparison remains.

- [ ] **Step 3: Change the pipeline to current-manifest validation**

Replace `actualDxfFiles`/`unexpectedDxfFiles` logic with:

```csharp
var pathComparer = StringComparer.OrdinalIgnoreCase;
var expectedDxfFiles = new HashSet<string>(currentRunDxfFiles, pathComparer);
var missingDxfFiles = expectedDxfFiles
    .Where(path => !IsRegularNonEmptyFile(path))
    .OrderBy(path => path, pathComparer)
    .ToArray();
if (missingDxfFiles.Length > 0)
{
    var manifestError = new StringBuilder();
    manifestError.AppendLine(
        $"本次 DXF 清单中有 {missingDxfFiles.Length} 个文件缺失或无效：");
    foreach (var path in missingDxfFiles)
        manifestError.AppendLine($"- {path}");
    manifestError.Append("请重新运行流程生成完整的本次 DXF 清单。");
    throw new InvalidOperationException(manifestError.ToString());
}
AppendPipelineLog($"已验证本次 DXF 清单：{expectedDxfFiles.Count} 个文件。");
```

After existing machine options are added, append:

```csharp
foreach (var layerDxfPath in currentRunDxfFiles)
{
    machineInfo.ArgumentList.Add("--layer-dxf");
    machineInfo.ArgumentList.Add(layerDxfPath);
}
```

- [ ] **Step 4: Run Step 2 again and verify GREEN**

Expected: both pass.

- [ ] **Step 5: Run application regression suites**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
```

Expected: all pass.

- [ ] **Step 6: Commit Task 2**

```bash
git add GrayscaleLayersMac/MainWindow.cs tests/test_texture_to_hatch_dxf.py
git commit -m "fix: isolate machine export to current DXFs"
```

---

### Task 3: Final regression and reported-case verification

**Files:**
- Verify only

**Interfaces:**
- Verifies Python selection and Avalonia wiring together.

- [ ] **Step 1: Run all repository tests**

```bash
python3 -m unittest discover -s tests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
```

Expected: all pass without errors.

- [ ] **Step 2: Re-run the exact collision regression**

```bash
python3 -m unittest \
  tests.test_dxf_to_machine_file.CliTests.test_cli_repeatable_layer_dxf_uses_only_explicit_manifest -v
```

Expected: PASS with a directory containing historical `layer_1_...` and current `layer_01_...`.

- [ ] **Step 3: Inspect final scope**

```bash
git diff HEAD~2 --check
git diff HEAD~2 --stat
git status --short
```

Expected: only intended source/tests changed after plan execution; unrelated untracked user files remain untouched.
