# Layer Texture/DXF Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the fitted grayscale texture for the selected pipeline layer behind its DXF hatch lines, with independent visibility and opacity controls that remain registered during top-view navigation.

**Architecture:** Extend the Python conversion boundary so the fitted boolean mask that drives Hatch generation is also published as a paired PNG in the same staged artifact bundle as the DXF and optional block metadata. Represent layer pairing and overlay UI state in small testable C# models, then let `DxfPreviewControl` own and render the validated bitmap with the same model-to-screen transform used by DXF lines. `MainWindow` wires generated layer triples into the selector and keeps unpaired imported DXFs supported.

**Tech Stack:** Python 3, NumPy, Pillow, `unittest`, C#/.NET 10, Avalonia 11, MSTest.

## Global Constraints

- Apply paired layer behavior only to the main “灰度分层 → Hatch DXF → 加工文件” pipeline.
- The overlay must use the exact fitted mask consumed by Hatch generation, including unit detection/fallback, crop anchor, tiling, threshold, DPI, and target-size rounding.
- Do not alter grayscale threshold semantics, Hatch geometry, Voronoi blocks, DXF syntax, block metadata, or machine-file contents.
- Publish DXF, optional block metadata, and requested preview PNG as one validated staged bundle; do not register a layer when any requested artifact is invalid.
- Keep imported unpaired DXFs usable and explicitly mark their texture overlay unavailable.
- Draw the 2D overlay only in top view; preserve its selected visibility state while isometric view temporarily suppresses it.
- Preserve all unrelated tracked and untracked workspace content.

---

## File Structure

- `texture_to_hatch_dxf.py`: encode the fitted mask, validate the staged PNG, and publish it with the existing DXF/metadata artifact bundle.
- `tests/test_texture_to_hatch_dxf.py`: mask identity, CLI, no-replace publication, rollback, and Avalonia source-contract tests.
- `GrayscaleLayersMac/DxfLayerPreviewItem.cs`: immutable paired/unpaired selector item and artifact validation.
- `GrayscaleLayersMac/DxfOverlayState.cs`: control-independent visibility, availability, opacity, and view-state rules.
- `GrayscaleLayersMac/DxfPreviewControl.cs`: bitmap ownership, top-view texture rendering, shared transforms, and line visibility.
- `GrayscaleLayersMac/MainWindow.cs`: pipeline preview-path arguments, layer pairing, selector loading, and overlay controls.
- `GrayscaleLayersMac.Tests/DxfLayerPreviewItemTests.cs`: pairing and file validation tests.
- `GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs`: independent-toggle and top/isometric state tests.
- `GrayscaleLayersMac/README.md`: user-facing overlay behavior and preview artifact naming.

---

### Task 1: Publish the exact fitted Hatch mask as a paired PNG

**Files:**
- Modify: `texture_to_hatch_dxf.py:1597-1959,1964-2318`
- Test: `tests/test_texture_to_hatch_dxf.py`

**Interfaces:**
- Consumes: the `fitted: np.ndarray` boolean mask already passed to `export_horizontal_hatch_dxf`.
- Produces: optional keyword parameter `preview_output_path: Path | None = None` on `convert_texture_to_dxf`; CLI option `--preview-output PATH`; optional keyword parameter `preview_output: Path | None = None` on `export_horizontal_hatch_dxf`.
- Invariant: preview pixel value is `0` where `fitted` is `True` and `255` where it is `False`.

- [ ] **Step 1: Write failing mask-identity and CLI tests**

