# Top Import Progress Reverse Collapse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an anchored, non-modal Reverse Collapse progress overlay to the top “导入中间结果” file and folder imports without changing their existing results or the separate pipeline execution window.

**Architecture:** A small immutable progress model separates import state from rendering. An Avalonia `Popup` anchored to `_pipelineImportButton` renders and animates the state. Import preparation validates and prepares every TIFF/DXF before one commit phase, so mixed imports remain all-or-nothing while reporting monotonic progress.

**Tech Stack:** C# 14, .NET 10, Avalonia 11.3.18, FluentIcons.Avalonia 2.0.317, MSTest, existing Python pytest suite.

## Global Constraints

- Apply this feature only to the top “导入中间结果” file and folder entries.
- Do not change the file picker options, supported file types, preview-selection rules, path-field semantics, log semantics, “全部执行”, or single-step execution.
- Do not add cancellation, history, queues, background notifications, or parallel parsing.
- Anchor the overlay below the top import button; it must not move the main layout.
- Normal motion: 280 ms no-bounce spatial expansion plus fade; success remains visible for 600 ms before reversing along the same path.
- Reduced motion: no spatial transition; retain an 80 ms fade.
- User cancellation of the system picker must not open the overlay.
- Mixed TIFF/DXF imports must finish all validation and preparation before committing any new UI state.
- Expected import failures stay in the overlay with a “关闭” action and do not duplicate the same error in a modal dialog.
- Preserve all unrelated untracked files and user changes.

---

### Task 1: Define the import progress state contract

**Files:**
- Create: `GrayscaleLayersMac/ImportProgressState.cs`
- Create: `GrayscaleLayersMac.Tests/ImportProgressStateTests.cs`

**Interfaces:**
- Produces: `ImportProgressStage` enum.
- Produces: immutable `ImportProgressState` record with `Stage`, `Current`, `Total`, `CurrentFileName`, `Message`, `IsTerminal`, `IsError`, `IsIndeterminate`, `ProgressValue`, `CounterText`, and `AutomationText`.
- Consumed by: `ImportProgressOverlay` and `PipelineImportPreparation` in later tasks.

- [ ] **Step 1: Write failing state-format tests**

```csharp
[TestMethod]
public void ScanningIsIndeterminateAndHasNoCounter()
{
    var state = ImportProgressState.Scanning("正在扫描文件…");
    Assert.IsTrue(state.IsIndeterminate);
    Assert.IsNull(state.ProgressValue);
    Assert.AreEqual(string.Empty, state.CounterText);
}

[TestMethod]
public void ValidationFormatsMonotonicCountAndAccessibleText()
{
    var state = ImportProgressState.ValidatingTiff(4, 10, "/tmp/layer_04.tiff");
    Assert.AreEqual(0.4, state.ProgressValue);
    Assert.AreEqual("正在检查分层 TIFF · 4/10", state.CounterText);
    StringAssert.Contains(state.AutomationText, "layer_04.tiff");
}

[TestMethod]
public void FailureAndSuccessAreTerminalButOnlyFailureIsError()
{
    Assert.IsTrue(ImportProgressState.Succeeded(10).IsTerminal);
    Assert.IsFalse(ImportProgressState.Succeeded(10).IsError);
    Assert.IsTrue(ImportProgressState.Failed("坏文件", "无法读取").IsError);
}
```

- [ ] **Step 2: Run the focused tests and confirm the type is missing**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter FullyQualifiedName~ImportProgressStateTests`

Expected: FAIL because `ImportProgressState` and `ImportProgressStage` do not exist.

- [ ] **Step 3: Implement the immutable state and named factories**

```csharp
internal enum ImportProgressStage
{
    Scanning,
    ValidatingTiff,
    ValidatingDxf,
    LoadingPreview,
    Succeeded,
    Failed
}

