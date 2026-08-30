using Avalonia.Automation;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;
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
    public void NormalMotionStartsCollapsedAndInstallsSpatialAndOpacityTransitions()
    {
        using var _ = MotionPreferences.OverrideForTesting(false);
        var overlay = new ImportProgressOverlay(new Button());

        Assert.AreEqual(0, overlay.SurfaceHeight);
        Assert.AreEqual(0, overlay.SurfaceOpacity);
        Assert.AreEqual(-8, overlay.SurfaceTranslationY);
        Assert.IsTrue(overlay.HasSpatialTransitions);

        AttachMotion(overlay);

        Assert.AreEqual(2, overlay.InstalledSurfaceTransitionCount);
        Assert.AreEqual(1, overlay.InstalledTranslationTransitionCount);
        AssertTransitions(overlay.SurfaceTransitions!, TimeSpan.FromMilliseconds(280), 2);
        AssertTransitions(overlay.TranslationTransitions!, TimeSpan.FromMilliseconds(280), 1);
    }

    [TestMethod]
    public void ReducedMotionStartsExpandedAndInstallsFadeOnly()
    {
        using var _ = MotionPreferences.OverrideForTesting(true);
        var overlay = new ImportProgressOverlay(new Button());

        Assert.AreEqual(136, overlay.SurfaceHeight);
        Assert.AreEqual(0, overlay.SurfaceOpacity);
        Assert.AreEqual(0, overlay.SurfaceTranslationY);
        Assert.IsFalse(overlay.HasSpatialTransitions);

        AttachMotion(overlay);

        Assert.AreEqual(1, overlay.InstalledSurfaceTransitionCount);
        Assert.AreEqual(0, overlay.InstalledTranslationTransitionCount);
        AssertTransitions(overlay.SurfaceTransitions!, TimeSpan.FromMilliseconds(80), 1);
    }

    [TestMethod]
    public void UpdateRefreshesVisibleStateWithoutReopeningOrReannouncingTheSameStage()
    {
        var overlay = new ImportProgressOverlay(new Button());
        overlay.Show(ImportProgressState.ValidatingTiff(1, 3, "first.tiff"));
        var firstAnnouncement = overlay.LiveRegionText;

        overlay.Update(ImportProgressState.ValidatingTiff(2, 3, "second.tiff"));

        Assert.IsTrue(overlay.IsOpen);
        Assert.AreEqual("正在检查分层 TIFF…", overlay.TitleText);
        Assert.AreEqual("second.tiff", overlay.DetailText);
        Assert.AreEqual("正在检查分层 TIFF · 2/3", overlay.CounterText);
        Assert.AreEqual(firstAnnouncement, overlay.LiveRegionText);

        overlay.Update(ImportProgressState.Succeeded(3));

        Assert.AreEqual(ImportProgressState.Succeeded(3).AutomationText, overlay.LiveRegionText);
        Assert.IsTrue(overlay.IsOpen);

        overlay.Update(ImportProgressState.Succeeded(4));

        Assert.AreEqual(ImportProgressState.Succeeded(4).AutomationText, overlay.LiveRegionText);
    }

    [TestMethod]
    public void LiveRegionRemainsVisibleToAutomationWhileHavingNoVisualFootprint()
    {
        var overlay = new ImportProgressOverlay(new Button());
        overlay.Show(ImportProgressState.Scanning("正在扫描文件…"));

        Assert.IsTrue(overlay.LiveRegionIsVisible);
        Assert.AreEqual(AutomationLiveSetting.Polite, overlay.LiveRegionSetting);
        Assert.AreEqual(ImportProgressState.Scanning("正在扫描文件…").AutomationText, overlay.LiveRegionText);
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
        Assert.AreSame(UiTheme.WarningTextBrush, overlay.TitleForeground);
        Assert.AreSame(UiTheme.WarningBrush, overlay.ProgressForeground);
        Assert.IsTrue(overlay.CloseButtonFocusRequested);

        overlay.Close();

        Assert.IsFalse(overlay.IsOpen);
    }

    private static void AttachMotion(ImportProgressOverlay overlay)
    {
        typeof(ImportProgressOverlay)
            .GetMethod("AttachMotion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(overlay, null);
    }

    private static void AssertTransitions(
        Transitions transitions,
        TimeSpan expectedDuration,
        int expectedCount)
    {
        var animated = transitions.OfType<DoubleTransition>().ToArray();
        Assert.AreEqual(expectedCount, animated.Length);
        Assert.IsTrue(animated.All(transition => transition.Duration == expectedDuration));
        Assert.IsTrue(animated.All(transition => transition.Easing?.GetType().Name == "CubicEaseOut"));
    }
}