```python
def test_preview_png_is_the_exact_fitted_mask(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        source = np.array([[0, 255], [255, 0]], dtype=np.uint8)
        input_path = root / "layer.tiff"
        preview_path = root / "layer.preview.png"
        Image.fromarray(source).save(input_path, dpi=(25.4, 25.4))

        convert_texture_to_dxf(
            input_path,
            root / "layer.dxf",
            3,
            2,
            1,
            tile_mode="repeat",
            crop_anchor="top-left",
            preview_output_path=preview_path,
        )

        with Image.open(preview_path) as preview:
            self.assertEqual(preview.mode, "L")
            np.testing.assert_array_equal(
                np.asarray(preview),
                np.array([[0, 255, 0], [255, 0, 255]], dtype=np.uint8),
            )

def test_cli_writes_requested_preview_output(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        input_path = root / "layer.tiff"
        dxf_path = root / "layer.dxf"
        preview_path = root / "layer.preview.png"
        Image.fromarray(np.zeros((2, 2), dtype=np.uint8)).save(
            input_path, dpi=(25.4, 25.4)
        )
        completed = subprocess.run(
            [sys.executable, str(ROOT / "texture_to_hatch_dxf.py"), str(input_path),
             str(dxf_path), "--size", "2", "--spacing", "1", "--blocks", "0",
             "--tile-mode", "repeat", "--preview-output", str(preview_path)],
            check=False, capture_output=True, text=True,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        self.assertTrue(preview_path.is_file())
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.FittedPreviewOutputTests -v
```

Expected: FAIL because `preview_output_path` and `--preview-output` do not exist.

- [ ] **Step 3: Add exact-mask PNG encoding and CLI plumbing**

Add the following focused encoder and signatures:

```python
def _write_fitted_preview_png(
    black_mask: np.ndarray,
    destination: Path,
    *,
    owned_file: _OwnedStagedFile | None = None,
) -> None:
    pixels = np.where(black_mask, 0, 255).astype(np.uint8)
    if owned_file is None:
        Image.fromarray(pixels, mode="L").save(destination, format="PNG")
        return
    with os.fdopen(os.dup(owned_file.descriptor), "wb") as stream:
        Image.fromarray(pixels, mode="L").save(stream, format="PNG")

# Append after random_seed in export_horizontal_hatch_dxf's keyword parameters:
preview_output: Path | None = None,

# Append after voronoi_attempts in convert_texture_to_dxf's keyword parameters:
preview_output_path: Path | None = None,
```

Add `--preview-output` as a `Path` option and pass it from `main()` to `convert_texture_to_dxf`. Reject a preview path equal to the DXF or block metadata path before creating artifacts.

- [ ] **Step 4: Write failing staged-bundle and rollback tests**

```python
def test_preview_is_published_with_dxf_and_metadata(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        input_path = root / "layer.tiff"
        dxf_path = root / "layer.dxf"
        preview_path = root / "layer.preview.png"
        Image.fromarray(np.zeros((2, 2), dtype=np.uint8)).save(
            input_path, dpi=(25.4, 25.4)
        )
        convert_texture_to_dxf(
            input_path, dxf_path, 2, 2, 1,
            tile_mode="repeat", voronoi_block_count=2,
            min_block_area_mm2=0, max_block_area_mm2=4,
            preview_output_path=preview_path,
        )
        self.assertTrue(dxf_path.is_file())
        self.assertTrue(block_metadata_path(dxf_path).is_file())
        self.assertTrue(preview_path.is_file())

def test_preview_encoding_failure_publishes_neither_dxf_nor_preview(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        input_path = root / "layer.tiff"
        dxf_path = root / "layer.dxf"
        preview_path = root / "layer.preview.png"
        Image.fromarray(np.zeros((2, 2), dtype=np.uint8)).save(
            input_path, dpi=(25.4, 25.4)
        )
        with mock.patch.object(
            hatch,
            "_write_fitted_preview_png",
            side_effect=OSError("preview encode failed"),
        ):
            with self.assertRaisesRegex(OSError, "preview encode failed"):
                convert_texture_to_dxf(
                    input_path, dxf_path, 2, 2, 1,
                    tile_mode="repeat", voronoi_block_count=0,
                    preview_output_path=preview_path,
                )
        self.assertFalse(dxf_path.exists())
        self.assertFalse(preview_path.exists())

def test_existing_preview_is_not_replaced_and_dxf_is_not_published(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        input_path = root / "layer.tiff"
        dxf_path = root / "layer.dxf"
        preview_path = root / "layer.preview.png"
        Image.fromarray(np.zeros((2, 2), dtype=np.uint8)).save(
            input_path, dpi=(25.4, 25.4)
        )
        preview_path.write_bytes(b"foreign")
        with self.assertRaises(FileExistsError):
            convert_texture_to_dxf(
                input_path, dxf_path, 2, 2, 1,
                tile_mode="repeat", voronoi_block_count=0,
                preview_output_path=preview_path,
            )
        self.assertEqual(preview_path.read_bytes(), b"foreign")
        self.assertFalse(dxf_path.exists())
```