internal sealed record ImportProgressState(
    ImportProgressStage Stage,
    int Current,
    int? Total,
    string? CurrentFileName,
    string Message)
{
    public bool IsTerminal => Stage is ImportProgressStage.Succeeded or ImportProgressStage.Failed;
    public bool IsError => Stage == ImportProgressStage.Failed;
    public bool IsIndeterminate => Total is null;
    public double? ProgressValue => Total is > 0 ? (double)Current / Total.Value : null;

    public string CounterText => Stage switch
    {
        ImportProgressStage.ValidatingTiff => $"正在检查分层 TIFF · {Current}/{Total}",
        ImportProgressStage.ValidatingDxf => $"正在检查 DXF · {Current}/{Total}",
        ImportProgressStage.LoadingPreview => $"正在加载预览 · {Current}/{Total}",
        ImportProgressStage.Succeeded => $"已导入 {Current} 个文件",
        _ => string.Empty
    };

    public string AutomationText => string.Join("，", new[]
    {
        Message,
        CounterText,
        CurrentFileName is null ? string.Empty : Path.GetFileName(CurrentFileName)
    }.Where(value => value.Length > 0));

    public static ImportProgressState Scanning(string message) =>
        new(ImportProgressStage.Scanning, 0, null, null, message);

    public static ImportProgressState ValidatingTiff(int current, int total, string file) =>
        Counted(ImportProgressStage.ValidatingTiff, current, total, file, "正在检查分层 TIFF…");

    public static ImportProgressState ValidatingDxf(int current, int total, string file) =>
        Counted(ImportProgressStage.ValidatingDxf, current, total, file, "正在检查 DXF…");

    public static ImportProgressState LoadingPreview(int current, int total, string message) =>
        Counted(ImportProgressStage.LoadingPreview, current, total, null, message);

    public static ImportProgressState Succeeded(int total) =>
        Counted(ImportProgressStage.Succeeded, total, total, null, $"已导入 {total} 个文件");

    public static ImportProgressState Failed(string? file, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(ImportProgressStage.Failed, 0, null, file, message);
    }

    private static ImportProgressState Counted(
        ImportProgressStage stage, int current, int total, string? file, string message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        if (current > total)
            throw new ArgumentOutOfRangeException(nameof(current));
        return new(stage, current, total, file, message);
    }
}
```

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter FullyQualifiedName~ImportProgressStateTests`

Expected: PASS.

- [ ] **Step 5: Commit the state contract**

```bash
git add GrayscaleLayersMac/ImportProgressState.cs GrayscaleLayersMac.Tests/ImportProgressStateTests.cs
git commit -m "feat(ui): define import progress states"
```

---

### Task 2: Build the anchored Reverse Collapse overlay

**Files:**
- Create: `GrayscaleLayersMac/ImportProgressOverlay.cs`
- Create: `GrayscaleLayersMac.Tests/ImportProgressOverlayTests.cs`
- Modify: `GrayscaleLayersMac/UiIcons.cs`
- Modify: `GrayscaleLayersMac.Tests/UiIconsTests.cs`

**Interfaces:**
- Consumes: `ImportProgressState` from Task 1 and `MotionPreferences`.
- Produces: `ImportProgressOverlay(Control anchor, Func<TimeSpan, CancellationToken, Task>? delay = null)`.
- Produces: `Popup Root`, `Show`, `Update`, `ShowSucceededAndCollapseAsync`, `ShowFailure`, and `Close`.
- Produces: test-facing state properties `IsOpen`, `SurfaceHeight`, `SurfaceOpacity`, `TitleText`, `DetailText`, `CounterText`, `CloseButtonVisible`, `HasSpatialTransitions`, and `Placement`.

- [ ] **Step 1: Add failing overlay and icon tests**

```csharp
[TestMethod]
public void OverlayIsAnchoredBelowTheImportButton()
{
    var anchor = new Button();
    var overlay = new ImportProgressOverlay(anchor);
    Assert.AreSame(anchor, overlay.Root.PlacementTarget);
    Assert.AreEqual(PlacementMode.BottomEdgeAlignedRight, overlay.Placement);
    Assert.IsFalse(overlay.IsOpen);
}

[TestMethod]
public void NormalMotionUsesSpatialAndOpacityTransitions()
{
    using var _ = MotionPreferences.OverrideForTesting(false);
    var overlay = new ImportProgressOverlay(new Button());
    Assert.IsTrue(overlay.HasSpatialTransitions);
}

[TestMethod]
public void ReducedMotionUsesFadeOnly()
{
    using var _ = MotionPreferences.OverrideForTesting(true);
    var overlay = new ImportProgressOverlay(new Button());
    Assert.IsFalse(overlay.HasSpatialTransitions);
}
```

