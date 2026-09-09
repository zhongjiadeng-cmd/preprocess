using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtAlignmentSnapTests
{
    [TestMethod]
    public void SnapsEdgesIndependentlyAndReturnsGuideCoordinates()
    {
        var result = LaserPmtAlignmentSnap.Apply(
            new(19.4, 9.6, 10, 10),
            [new LaserPmtWorkflowBounds(30, 10, 10, 10)],
            new(0, 0, 100, 80),
            1);

        Assert.AreEqual(20d, result.Bounds.Left, 1e-9);
        Assert.AreEqual(10d, result.Bounds.Top, 1e-9);
        Assert.AreEqual(10d, result.Bounds.Width, 1e-9);
        Assert.AreEqual(10d, result.Bounds.Height, 1e-9);
        Assert.AreEqual(30d, result.VerticalGuide);
        Assert.IsTrue(result.HorizontalGuide is 10d or 20d);
    }

    [TestMethod]
    public void SnapsCenterToWorkpieceCenter()
    {
        var result = LaserPmtAlignmentSnap.Apply(
            new(44.5, 30, 10, 10),
            [],
            new(0, 0, 100, 80),
            1);

        Assert.AreEqual(45d, result.Bounds.Left);
        Assert.AreEqual(50d, result.VerticalGuide);
    }

    [TestMethod]
    public void LeavesPositionUnchangedOutsideTolerance()
    {
        var moving = new LaserPmtWorkflowBounds(12, 13, 10, 10);
        var result = LaserPmtAlignmentSnap.Apply(
            moving, [new LaserPmtWorkflowBounds(40, 40, 10, 10)],
            new(0, 0, 100, 80), 1);

        Assert.AreEqual(moving, result.Bounds);
        Assert.IsNull(result.VerticalGuide);
        Assert.IsNull(result.HorizontalGuide);
    }
}
