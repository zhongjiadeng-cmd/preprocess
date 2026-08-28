using Avalonia;
using System;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PlanarTextureProjectionTests
{
    [TestMethod]
    public void ModelCornersUseHigherModelYForRasterTop()
    {
        var corners = ProjectedTextureQuad.ModelCorners(new Rect(-10, -20, 30, 50));

        Assert.AreEqual(new Point(-10, 30), corners.RasterTopLeft);
        Assert.AreEqual(new Point(20, 30), corners.RasterTopRight);
        Assert.AreEqual(new Point(20, -20), corners.RasterBottomRight);
        Assert.AreEqual(new Point(-10, -20), corners.RasterBottomLeft);
    }

    [TestMethod]
    public void ImageTransformMapsAllRasterCornersToProjectedQuad()
    {
        var quad = new ProjectedTextureQuad(
            new Point(20, 30), new Point(220, 70),
            new Point(160, 170), new Point(-40, 130));

        var transform = quad.CreateImageToScreenTransform(new Size(100, 50));

        AssertPoint(new Point(20, 30), transform.Transform(new Point(0, 0)));
        AssertPoint(new Point(220, 70), transform.Transform(new Point(100, 0)));
        AssertPoint(new Point(160, 170), transform.Transform(new Point(100, 50)));
        AssertPoint(new Point(-40, 130), transform.Transform(new Point(0, 50)));
    }

    [TestMethod]
    public void ImageTransformRejectsNonPositivePixelDimensions()
    {
        var quad = new ProjectedTextureQuad(
            new Point(0, 0), new Point(1, 0),
            new Point(1, 1), new Point(0, 1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            quad.CreateImageToScreenTransform(new Size(0, 10)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            quad.CreateImageToScreenTransform(new Size(10, 0)));
    }

    [TestMethod]
    public void ImageTransformRejectsQuadWhoseFourthCornerBreaksAffinePlane()
    {
        var quad = new ProjectedTextureQuad(
            new Point(20, 30), new Point(220, 70),
            new Point(165, 170), new Point(-40, 130));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            quad.CreateImageToScreenTransform(new Size(100, 50)));
    }

    [TestMethod]
    public void ImageTransformRejectsNonFiniteProjectedPoint()
    {
        var quad = new ProjectedTextureQuad(
            new Point(double.NaN, 0), new Point(1, 0),
            new Point(1, 1), new Point(0, 1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            quad.CreateImageToScreenTransform(new Size(10, 10)));
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-9);
        Assert.AreEqual(expected.Y, actual.Y, 1e-9);
    }
}