Extend `UiIconsTests.RequiredIconsMapToFluentGlyphs` with `Success -> CheckmarkCircle` and `Error -> ErrorCircle`.

- [ ] **Step 2: Run tests and verify the overlay/icons are absent**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~ImportProgressOverlayTests|FullyQualifiedName~UiIconsTests"`

Expected: FAIL because the overlay and two icon enum values do not exist.

- [ ] **Step 3: Implement the popup structure and semantic icons**

Add `Success` and `Error` to `UiIcon`, mapping them to `Icon.CheckmarkCircle` and `Icon.ErrorCircle`.

Construct the overlay around an Avalonia `Popup`:

```csharp
Root = new Popup
{
    PlacementTarget = anchor,
    Placement = PlacementMode.BottomEdgeAlignedRight,
    VerticalOffset = 8,
    IsLightDismissEnabled = false,
    Child = surface
};
```

Use a 320 px `Border` with `UiTheme.PopupBrush`, `UiTheme.BorderMediumBrush`, `UiTheme.CardRadius`, `ClipToBounds = true`, and the existing UI font. The content must include a Fluent icon, title, ellipsized detail text with Tooltip, `ProgressBar`, counter, and a hidden ghost-style “关闭” button. Use `UiTheme.SuccessBrush` / `SuccessTextBrush` for success and `UiTheme.WarningBrush` / `WarningTextBrush` for failure; do not introduce an unowned error palette.

Set `AutomationProperties.LiveSetting` to `AutomationLiveSetting.Polite`. Track the last announced `ImportProgressStage`: update the live-region automation name only when the stage changes or reaches success/failure, while the visible filename and count may update for every file. This prevents VoiceOver from announcing every file.

- [ ] **Step 4: Implement reversible show, success, failure, and close behavior**

```csharp
private static readonly TimeSpan SpatialMotion = TimeSpan.FromMilliseconds(280);
private static readonly TimeSpan SuccessHold = TimeSpan.FromMilliseconds(600);
private const double ExpandedHeight = 136;

public void Show(ImportProgressState state)
{
    Root.IsOpen = true;
    Apply(state);
    _surface.IsHitTestVisible = true;
    _surface.Height = ExpandedHeight;
    _surface.Opacity = 1;
}

public async Task ShowSucceededAndCollapseAsync(
    ImportProgressState state,
    CancellationToken cancellationToken = default)
{
    Apply(state);
    await _delay(SuccessHold, cancellationToken);
    Close();
}

public void Close()
{
    _surface.IsHitTestVisible = false;
    _surface.Height = MotionPreferences.AnimateSpatialProperties ? 0 : ExpandedHeight;
    _surface.Opacity = 0;
    // Close the Popup after the active transition duration; guard with a
    // monotonically increasing generation so a new Show interrupts an old Close.
}
```

Attach transitions only after `AttachedToVisualTree`, matching `LogPanelView`: opacity always gets a fade transition; height and a small `TranslateTransform.Y` transition are added only when `MotionPreferences.AnimateSpatialProperties` is true. Use `CubicEaseOut`, no bounce. A generation counter must prevent an earlier delayed close from hiding a newly opened import.

`ShowFailure` keeps the popup open, switches to the error icon and warning/error semantic brushes named above, shows “关闭”, and moves keyboard focus to that button. The close button calls `Close()`.

- [ ] **Step 5: Add deterministic success and interruption tests**

Use an injected delay that returns a controlled `TaskCompletionSource`. Assert that success remains open until the delay completes, then closes; call `Show` for a new scanning state before the previous close finishes and assert the stale close cannot hide it. Assert failure stays open until `Close()`.

- [ ] **Step 6: Run the focused tests**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~ImportProgressOverlayTests|FullyQualifiedName~UiIconsTests|FullyQualifiedName~MotionPreferencesTests"`

Expected: PASS.

- [ ] **Step 7: Commit the overlay**

```bash
git add GrayscaleLayersMac/ImportProgressOverlay.cs GrayscaleLayersMac/UiIcons.cs GrayscaleLayersMac.Tests/ImportProgressOverlayTests.cs GrayscaleLayersMac.Tests/UiIconsTests.cs
git commit -m "feat(ui): add anchored import progress overlay"
```

---

### Task 3: Prepare TIFF layers without mutating the visible preview

