using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace GrayscaleLayersMac.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ImportProgressOverlayTests
{
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

    [TestMethod]
    public async Task SuccessWaitsForTheHoldBeforeItCloses()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlay = new ImportProgressOverlay(new Button(), (_, _) => hold.Task);
        overlay.Show(ImportProgressState.Scanning("正在扫描文件…"));

        var success = overlay.ShowSucceededAndCollapseAsync(ImportProgressState.Succeeded(2));

        Assert.IsTrue(overlay.IsOpen);
        hold.SetResult();
        await success;

        Assert.IsFalse(overlay.IsOpen);
    }

    [TestMethod]
    public async Task ANewShowInterruptsAStaleSuccessClose()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlay = new ImportProgressOverlay(new Button(), (_, _) => hold.Task);
        overlay.Show(ImportProgressState.Scanning("正在扫描文件…"));
        var success = overlay.ShowSucceededAndCollapseAsync(ImportProgressState.Succeeded(2));

        overlay.Show(ImportProgressState.ValidatingTiff(1, 2, "first.tiff"));
        hold.SetResult();
        await success;

        Assert.IsTrue(overlay.IsOpen);
        Assert.AreEqual("正在检查分层 TIFF…", overlay.TitleText);
    }

    [TestMethod]
    public void FailureRemainsOpenUntilClosed()
    {
        var overlay = new ImportProgressOverlay(new Button());

        overlay.ShowFailure(ImportProgressState.Failed("broken.tiff", "无法读取 TIFF"));

        Assert.IsTrue(overlay.IsOpen);
        Assert.IsTrue(overlay.CloseButtonVisible);
        Assert.AreEqual("无法读取 TIFF", overlay.TitleText);

        overlay.Close();

        Assert.IsFalse(overlay.IsOpen);
    }
}