The production change that must make these tests fail is removal of preview staging/publication from the bundle.

- [ ] **Step 5: Generalize pair staging into an artifact bundle**

Inside `export_horizontal_hatch_dxf`:

```python
publish_bundle = block_metadata_output is not None or preview_output is not None
requested_outputs = [output_path]
if block_metadata_output is not None:
    requested_outputs.append(block_metadata_output)
if preview_output is not None:
    requested_outputs.append(preview_output)
```

Validate that all requested outputs share one parent, are distinct, and do not already exist. Create one `_OwnedStagedFile` per requested output in the existing private staging directory. Write DXF, metadata when requested, and the PNG when requested; bind, seal, validate, publish with no replacement, revalidate, restore modes, and roll back owned public entries through the existing `_OwnedHatchResources` path on any failure.

Add `validate_hatch_output_bundle(dxf_path, metadata_path, expected_metadata, preview_path, black_mask)` that preserves existing DXF/metadata checks and opens the staged PNG through a duplicated owned descriptor. Assert PNG format, mode `L`, size `(black_mask.shape[1], black_mask.shape[0])`, and exact pixel equality with `np.where(black_mask, 0, 255)`.

- [ ] **Step 6: Run focused and regression tests**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.FittedPreviewOutputTests -v
python3 -m unittest tests.test_texture_to_hatch_dxf -v
```

Expected: all tests pass with zero failures.

- [ ] **Step 7: Commit the Python boundary**

```bash
git add texture_to_hatch_dxf.py tests/test_texture_to_hatch_dxf.py
git commit -m "feat: publish fitted hatch preview png"
```

---

### Task 2: Model layer pairing and overlay interaction independently of Avalonia controls

**Files:**
- Create: `GrayscaleLayersMac/DxfLayerPreviewItem.cs`
- Create: `GrayscaleLayersMac/DxfOverlayState.cs`
- Create: `GrayscaleLayersMac.Tests/DxfLayerPreviewItemTests.cs`
- Create: `GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs`

**Interfaces:**
- Produces: `DxfLayerPreviewItem(Name, DxfPath, TexturePath, WidthMm, HeightMm)` and `HasTexture`.
- Produces: `DxfOverlayState.TextureAvailable`, `ShowTexture`, `ShowLines`, `ShowDirectionArrows`, `TextureOpacity`, `IsTopView`, `ShouldDrawTexture`, and `ShouldDrawDirectionArrows`.

- [ ] **Step 1: Write failing pairing tests**

```csharp
[TestMethod]
public void GeneratedLayerCarriesMatchingDxfTextureAndPhysicalBounds()
{
    var item = new DxfLayerPreviewItem(
        "第 03 层", "/tmp/layer_03.dxf", "/tmp/layer_03.preview.png", 30, 20);
    Assert.IsTrue(item.HasTexture);
    Assert.AreEqual(30, item.WidthMm);
    Assert.AreEqual(20, item.HeightMm);
}

[TestMethod]
public void ImportedDxfIsExplicitlyUnpaired()
{
    var item = DxfLayerPreviewItem.Imported("/tmp/imported.dxf");
    Assert.IsFalse(item.HasTexture);
    Assert.IsNull(item.TexturePath);
}

[TestMethod]
public void RejectsTextureWithoutPositivePhysicalBounds()
{
    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        new DxfLayerPreviewItem("bad", "a.dxf", "a.png", 0, 20));
}
```

- [ ] **Step 2: Write failing independent-state tests**

```csharp
[TestMethod]
public void TextureLinesAndArrowsToggleIndependently()
{
    var state = new DxfOverlayState();
    state.SetTextureAvailable(true);
    state.ShowTexture = false;
    Assert.IsTrue(state.ShowLines);
    Assert.IsTrue(state.ShowDirectionArrows);
    Assert.IsFalse(state.ShouldDrawTexture);
}