**Files:**
- Create: `GrayscaleLayersMac/PreparedGrayscaleLayerSet.cs`
- Modify: `GrayscaleLayersMac/GrayscaleLayerPreviewController.cs`
- Modify: `GrayscaleLayersMac/GrayscaleLayerPreviewControl.cs`
- Modify: `GrayscaleLayersMac.Tests/GrayscaleLayerPreviewControllerTests.cs`
- Create: `GrayscaleLayersMac.Tests/PreparedGrayscaleLayerSetTests.cs`

**Interfaces:**
- Produces: disposable `PreparedGrayscaleLayerSet` containing fully decoded `GrayscaleLayerPreviewItem` instances.
- Produces: `GrayscaleLayerPreviewControl.PrepareLayerFilesAsync(IReadOnlyList<KeyValuePair<string, TextureImageInspection>>, CancellationToken)`.
- Produces: `GrayscaleLayerPreviewControl.CommitPreparedLayers(PreparedGrayscaleLayerSet)`.
- Produces: `GrayscaleLayerPreviewController.ReplaceLayers(IEnumerable<GrayscaleLayerPreviewItem>)` with ownership transfer.

- [ ] **Step 1: Write failing atomic-replacement tests**

```csharp
[TestMethod]
public void ReplaceLayersDoesNotChangeTheSourceSlotAndSelectsFirstNewLayer()
{
    using var controller = new GrayscaleLayerPreviewController();
    controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture("/tmp/source.png"));
    var replacement = new[] { new GrayscaleLayerPreviewItem("/tmp/new.tiff", 1) };
    controller.ReplaceLayers(replacement);
    Assert.AreEqual("source.png", Path.GetFileName(controller.Items[0].FilePath));
    Assert.AreSame(replacement[0], controller.SelectedItem);
}
```

Add tests proving an uncommitted `PreparedGrayscaleLayerSet.Dispose()` disposes its thumbnails, while `TakeItems()` transfers ownership and makes later disposal a no-op.

- [ ] **Step 2: Run the focused tests and confirm missing APIs**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~GrayscaleLayerPreviewControllerTests|FullyQualifiedName~PreparedGrayscaleLayerSetTests"`

Expected: FAIL on `ReplaceLayers` and `PreparedGrayscaleLayerSet`.

- [ ] **Step 3: Implement preparation and ownership transfer**

`PrepareLayerFilesAsync` must create new items off-screen, set each already-inspected PNG and decoded thumbnail, and dispose all newly created items if any decode fails. It must never call `_controller.RefreshFiles`.

Implement the ownership container exactly once:

```csharp
internal sealed class PreparedGrayscaleLayerSet : IDisposable
{
    private List<GrayscaleLayerPreviewItem>? _items;

    public PreparedGrayscaleLayerSet(IEnumerable<GrayscaleLayerPreviewItem> items) =>
        _items = items.ToList();

    public IReadOnlyList<GrayscaleLayerPreviewItem> TakeItems()
    {
        var items = _items ?? throw new InvalidOperationException("分层预览已经提交。");
        _items = null;
        return items;
    }

    public void Dispose()
    {
        if (_items is null) return;
        foreach (var item in _items) item.Dispose();
        _items = null;
    }
}
```

```csharp
public async Task<PreparedGrayscaleLayerSet> PrepareLayerFilesAsync(
    IReadOnlyList<KeyValuePair<string, TextureImageInspection>> layers,
    CancellationToken cancellationToken)
{
    var prepared = new List<GrayscaleLayerPreviewItem>();
    try
    {
        foreach (var pair in layers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new GrayscaleLayerPreviewItem(pair.Key, prepared.Count + 1);
            using var stream = new MemoryStream(pair.Value.PreviewPng, writable: false);
            var thumbnail = Bitmap.DecodeToWidth(stream, 120, BitmapInterpolationMode.MediumQuality);
            item.SetPreview(pair.Value.PreviewPng, pair.Value.Info.PixelWidth,
                pair.Value.Info.PixelHeight, thumbnail);
            prepared.Add(item);
        }
        return new PreparedGrayscaleLayerSet(prepared);
    }
    catch
    {
        foreach (var item in prepared) item.Dispose();
        throw;
    }
}
```

If the compiler reports `CS1998`, remove `async` and return `Task.FromResult(new PreparedGrayscaleLayerSet(prepared))`; do not introduce an artificial `await`.

