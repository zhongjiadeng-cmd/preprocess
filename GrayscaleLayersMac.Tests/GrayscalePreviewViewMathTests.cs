using System;
using Avalonia;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class GrayscalePreviewViewMathTests
{
    private static readonly Size Image = new(1000, 1000);
    private static readonly Size Viewport = new(400, 300);

    [TestMethod]
    public void ZoomModifierAlwaysZoomsRegardlessOfMode()
    {
        foreach (var mode in Enum.GetValues<GrayscalePreviewWheelMode>())
        {
            var action = GrayscalePreviewViewMath.ResolveWheelAction(
                mode, zoomModifier: true, shiftModifier: false,
                canScrollVertically: true, canScrollHorizontally: true);

            Assert.AreEqual(GrayscalePreviewWheelAction.Zoom, action, $"mode={mode}");
        }
    }

    [TestMethod]
    public void AutoModeScrollsWhileTheAxisStillOverflows()
    {
        var action = GrayscalePreviewViewMath.ResolveWheelAction(
            GrayscalePreviewWheelMode.Auto, false, false,
            canScrollVertically: true, canScrollHorizontally: false);

        Assert.AreEqual(GrayscalePreviewWheelAction.Scroll, action);
    }

    [TestMethod]
    public void AutoModeFallsBackToZoomWhenTheAxisCannotScroll()
    {
        // 画布完全放不下时竖向滚不动，滚轮不该"没反应"。
        var vertical = GrayscalePreviewViewMath.ResolveWheelAction(
            GrayscalePreviewWheelMode.Auto, false, false,
            canScrollVertically: false, canScrollHorizontally: true);

        Assert.AreEqual(GrayscalePreviewWheelAction.Zoom, vertical);
    }

    [TestMethod]
    public void AutoModeShiftSwitchesToTheHorizontalAxis()
    {
        var scrollable = GrayscalePreviewViewMath.ResolveWheelAction(
            GrayscalePreviewWheelMode.Auto, false, true,
            canScrollVertically: false, canScrollHorizontally: true);
        var saturated = GrayscalePreviewViewMath.ResolveWheelAction(
            GrayscalePreviewWheelMode.Auto, false, true,
            canScrollVertically: true, canScrollHorizontally: false);

        Assert.AreEqual(GrayscalePreviewWheelAction.Scroll, scrollable);
        Assert.AreEqual(GrayscalePreviewWheelAction.Zoom, saturated);
    }

    [TestMethod]
    public void ExplicitModesIgnoreScrollability()
    {
        Assert.AreEqual(
            GrayscalePreviewWheelAction.Scroll,
            GrayscalePreviewViewMath.ResolveWheelAction(
                GrayscalePreviewWheelMode.Scroll, false, false, false, false));
        Assert.AreEqual(
            GrayscalePreviewWheelAction.Zoom,
            GrayscalePreviewViewMath.ResolveWheelAction(
                GrayscalePreviewWheelMode.Zoom, false, false, true, true));
    }

    [TestMethod]
    public void ZoomAtKeepsTheAnchoredPixelUnderTheCursor()
    {
        var view = new GrayscalePreviewView(1, 0, 0);
        var anchor = new Point(200, 150);

        var zoomed = GrayscalePreviewViewMath.ZoomAt(view, Image, Viewport, anchor, 2);

        Assert.AreEqual(2, zoomed.Zoom, 1e-9);
        var screenX = GrayscalePreviewViewMath.ScreenFromContent(
            200 * 2, 2000, Viewport.Width, zoomed.OffsetX);
        var screenY = GrayscalePreviewViewMath.ScreenFromContent(
            150 * 2, 2000, Viewport.Height, zoomed.OffsetY);
        Assert.AreEqual(anchor.X, screenX, 1e-6);
        Assert.AreEqual(anchor.Y, screenY, 1e-6);
    }

    [TestMethod]
    public void ZoomAtKeepsCenteredContentCentered()
    {
        var small = new Size(100, 100);
        var viewport = new Size(400, 400);
        var view = GrayscalePreviewView.Identity;

        var zoomed = GrayscalePreviewViewMath.ZoomAt(view, small, viewport, new Point(200, 200), 4);

        // 400 × 400 仍装得下，应保持居中、不产生偏移。
        Assert.AreEqual(4, zoomed.Zoom, 1e-9);
        Assert.AreEqual(0, zoomed.OffsetX, 1e-9);
        Assert.AreEqual(0, zoomed.OffsetY, 1e-9);

        var bigger = GrayscalePreviewViewMath.ZoomAt(view, small, viewport, new Point(200, 200), 8);
        var screenX = GrayscalePreviewViewMath.ScreenFromContent(
            50 * 8, 800, viewport.Width, bigger.OffsetX);
        Assert.AreEqual(200, screenX, 1e-6);
    }

    [TestMethod]
    public void ZoomIsClampedToTheSupportedRange()
    {
        Assert.AreEqual(
            GrayscalePreviewViewMath.MaxZoom,
            GrayscalePreviewViewMath.ZoomAt(GrayscalePreviewView.Identity, Image, Viewport, default, 10_000).Zoom,
            1e-9);
        Assert.AreEqual(
            GrayscalePreviewViewMath.MinZoom,
            GrayscalePreviewViewMath.ZoomAt(GrayscalePreviewView.Identity, Image, Viewport, default, 0.00001).Zoom,
            1e-9);
    }

    [TestMethod]
    public void WheelZoomIsMonotonicAndIgnoresInertiaSpikes()
    {
        var zoom = 1d;
        for (var i = 0; i < 5; i++)
            zoom = GrayscalePreviewViewMath.WheelZoom(zoom, 1);

        Assert.IsTrue(zoom > 1, "向上滚应持续放大");

        // 触控板惯性会甩出很大的 delta，必须被夹住而不是一次跳到极限。
        var spiked = GrayscalePreviewViewMath.WheelZoom(1, 10_000);
        Assert.AreEqual(GrayscalePreviewViewMath.WheelZoom(1, 4), spiked, 1e-9);
        Assert.IsTrue(spiked is > 1 and < 2, $"实际倍率 {spiked}");

        Assert.AreEqual(1, GrayscalePreviewViewMath.WheelZoom(1, 0), 1e-9);
        Assert.AreEqual(1, GrayscalePreviewViewMath.WheelZoom(1, double.NaN), 1e-9);
    }

    [TestMethod]
    public void PanFollowsThePointerAndStopsAtTheEdges()
    {
        var view = new GrayscalePreviewView(1, 300, 300);

        // 指针右移 100 → 内容右移 → 偏移减小。
        var dragged = GrayscalePreviewViewMath.PanBy(view, Image, Viewport, new Vector(100, 40));
        Assert.AreEqual(200, dragged.OffsetX, 1e-9);
        Assert.AreEqual(260, dragged.OffsetY, 1e-9);

        // 已经贴到左上角时不该继续往正方向跑。
        var atEdge = GrayscalePreviewViewMath.PanBy(
            new GrayscalePreviewView(1, 0, 0), Image, Viewport, new Vector(100, 100));
        Assert.AreEqual(0, atEdge.OffsetX, 1e-9);
        Assert.AreEqual(0, atEdge.OffsetY, 1e-9);

        // 右下边界同样被夹住。
        var atFarEdge = GrayscalePreviewViewMath.PanBy(
            new GrayscalePreviewView(1, 600, 700), Image, Viewport, new Vector(-500, -500));
        Assert.AreEqual(600, atFarEdge.OffsetX, 1e-9);
        Assert.AreEqual(700, atFarEdge.OffsetY, 1e-9);
    }

    [TestMethod]
    public void PanningIsANoOpWhenEverythingFits()
    {
        var small = new Size(100, 100);
        var viewport = new Size(400, 400);

        var panned = GrayscalePreviewViewMath.PanBy(
            GrayscalePreviewView.Identity, small, viewport, new Vector(80, 80));

        Assert.AreEqual(0, panned.OffsetX, 1e-9);
        Assert.AreEqual(0, panned.OffsetY, 1e-9);
        Assert.IsFalse(GrayscalePreviewViewMath.CanScroll(100, 400));
    }

    [TestMethod]
    public void FitFitsTheWholeImageInsideTheViewport()
    {
        var fit = GrayscalePreviewViewMath.Fit(Image, Viewport);

        // 受限于较窄的那条边：300 / 1000。
        Assert.AreEqual(0.3, fit.Zoom, 1e-9);
        Assert.AreEqual(0, fit.OffsetX, 1e-9);
        Assert.AreEqual(0, fit.OffsetY, 1e-9);
    }

    [TestMethod]
    public void CenterContentCentersAnOversizedImage()
    {
        var centered = GrayscalePreviewViewMath.CenterContent(
            new GrayscalePreviewView(2, 0, 0), Image, Viewport);

        Assert.AreEqual(800, centered.OffsetX, 1e-9);
        Assert.AreEqual(850, centered.OffsetY, 1e-9);
    }

    [TestMethod]
    public void ScreenAndContentCoordinatesRoundTrip()
    {
        const double content = 1000;
        const double viewport = 400;

        var scrolled = GrayscalePreviewViewMath.ContentFromScreen(120, content, viewport, 300);
        Assert.AreEqual(420, scrolled, 1e-9);
        Assert.AreEqual(
            120,
            GrayscalePreviewViewMath.ScreenFromContent(scrolled, content, viewport, 300),
            1e-9);

        var centered = GrayscalePreviewViewMath.ContentFromScreen(250, 100, viewport, 0);
        Assert.AreEqual(100, centered, 1e-9);
    }

    [TestMethod]
    public void ClampRepairsOutOfRangeViews()
    {
        var repaired = GrayscalePreviewViewMath.Clamp(
            new GrayscalePreviewView(99, -500, 1_000_000), Image, Viewport);

        Assert.AreEqual(GrayscalePreviewViewMath.MaxZoom, repaired.Zoom, 1e-9);
        Assert.AreEqual(0, repaired.OffsetX, 1e-9);
        Assert.AreEqual(1000 * GrayscalePreviewViewMath.MaxZoom - Viewport.Height, repaired.OffsetY, 1e-9);

        // 仍在合法范围内的偏移不该被改动。
        var inside = GrayscalePreviewViewMath.Clamp(
            new GrayscalePreviewView(2, 800, 850), Image, Viewport);
        Assert.AreEqual(800, inside.OffsetX, 1e-9);
        Assert.AreEqual(850, inside.OffsetY, 1e-9);
    }

    [TestMethod]
    public void ScrollableAxesReportOverflowPerAxis()
    {
        var (horizontal, vertical) = GrayscalePreviewViewMath.ScrollableAxes(
            new GrayscalePreviewView(1, 0, 0), Image, Viewport);

        Assert.IsTrue(horizontal);
        Assert.IsTrue(vertical);

        var fitted = GrayscalePreviewViewMath.ScrollableAxes(
            GrayscalePreviewViewMath.Fit(Image, Viewport), Image, Viewport);

        Assert.IsFalse(fitted.Horizontal);
        Assert.IsFalse(fitted.Vertical);
    }

    [TestMethod]
    public void DegenerateImagesDoNotProduceNaN()
    {
        var view = GrayscalePreviewViewMath.ZoomAt(
            GrayscalePreviewView.Identity, default, Viewport, new Point(10, 10), 4);

        Assert.AreEqual(4, view.Zoom, 1e-9);
        Assert.AreEqual(0, view.OffsetX, 1e-9);
        Assert.AreEqual(0, view.OffsetY, 1e-9);
        Assert.AreEqual(1, GrayscalePreviewViewMath.FitZoom(default, Viewport), 1e-9);
    }
}