[TestMethod]
public void IsometricSuppressesTextureWithoutLosingSelection()
{
    var state = new DxfOverlayState();
    state.SetTextureAvailable(true);
    state.IsTopView = false;
    Assert.IsFalse(state.ShouldDrawTexture);
    Assert.IsTrue(state.ShowTexture);
    state.IsTopView = true;
    Assert.IsTrue(state.ShouldDrawTexture);
}

[TestMethod]
public void ArrowsRequireVisibleDxfLines()
{
    var state = new DxfOverlayState { ShowLines = false };
    Assert.IsFalse(state.ShouldDrawDirectionArrows);
}
```

- [ ] **Step 3: Run and verify the missing-type failures**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter "FullyQualifiedName~DxfLayerPreviewItemTests|FullyQualifiedName~DxfOverlayStateTests" --no-restore
```

Expected: compilation fails because both types are absent.

- [ ] **Step 4: Implement the minimal models**

```csharp
public sealed record DxfLayerPreviewItem
{
    public string Name { get; }
    public string DxfPath { get; }
    public string? TexturePath { get; }
    public double WidthMm { get; }
    public double HeightMm { get; }
    public bool HasTexture => TexturePath is not null;

    public DxfLayerPreviewItem(
        string name, string dxfPath, string? texturePath,
        double widthMm, double heightMm)
    {
        if (texturePath is not null &&
            (!double.IsFinite(widthMm) || widthMm <= 0 ||
             !double.IsFinite(heightMm) || heightMm <= 0))
            throw new ArgumentOutOfRangeException(nameof(widthMm));
        (Name, DxfPath, TexturePath, WidthMm, HeightMm) =
            (name, dxfPath, texturePath, widthMm, heightMm);
    }

    public static DxfLayerPreviewItem Imported(string path) =>
        new($"导入 · {Path.GetFileName(path)}", path, null, 0, 0);
    public override string ToString() => Name;
}
```

Implement `DxfOverlayState` without Avalonia dependencies:

```csharp
public sealed class DxfOverlayState
{
    private double _textureOpacity = 0.55;
    public bool TextureAvailable { get; private set; }
    public bool ShowTexture { get; set; } = true;
    public bool ShowLines { get; set; } = true;
    public bool ShowDirectionArrows { get; set; } = true;
    public bool IsTopView { get; set; }
    public double TextureOpacity
    {
        get => _textureOpacity;
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _textureOpacity = Math.Clamp(value, 0, 1);
        }
    }
    public bool ShouldDrawTexture => TextureAvailable && ShowTexture && IsTopView;
    public bool ShouldDrawDirectionArrows => ShowLines && ShowDirectionArrows;
    public void SetTextureAvailable(bool available) => TextureAvailable = available;
}
```

`SetTextureAvailable(false)` deliberately does not overwrite the saved `ShowTexture` preference.

- [ ] **Step 5: Run tests and commit**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter "FullyQualifiedName~DxfLayerPreviewItemTests|FullyQualifiedName~DxfOverlayStateTests" --no-restore
git add GrayscaleLayersMac/DxfLayerPreviewItem.cs GrayscaleLayersMac/DxfOverlayState.cs GrayscaleLayersMac.Tests/DxfLayerPreviewItemTests.cs GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs
git commit -m "feat: model paired dxf texture overlays"
```

---

### Task 3: Render and own the registered texture inside `DxfPreviewControl`

**Files:**
- Modify: `GrayscaleLayersMac/DxfPreviewControl.cs`
- Test: `GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs`
- Test: `tests/test_texture_to_hatch_dxf.py`

**Interfaces:**
- Consumes: `DxfOverlayState` and `Avalonia.Media.Imaging.Bitmap`.
- Produces: `LoadTexture(string path, double widthMm, double heightMm)`, `ClearTexture()`, `ShowTexture`, `ShowLines`, `TextureOpacity`, `HasTexture`, and `TextureStatus`.
- Ownership: the control disposes every bitmap it replaces or clears and implements `IDisposable` for window shutdown.

- [ ] **Step 1: Add a failing source-contract test for shared coordinate mapping and disposal**

```python
def test_dxf_control_draws_texture_with_dxf_transform_and_owns_bitmap(self) -> None:
    source = (ROOT / "GrayscaleLayersMac" / "DxfPreviewControl.cs").read_text()
    self.assertIn("IDisposable", source)
    self.assertIn("public void LoadTexture(", source)
    self.assertIn("public void ClearTexture()", source)
    self.assertIn("_textureBitmap?.Dispose()", source)
    render = source[source.index("public override void Render"):]
    self.assertLess(render.index("DrawTextureOverlay"), render.index("DrawDxfSegments"))
    self.assertIn("ToScreen(_textureBounds.Left", source)
    self.assertIn("ToScreen(_textureBounds.Right", source)