`CommitPreparedLayers` calls `ReplaceLayers(prepared.TakeItems())`, then `SyncItems()`. `ReplaceLayers` disposes the previous layer list only after the replacement enumerable has been materialized and validated, preserving the source slot.

- [ ] **Step 4: Run preview-controller and preparation tests**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~GrayscaleLayerPreviewControllerTests|FullyQualifiedName~PreparedGrayscaleLayerSetTests"`

Expected: PASS.

- [ ] **Step 5: Commit atomic TIFF preparation**

```bash
git add GrayscaleLayersMac/PreparedGrayscaleLayerSet.cs GrayscaleLayersMac/GrayscaleLayerPreviewController.cs GrayscaleLayersMac/GrayscaleLayerPreviewControl.cs GrayscaleLayersMac.Tests/GrayscaleLayerPreviewControllerTests.cs GrayscaleLayersMac.Tests/PreparedGrayscaleLayerSetTests.cs
git commit -m "refactor(ui): stage imported layer previews before commit"
```

---

### Task 4: Add a testable all-or-nothing import preparation coordinator

**Files:**
- Create: `GrayscaleLayersMac/PipelineImportPreparation.cs`
- Create: `GrayscaleLayersMac.Tests/PipelineImportPreparationTests.cs`

**Interfaces:**
- Consumes: `ImportProgressState` and `TextureImageInspection`.
- Produces: `PreparedPipelineImport(IReadOnlyList<KeyValuePair<string, TextureImageInspection>> TiffInspections, IReadOnlyList<string> DxfPaths)` with `TotalCount => TiffInspections.Count + DxfPaths.Count`.
- Produces: `PipelineImportPreparation.PrepareAsync(string[] tiffs, string[] dxfs, Func<string, CancellationToken, Task<TextureImageInspection>> inspectTiff, Action<string> validateDxf, IProgress<ImportProgressState> progress, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write failing coordinator tests**

Test these exact behaviors:

```csharp
[TestMethod]
public async Task MixedImportReportsOneMonotonicTotal()
{
    var states = new List<ImportProgressState>();
    var result = await PipelineImportPreparation.PrepareAsync(
        ["/tmp/a.tiff", "/tmp/b.tiff"], ["/tmp/c.dxf"],
        FakeInspectionAsync, _ => { },
        new InlineProgress<ImportProgressState>(states.Add), CancellationToken.None);
    CollectionAssert.AreEqual(new[] { 1, 2, 3 },
        states.Where(x => x.Stage is ImportProgressStage.ValidatingTiff or ImportProgressStage.ValidatingDxf)
            .Select(x => x.Current).ToArray());
    Assert.IsTrue(states.All(x => x.Total is null or 3));
    Assert.AreEqual(3, result.TotalCount);
}

private static Task<TextureImageInspection> FakeInspectionAsync(
    string _, CancellationToken __) => Task.FromResult(new TextureImageInspection(
        new TextureImageInfo(1, 1, null, null),
        [137, 80, 78, 71, 13, 10, 26, 10]));

private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
```

Add a failure test where TIFF validation succeeds and DXF validation throws. Assert `PrepareAsync` throws, returns no `PreparedPipelineImport`, and never reports `LoadingPreview` or `Succeeded`.

- [ ] **Step 2: Run tests and verify the coordinator is missing**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter FullyQualifiedName~PipelineImportPreparationTests`

Expected: FAIL because `PipelineImportPreparation` does not exist.

- [ ] **Step 3: Implement ordered validation and progress reporting**

Materialize both arrays first. Report TIFF progress as `1..tiffCount`; report DXF progress as `tiffCount + 1..total`. Store successful TIFF inspections in ordered key/value pairs. Validate all DXFs before constructing the returned `PreparedPipelineImport`. Wrap errors with the existing actionable wording:

```csharp
internal sealed record PreparedPipelineImport(
    IReadOnlyList<KeyValuePair<string, TextureImageInspection>> TiffInspections,
    IReadOnlyList<string> DxfPaths)
{
    public int TotalCount => TiffInspections.Count + DxfPaths.Count;
}
```

```csharp
throw new InvalidDataException(
    $"无法读取分层 TIFF {Path.GetFileName(file)}：{error.Message}", error);
```

