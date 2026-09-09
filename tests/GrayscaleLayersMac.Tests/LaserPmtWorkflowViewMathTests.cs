using Avalonia;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowViewMathTests
{
    [TestMethod]
    public void WorldAndScreenTransformsRoundTrip()
    {
        var viewport = new LaserPmtCanvasViewport(2.5, 18, -7);
        var size = new Size(800, 600);
        var world = new Point(-42.5, 18.25);

        var screen = LaserPmtWorkflowViewMath.WorldToScreen(world, viewport, size);
        var restored = LaserPmtWorkflowViewMath.ScreenToWorld(screen, viewport, size);

        Assert.AreEqual(world.X, restored.X, 1e-9);
        Assert.AreEqual(world.Y, restored.Y, 1e-9);
    }

    [TestMethod]
    public void ZoomAtKeepsWorldAnchorFixed()
    {
        var size = new Size(900, 500);
        var anchor = new Point(227, 314);
        var before = new LaserPmtCanvasViewport(1.2, 35, -20);
        var world = LaserPmtWorkflowViewMath.ScreenToWorld(anchor, before, size);

        var after = LaserPmtWorkflowViewMath.ZoomAt(before, anchor, size, 3.4);

        var restored = LaserPmtWorkflowViewMath.WorldToScreen(world, after, size);
        Assert.AreEqual(anchor.X, restored.X, 1e-9);
        Assert.AreEqual(anchor.Y, restored.Y, 1e-9);
    }

    [TestMethod]
    public void FitBoundsCentersContentWithPadding()
    {
        var content = new Rect(-50, -20, 200, 100);
        var size = new Size(800, 500);

        var viewport = LaserPmtWorkflowViewMath.FitBounds(content, size, 40);
        var screen = LaserPmtWorkflowViewMath.WorldRectToScreen(content, viewport, size);

        Assert.IsTrue(screen.Left >= 39.999);
        Assert.IsTrue(screen.Right <= size.Width - 39.999);
        Assert.IsTrue(screen.Top >= 39.999);
        Assert.IsTrue(screen.Bottom <= size.Height - 39.999);
        Assert.AreEqual(size.Width / 2, screen.Center.X, 1e-9);
        Assert.AreEqual(size.Height / 2, screen.Center.Y, 1e-9);
    }

    [TestMethod]
    public void ClampZoomUsesStableLimits()
    {
        Assert.AreEqual(LaserPmtWorkflowViewMath.MinimumZoom,
            LaserPmtWorkflowViewMath.ClampZoom(0));
        Assert.AreEqual(LaserPmtWorkflowViewMath.MaximumZoom,
            LaserPmtWorkflowViewMath.ClampZoom(1000));
    }
}