```

The production change that makes this test fail is removal of the registered overlay render path or bitmap disposal.

- [ ] **Step 2: Run the contract test and verify RED**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.AvaloniaTextureOverlaySourceContractTests -v
```

Expected: FAIL because the overlay members do not exist.

- [ ] **Step 3: Implement bitmap lifecycle and overlay properties**

Add fields:

```csharp
private readonly DxfOverlayState _overlay = new();
private Bitmap? _textureBitmap;
private Rect _textureBounds;

public bool HasTexture => _textureBitmap is not null;
public bool ShowTexture { get => _overlay.ShowTexture; set { _overlay.ShowTexture = value; InvalidateVisual(); } }
public bool ShowLines { get => _overlay.ShowLines; set { _overlay.ShowLines = value; InvalidateVisual(); } }
public double TextureOpacity { get => _overlay.TextureOpacity; set { _overlay.TextureOpacity = value; InvalidateVisual(); } }
```

`LoadTexture` validates regular non-reparse input, positive finite physical bounds, decodes into a candidate `Bitmap`, rejects non-positive pixel sizes, then atomically replaces and disposes the previous bitmap:

```csharp
public void LoadTexture(string path, double widthMm, double heightMm)
{
    if (!double.IsFinite(widthMm) || widthMm <= 0 ||
        !double.IsFinite(heightMm) || heightMm <= 0)
        throw new ArgumentOutOfRangeException(nameof(widthMm));
    var file = new FileInfo(path);
    file.Refresh();
    if (!file.Exists || file.Length <= 0 ||
        (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        throw new InvalidDataException("配准纹理必须是非空普通文件。");

    Bitmap? candidate = null;
    try
    {
        candidate = new Bitmap(path);
        if (candidate.PixelSize.Width <= 0 || candidate.PixelSize.Height <= 0)
            throw new InvalidDataException("配准纹理像素尺寸无效。");
        var previous = _textureBitmap;
        _textureBitmap = candidate;
        candidate = null;
        _textureBounds = new Rect(-widthMm / 2, -heightMm / 2, widthMm, heightMm);
        _modelBounds = _textureBounds;
        _overlay.SetTextureAvailable(true);
        previous?.Dispose();
        FitToView();
    }
    finally
    {
        candidate?.Dispose();
    }
}

public void ClearTexture()
{
    _textureBitmap?.Dispose();
    _textureBitmap = null;
    _overlay.SetTextureAvailable(false);
    InvalidateVisual();
}

public void Dispose() => ClearTexture();
```

`Clear()` calls `ClearTexture()` before resetting the DXF state.

- [ ] **Step 4: Split rendering into ordered texture and DXF helpers**

Keep the existing `CalculateScale`, `_modelBounds`, `_zoom`, and `_pan` path. For a paired generated layer, set `_modelBounds = new Rect(-widthMm / 2, -heightMm / 2, widthMm, heightMm)` after both files load so whitespace at the processing boundary remains visible even when DXF lines do not reach an edge.

In top view, calculate the destination corners through the same transform:

```csharp
var topLeft = ToScreen(_textureBounds.Left, _textureBounds.Bottom, 0, scale, center);
var bottomRight = ToScreen(_textureBounds.Right, _textureBounds.Top, 0, scale, center);
var destination = new Rect(topLeft, bottomRight);
using (context.PushOpacity(_overlay.TextureOpacity))
    context.DrawImage(_textureBitmap, new Rect(_textureBitmap.Size), destination);
```

The swapped model Y values are the explicit image-Y/DXF-Y flip. Extract the existing line loop to `DrawDxfSegments`; call it only when `_overlay.ShowLines`, and draw arrows only when `_overlay.ShouldDrawDirectionArrows`. Update `SetTopView`, `SetIsometricView`, and orbiting so `_overlay.IsTopView` tracks the actual view before invalidation.