and

```csharp
throw new InvalidDataException(
    $"无法读取 DXF {Path.GetFileName(file)}：{error.Message}", error);
```

- [ ] **Step 4: Run coordinator tests**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter FullyQualifiedName~PipelineImportPreparationTests`

Expected: PASS.

- [ ] **Step 5: Commit the coordinator**

```bash
git add GrayscaleLayersMac/PipelineImportPreparation.cs GrayscaleLayersMac.Tests/PipelineImportPreparationTests.cs
git commit -m "feat(ui): report staged import preparation progress"
```

---

### Task 5: Integrate the overlay into the two top import entries

**Files:**
- Modify: `GrayscaleLayersMac/MainWindow.cs`
- Modify: `GrayscaleLayersMac.Tests/UiStructureContractTests.cs`
- Create: `GrayscaleLayersMac.Tests/PipelineImportFlowContractTests.cs`

**Interfaces:**
- Consumes: `ImportProgressOverlay`, `PipelineImportPreparation`, and `PreparedGrayscaleLayerSet`.
- Changes only: `CreatePipelineImportMenuButton`, `ImportPipelineDirectoryAsync`, `ImportPipelineFilesAsync`, `ImportLayerTiffsAsync`, and DXF validation/commit helpers.

- [ ] **Step 1: Write failing integration contracts**

Add source-contract assertions that `MainWindow` owns exactly one overlay anchored to `_pipelineImportButton`, adds its `Root` to the outer content grid, and does not construct `ProcessingProgressWindow` inside either import method.

Add focused flow tests around an extracted internal `RunPreparedImportAsync` coordinator using fake prepare/commit delegates:

- picker cancellation never calls `overlay.Show`;
- mixed validation failure calls no commit delegate;
- success commits TIFF then DXF once, reports success, and preserves existing log summary wording;
- expected failure calls `ShowFailure` and does not call `ShowMessageAsync`.

- [ ] **Step 2: Run the integration tests and verify they fail**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~PipelineImportFlowContractTests|FullyQualifiedName~UiStructureContractTests"`

Expected: FAIL because the overlay is not wired and the import flow is not staged.

- [ ] **Step 3: Add the overlay to the existing root grid**

Create `_pipelineImportProgress = new ImportProgressOverlay(_pipelineImportButton);` after the button is styled. Store the outer `Grid` in a local variable, add the header and content exactly as today, then add `_pipelineImportProgress.Root` without assigning a new row that changes layout.

Do not replace the import Flyout or header tool group.

- [ ] **Step 4: Move validation before commit and report each phase**

After a non-empty picker result:

```csharp
var latestProgress = ImportProgressState.Scanning("正在扫描文件…");
IProgress<ImportProgressState> progress = new Progress<ImportProgressState>(state =>
{
    latestProgress = state;
    _pipelineImportProgress.Update(state);
});
_pipelineImportProgress.Show(latestProgress);
var prepared = await PipelineImportPreparation.PrepareAsync(
    tiffs, dxfs, InspectTextureImageAsync, ValidateImportedDxf,
    progress, CancellationToken.None);
using var preparedLayers = await _pipelineTextureSurface.PrepareLayerFilesAsync(
    prepared.TiffInspections, CancellationToken.None);
progress.Report(ImportProgressState.LoadingPreview(
    prepared.TotalCount, prepared.TotalCount, "正在加载预览…"));
```

Only after all lines above succeed:

- call `CommitPreparedLayers` if TIFF files exist;
- build and install validated `DxfLayerPreviewItem` objects if DXF files exist;
- update path fields, selection, report text, and logs using the existing wording;
- call `await _pipelineImportProgress.ShowSucceededAndCollapseAsync(ImportProgressState.Succeeded(prepared.TotalCount));`.

Split current `LoadPipelineDxfImports` into `ValidateImportedDxf(string path)` and `CommitPipelineDxfImports(...)`; the commit method must not parse files again.

- [ ] **Step 5: Route expected errors to the overlay**

In both top import methods, replace duplicated import-error dialogs with:

```csharp
AppendPipelineLog($"导入失败：{error.Message}");
_pipelineImportProgress.ShowFailure(
    ImportProgressState.Failed(latestProgress.CurrentFileName, error.Message));
```

