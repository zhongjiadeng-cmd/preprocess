using System;
using System.Collections.Generic;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class TexturePreviewControllerTests
{
    [TestMethod]
    public void Reset_DiscardsPreviewButKeepsControllerUsable()
    {
        var displayed = new List<IDisposable?>();
        using var controller = new TexturePreviewController(displayed.Add, _ => { });
        var request = controller.BeginImport();
        controller.TryCompleteImport(
            request,
            new TrackedPreview(),
            new TextureImageInfo(600, 300, 300, 150),
            fallbackDpiText: "96",
            minimum: 0.01m,
            maximum: 100000m,
            out _);
        Assert.AreEqual(TexturePreviewPhase.Ready, controller.State.Phase);

        controller.Reset();

        Assert.AreEqual(TexturePreviewState.Empty, controller.State);
        Assert.IsNull(controller.CurrentInfo);
        Assert.IsNull(displayed[^1]);

        // Close 是终态（之后 BeginImport 会抛 ObjectDisposedException）；
        // Reset 之后必须还能继续导入，否则"清空缓存"会顺手把界面废掉。
        controller.BeginImport();
        Assert.AreEqual(TexturePreviewState.Loading, controller.State);
    }

    [TestMethod]
    public void Import_WithEmbeddedDpi_ProducesTargetWrite()
    {
        var displayed = new List<IDisposable?>();
        var targetWidth = 1m;
        var targetHeight = 2m;
        using var controller = new TexturePreviewController(
            displayed.Add,
            update =>
            {
                targetWidth = update.Width;
                targetHeight = update.Height;
            });
        var request = controller.BeginImport();
        var preview = new TrackedPreview();

        var completed = controller.TryCompleteImport(
            request,
            preview,
            new TextureImageInfo(600, 300, 300, 150),
            fallbackDpiText: "96",
            minimum: 0.01m,
            maximum: 100000m,
            out var update);

        Assert.IsTrue(completed);
        Assert.IsTrue(update.ShouldWriteTargets);
        Assert.AreEqual(50.8m, update.Width);
        Assert.AreEqual(50.8m, update.Height);
        Assert.AreEqual("物理尺寸：50.8 × 50.8 mm", update.PhysicalSizeText);
        Assert.AreEqual(50.8m, targetWidth);
        Assert.AreEqual(50.8m, targetHeight);
        Assert.AreEqual(TexturePreviewPhase.Ready, controller.State.Phase);
        Assert.AreSame(preview, displayed[^1]);
        Assert.IsFalse(preview.IsDisposed);
    }

    [TestMethod]
    public void Import_WithoutEmbeddedDpi_PreservesManualTargetsDespiteRetainedFallback()
    {
        var targetWidth = 73.125m;
        var targetHeight = 41.875m;
        using var controller = new TexturePreviewController(
            _ => { },
            update =>
            {
                targetWidth = update.Width;
                targetHeight = update.Height;
            });
        var request = controller.BeginImport();

        Assert.IsTrue(controller.TryCompleteImport(
            request,
            new TrackedPreview(),
            new TextureImageInfo(100, 50, null, null),
            fallbackDpiText: "200",
            minimum: 0.01m,
            maximum: 100000m,
            out var update));
        Assert.IsFalse(update.ShouldWriteTargets);
        Assert.AreEqual(73.125m, targetWidth);
        Assert.AreEqual(41.875m, targetHeight);
        Assert.AreEqual("物理尺寸：等待填写有效 DPI", controller.State.PhysicalSizeText);
    }

    [TestMethod]
    public void FallbackEdit_WithoutEmbeddedDpi_ProducesTargetWrite()
    {
        var targetWidth = 73m;
        var targetHeight = 41m;
        using var controller = new TexturePreviewController(
            _ => { },
            update =>
            {
                targetWidth = update.Width;
                targetHeight = update.Height;
            });
        var request = controller.BeginImport();
        Assert.IsTrue(controller.TryCompleteImport(
            request,
            new TrackedPreview(),
            new TextureImageInfo(100, 50, null, null),
            fallbackDpiText: "200",
            minimum: 0.01m,
            maximum: 100000m,
            out _));

        var update = controller.ApplyFallbackDpiEdit("100", 0.01m, 100000m);

        Assert.IsTrue(update.ShouldWriteTargets);
        Assert.AreEqual(25.4m, update.Width);
        Assert.AreEqual(12.7m, update.Height);
        Assert.AreEqual(25.4m, targetWidth);
        Assert.AreEqual(12.7m, targetHeight);
        Assert.AreEqual("物理尺寸：25.4 × 12.7 mm", controller.State.PhysicalSizeText);
    }

    [TestMethod]
    public void FallbackEdit_WithEmbeddedDpi_DoesNotOverwriteManualTargets()
    {
        var targetWidth = 1m;
        var targetHeight = 2m;
        using var controller = new TexturePreviewController(
            _ => { },
            update =>
            {
                targetWidth = update.Width;
                targetHeight = update.Height;
            });
        var request = controller.BeginImport();
        Assert.IsTrue(controller.TryCompleteImport(
            request,
            new TrackedPreview(),
            new TextureImageInfo(600, 300, 300, 150),
            fallbackDpiText: null,
            minimum: 0.01m,
            maximum: 100000m,
            out _));
        targetWidth = 72m;
        targetHeight = 44m;

        var update = controller.ApplyFallbackDpiEdit("96", 0.01m, 100000m);

        Assert.IsFalse(update.ShouldWriteTargets);
        Assert.AreEqual(72m, targetWidth);
        Assert.AreEqual(44m, targetHeight);
        Assert.AreEqual("物理尺寸：50.8 × 50.8 mm", controller.State.PhysicalSizeText);
    }

    [TestMethod]
    public void FailedImport_PreservesTargetsAndUsesActionableBoundedSummary()
    {
        var targetWidth = 73m;
        var targetHeight = 41m;
        using var controller = new TexturePreviewController(
            _ => { },
            update =>
            {
                targetWidth = update.Width;
                targetHeight = update.Height;
            });
        var request = controller.BeginImport();

        Assert.IsTrue(controller.TryFail(
            request,
            new InvalidOperationException("图片预览数据不是有效 PNG。\nTraceback (most recent call last): raw stderr")));

        Assert.AreEqual(73m, targetWidth);
        Assert.AreEqual(41m, targetHeight);
        Assert.AreEqual(TexturePreviewPhase.Failed, controller.State.Phase);
        Assert.AreEqual("无法读取图片：图片预览数据不是有效 PNG。", controller.State.MetadataText);
        Assert.IsTrue(controller.State.MetadataText.Length <= 120);
        Assert.IsFalse(controller.State.MetadataText.Contains("Traceback", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, controller.State.PhysicalSizeText);
    }

    [TestMethod]
    public void SupersededAndClosedOperations_AreCancelledAndCannotMutateOrRetainPreview()
    {
        var displayed = new List<IDisposable?>();
        var sizeWriteCount = 0;
        var controller = new TexturePreviewController(
            displayed.Add,
            _ => sizeWriteCount++);
        var stale = controller.BeginImport();
        var current = controller.BeginImport();
        var stalePreview = new TrackedPreview();

        Assert.IsTrue(stale.CancellationToken.IsCancellationRequested);
        Assert.IsFalse(controller.TryCompleteImport(
            stale,
            stalePreview,
            new TextureImageInfo(100, 50, 100, 100),
            null,
            0.01m,
            100000m,
            out var staleUpdate));
        Assert.IsTrue(stalePreview.IsDisposed);
        Assert.IsFalse(staleUpdate.ShouldWriteTargets);
        Assert.IsFalse(controller.TryFail(stale, new Exception("stale")));
        Assert.AreEqual(TexturePreviewPhase.Loading, controller.State.Phase);

        controller.Close();
        var closedPreview = new TrackedPreview();

        Assert.IsTrue(current.CancellationToken.IsCancellationRequested);
        Assert.IsFalse(controller.TryCompleteImport(
            current,
            closedPreview,
            new TextureImageInfo(100, 50, 100, 100),
            null,
            0.01m,
            100000m,
            out _));
        Assert.IsTrue(closedPreview.IsDisposed);
        Assert.IsFalse(controller.TryFail(current, new Exception("closed")));
        Assert.AreEqual(0, sizeWriteCount);
        Assert.AreEqual(TexturePreviewPhase.Closed, controller.State.Phase);
        Assert.IsNull(displayed[^1]);
    }

    [TestMethod]
    public void ReplacementAndClose_DisposeEveryOwnedPreviewExactlyOnce()
    {
        var displayed = new List<IDisposable?>();
        var controller = new TexturePreviewController(displayed.Add, _ => { });
        var first = new TrackedPreview();
        var firstRequest = controller.BeginImport();
        Assert.IsTrue(controller.TryCompleteImport(
            firstRequest,
            first,
            new TextureImageInfo(20, 10, 100, 100),
            null,
            0.01m,
            100000m,
            out _));

        var secondRequest = controller.BeginImport();
        Assert.IsTrue(first.IsDisposed);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.IsNull(displayed[^1]);

        var second = new TrackedPreview();
        Assert.IsTrue(controller.TryCompleteImport(
            secondRequest,
            second,
            new TextureImageInfo(40, 20, 100, 100),
            null,
            0.01m,
            100000m,
            out _));
        controller.Close();

        Assert.IsTrue(second.IsDisposed);
        Assert.AreEqual(1, second.DisposeCount);
    }

    [TestMethod]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    public void FallbackParser_RejectsNonFiniteValuesForUiAndPreflight(string text)
    {
        Assert.IsFalse(TextureFallbackDpi.TryParseOptional(text, out var value));
        Assert.IsNull(value);
    }

    private sealed class TrackedPreview : IDisposable
    {
        public int DisposeCount { get; private set; }
        public bool IsDisposed => DisposeCount > 0;

        public void Dispose() => DisposeCount++;
    }
}