- [ ] **Step 5: Run focused tests and build**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.AvaloniaTextureOverlaySourceContractTests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~DxfOverlayStateTests --no-restore
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Debug --no-restore
```

Expected: all commands exit 0.

- [ ] **Step 6: Commit the control**

```bash
git add GrayscaleLayersMac/DxfPreviewControl.cs GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs tests/test_texture_to_hatch_dxf.py
git commit -m "feat: overlay registered texture in dxf preview"
```

---

### Task 4: Wire generated layer triples and independent controls into the main pipeline

**Files:**
- Modify: `GrayscaleLayersMac/MainWindow.cs:25-40,173-237,882-957,1321-1432,1560-1597,1795-1805,2279-2334`
- Test: `tests/test_texture_to_hatch_dxf.py`

**Interfaces:**
- Consumes: Python `--preview-output`, `DxfLayerPreviewItem`, and `DxfPreviewControl.LoadTexture`.
- Produces: selecting one pipeline item loads its DXF and matching texture as one UI operation; imported DXFs remain unpaired.

- [ ] **Step 1: Write failing source-contract tests for pipeline pairing**

```python
def test_pipeline_requests_and_registers_matching_preview_png(self) -> None:
    source = (ROOT / "GrayscaleLayersMac" / "MainWindow.cs").read_text()
    loop = source[source.index("for (var index = 0;"):source.index("步骤 2/3 完成")]
    self.assertIn('Path.ChangeExtension(outputFile, ".preview.png")', loop)
    self.assertIn('hatchInfo.ArgumentList.Add("--preview-output")', loop)
    self.assertIn("ValidateGeneratedLayerArtifacts(", loop)
    self.assertIn("new DxfLayerPreviewItem(", loop)

def test_selector_clears_stale_texture_before_loading_new_item(self) -> None:
    source = (ROOT / "GrayscaleLayersMac" / "MainWindow.cs").read_text()
    handler = source[source.index("_pipelineDxfSelector.SelectionChanged"):source.index("_dpiBox.TextChanged")]
    self.assertIn("_pipelineDxfPreview.ClearTexture()", handler)
    self.assertIn("item.HasTexture", handler)
    self.assertIn("_pipelineDxfPreview.LoadTexture", handler)
```

- [ ] **Step 2: Run and verify RED**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.AvaloniaLayerOverlayWiringTests -v
```

Expected: FAIL because pipeline items do not contain preview paths and no CLI preview argument is passed.

- [ ] **Step 3: Replace the nested DXF item and load paired selections safely**

Remove the nested `DxfPreviewItem` and change the collection/selector to `DxfLayerPreviewItem`. Add one helper with an all-or-clear failure boundary:

```csharp
private bool LoadPipelineLayerPreview(DxfLayerPreviewItem item)
{
    _pipelineDxfPreview.ClearTexture();
    if (!LoadDxfPreview(_pipelineDxfPreview, _pipelineDxfPreviewStatus, item.DxfPath))
        return false;
    if (item.HasTexture)
    {
        try
        {
            _pipelineDxfPreview.LoadTexture(
                item.TexturePath!, item.WidthMm, item.HeightMm);
        }
        catch (Exception error)
        {
            _pipelineDxfPreview.ClearTexture();
            _pipelineDxfPreviewStatus.Text = $"无法加载配准纹理：{error.Message}";
            return false;
        }
    }
    _pipelineSharedPreview.Selection.CompleteDxfLoad();
    SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Dxf);
    return true;
}
```

The selector handler calls only this helper. Imported items use `DxfLayerPreviewItem.Imported(path)` so loading them always clears the preceding paired bitmap.

- [ ] **Step 4: Request, validate, and register each generated preview**

In the pipeline loop:

```csharp
var previewFile = Path.ChangeExtension(outputFile, ".preview.png");
hatchInfo.ArgumentList.Add("--preview-output");
hatchInfo.ArgumentList.Add(previewFile);
ValidateGeneratedLayerArtifacts(
    outputFile,
    previewFile,
    (_pipelineBlocksBox.Value ?? 0) > 0);
var previewItem = new DxfLayerPreviewItem(
    $"第 {index + 1:D2} 层 · {Path.GetFileName(outputFile)}",
    outputFile,
    previewFile,
    (double)width,
    (double)height);
```