Keep `ShowMessageAsync` for system picker failures that occur before the overlay opens. Keep the existing `finally` block that resets `_pipelineProgress.IsIndeterminate` and re-enables the three conflicting actions.

- [ ] **Step 6: Run focused integration and existing import tests**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore --filter "FullyQualifiedName~PipelineImport|FullyQualifiedName~UiStructureContractTests|FullyQualifiedName~PipelineArtifactDiscoveryTests"`

Expected: PASS.

- [ ] **Step 7: Commit MainWindow integration**

```bash
git add GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac.Tests/UiStructureContractTests.cs GrayscaleLayersMac.Tests/PipelineImportFlowContractTests.cs
git commit -m "feat(ui): show progress for top artifact imports"
```

---

### Task 6: Complete regression, packaging, and visual QA

**Files:**
- Modify only if QA exposes a defect: files introduced or changed in Tasks 1–5.
- Do not add generated `artifacts/` output to git.

**Interfaces:**
- Verifies the complete feature against the approved design specification.

- [ ] **Step 1: Run formatting and diff checks**

Run: `dotnet format GrayscaleLayersMac.sln --no-restore --verify-no-changes`

Expected: exit code 0. If it fails, run `dotnet format GrayscaleLayersMac.sln --no-restore`, inspect the diff, and commit only mechanical formatting with the relevant task changes.

Run: `git diff --check`

Expected: no output.

- [ ] **Step 2: Run the complete .NET suite**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore`

Expected: all tests pass; baseline before this feature is 245 tests.

- [ ] **Step 3: Run the complete Python suite**

Run: `python3 -m pytest -q`

Expected: all tests pass; baseline before this feature is 212 tests.

- [ ] **Step 4: Build the macOS application bundle**

Run: `./scripts/build-macos-app.sh`

Expected: `应用包验证通过` and a bundle at `artifacts/macos-arm64/灰度图分层工具.app`.

- [ ] **Step 5: Perform normal-motion visual QA with real safe files**

Open the packaged app and verify both top menu entries:

1. Cancel the system picker: no overlay appears.
2. Import one valid TIFF: overlay opens below “导入”, shows scanning/validation/loading, then completion for 600 ms and reverse-collapses.
3. Import a valid mixed TIFF/DXF selection: the counter increases across both types without resetting.
4. Import a damaged TIFF or DXF: old preview remains, overlay stays open with filename, error, and keyboard-accessible “关闭”.
5. Resize and move the window while the overlay is open: it remains aligned to the import button and inside the window edge.
6. Switch light/dark appearance and repeat one success and one failure: contrast and semantic state remain clear.

- [ ] **Step 6: Perform reduced-motion QA**

Launch with `GRAYSCALE_LAYERS_REDUCE_MOTION=1`, import a valid file, and verify that the overlay uses fade only, with no height or translation animation. Confirm completion feedback remains visible and closes.

- [ ] **Step 7: Commit any QA-only fixes and record final status**

If QA required code changes, rerun Steps 1–6 and commit those exact files:

```bash
git add GrayscaleLayersMac/ImportProgressState.cs GrayscaleLayersMac/ImportProgressOverlay.cs GrayscaleLayersMac/UiIcons.cs GrayscaleLayersMac/PreparedGrayscaleLayerSet.cs GrayscaleLayersMac/GrayscaleLayerPreviewController.cs GrayscaleLayersMac/GrayscaleLayerPreviewControl.cs GrayscaleLayersMac/PipelineImportPreparation.cs GrayscaleLayersMac/MainWindow.cs GrayscaleLayersMac.Tests/ImportProgressStateTests.cs GrayscaleLayersMac.Tests/ImportProgressOverlayTests.cs GrayscaleLayersMac.Tests/UiIconsTests.cs GrayscaleLayersMac.Tests/GrayscaleLayerPreviewControllerTests.cs GrayscaleLayersMac.Tests/PreparedGrayscaleLayerSetTests.cs GrayscaleLayersMac.Tests/PipelineImportPreparationTests.cs GrayscaleLayersMac.Tests/UiStructureContractTests.cs GrayscaleLayersMac.Tests/PipelineImportFlowContractTests.cs
git commit -m "fix(ui): polish import progress overlay"
```

If QA required no changes, do not create an empty commit. Finish with `git status --short` and verify only pre-existing untracked user files remain.