Rename `ValidateGeneratedLayerPair` to `ValidateGeneratedLayerArtifacts`; keep existing DXF and metadata checks and always validate the PNG as a nonempty regular non-reparse file before selector registration.

- [ ] **Step 5: Add independent overlay controls to the DXF toolbar**

Extend `MakeDxfPreviewContent` with:

```csharp
var textureCheckBox = new CheckBox { Content = "显示灰度纹理", IsChecked = true };
var linesCheckBox = new CheckBox { Content = "显示 DXF 填充线", IsChecked = true };
var textureOpacity = new Slider { Minimum = 0, Maximum = 1, Value = 0.55, Width = 110 };
```

Bind events to `preview.ShowTexture`, `preview.ShowLines`, and `preview.TextureOpacity`. Add `UpdateOverlayControlAvailability()` so texture checkbox/slider depend on `preview.HasTexture`, while the arrow checkbox depends on line visibility. Preserve checkbox values when disabled. Invoke the updater after every selection load, clear, and failure. Append top/isometric and unpaired-texture explanations to the DXF status without replacing parse errors.

- [ ] **Step 6: Dispose the pipeline DXF bitmap at shutdown**

Extend the existing close cleanup:

```csharp
private void DisposeTexturePreviews()
{
    _hatchPreviewController.Dispose();
    _pipelinePreviewController.Dispose();
    _hatchDxfPreview.Dispose();
    _pipelineDxfPreview.Dispose();
}
```

Ensure it remains called exactly once through `Closed`.

- [ ] **Step 7: Run focused tests and commit**

```bash
python3 -m unittest tests.test_texture_to_hatch_dxf.AvaloniaLayerOverlayWiringTests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Debug --no-restore
git add GrayscaleLayersMac/MainWindow.cs tests/test_texture_to_hatch_dxf.py
git commit -m "feat: pair pipeline layers with texture overlays"
```

---

### Task 5: Document and verify the complete feature

**Files:**
- Modify: `GrayscaleLayersMac/README.md`
- Verify: all modified production and test files.

**Interfaces:**
- Documents: `.preview.png` artifacts, top-view-only alignment, independent toggles, opacity, and unpaired DXF behavior.

- [ ] **Step 1: Update user documentation**

Add a paragraph after the existing DXF preview description explaining:

```markdown
主流程为每层 DXF 同时生成同名 `.preview.png` 配准纹理。选择某层时，顶视图默认叠加该层实际裁剪或平铺后的二值纹理与 Hatch 线；“显示灰度纹理”和“显示 DXF 填充线”可独立开关，纹理透明度可调。二维纹理只在顶视图显示，等轴测仍用于观察 DXF；手动导入且没有配对 PNG 的 DXF 会明确显示为无配准纹理。
```

- [ ] **Step 2: Run fresh full verification**

```bash
python3 -m unittest discover -s tests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
git diff --check
git status --short
```

Expected: Python and C# report zero failed tests, Release build exits 0, `git diff --check` is silent, and status lists only intended changes plus the user's pre-existing unrelated untracked paths.

- [ ] **Step 3: Perform the manual alignment acceptance check**

Run the app with `dotnet run --project GrayscaleLayersMac/GrayscaleLayersMac.csproj`, generate at least three layers from one source, and verify:

1. Each selected layer shows its own fitted texture and DXF by default.
2. Black/white texture boundaries align with Hatch inclusion/exclusion at top view.
3. Texture, DXF lines, and arrows can be independently suppressed; opacity visibly changes only the texture.
4. Fit, zoom, and pan keep image and lines registered.
5. Isometric view hides the texture with a clear explanation; top view restores it.
6. Selecting an imported unpaired DXF clears the previous texture and disables texture controls.
7. Cancelling after completed layers leaves only completed paired items selectable.

- [ ] **Step 4: Commit documentation**

```bash
git add GrayscaleLayersMac/README.md
git commit -m "docs: explain layered dxf texture overlay"
```
