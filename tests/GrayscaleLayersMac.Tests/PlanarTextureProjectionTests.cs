using Avalonia;
using System;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PlanarTextureProjectionTests
{
    [TestMethod]
    public void TopViewDrawPlanUsesModelCenterAndMapsAllRasterCorners()
    {
        var projection = new PlanarOverlayProjection(
            new Point(10, -5),
            0,
            0,
            0,
            2,
            new Point(300, 200));

        var plan = CreateDrawPlan(
            projection,
            new Rect(-10, -20, 30, 50),
            new Rect(-15, -25, 40, 60),
            new Size(300, 500));

        AssertQuad(
            plan.TextureQuad,
            new Point(260, 130),
            new Point(320, 130),
            new Point(320, 230),
            new Point(260, 230));
        AssertQuad(
            plan.FrameQuad,
            new Point(250, 120),
            new Point(330, 120),
            new Point(330, 240),
            new Point(250, 240));
        AssertImageTransformMapsQuad(plan, new Size(300, 500));
    }

    [TestMethod]
    public void ExistingIsometricViewProjectsTextureAndFrameAsFourCornerQuads()
    {
        var projection = new PlanarOverlayProjection(
            new Point(0, 0),
            0,
            -35 * Math.PI / 180,
            55 * Math.PI / 180,
            4,
            new Point(300, 200));

        var plan = CreateDrawPlan(
            projection,
            new Rect(-10, -5, 30, 20),
            new Rect(-12, -7, 34, 24),
            new Size(300, 200));

        AssertQuad(
            plan.TextureQuad,
            new Point(301.648504409503, 158.649624242936),
            new Point(399.946749724182, 198.128415643396),
            new Point(354.060634816098, 235.716120474832),
            new Point(255.762389501419, 196.237329074372));
        AssertQuad(
            plan.FrameQuad,
            new Point(299.683899546000, 152.258934333095),
            new Point(411.088577569302, 197.001564586950),
            new Point(356.025239679602, 242.106810384673),
            new Point(244.620561656299, 197.364180130819));
        Assert.IsTrue(Math.Abs(
            plan.TextureQuad.RasterTopLeft.Y -
            plan.TextureQuad.RasterTopRight.Y) > 1);
        Assert.IsTrue(Math.Abs(
            plan.FrameQuad.RasterTopLeft.X -
            plan.FrameQuad.RasterBottomLeft.X) > 1);
        AssertImageTransformMapsQuad(plan, new Size(300, 200));
    }

    [TestMethod]
    public void ArbitraryViewUsesTheSameLiteralProjectionForDxfAndTexture()
    {
        var projection = new PlanarOverlayProjection(
            new Point(5, -2),
            0,
            30 * Math.PI / 180,
            60 * Math.PI / 180,
            3,
            new Point(120, 80));

        var plan = CreateDrawPlan(
            projection,
            new Rect(-1, -4, 8, 10),
            new Rect(-2, -5, 10, 12),
            new Size(80, 100));

        AssertQuad(
            plan.TextureQuad,
            new Point(92.4115427318801, 74.1076951545867),
            new Point(113.196152422707, 68.1076951545867),
            new Point(128.196152422707, 81.0980762113533),
            new Point(107.411542731880, 87.0980762113533));
        AssertQuad(
            plan.FrameQuad,
            new Point(88.3134665205268, 73.5586570489101),
            new Point(114.294228634060, 66.0586570489101),
            new Point(132.294228634060, 81.6471143170300),
            new Point(106.313466520527, 89.1471143170300));
        AssertPoint(
            new Point(128.196152422707, 81.0980762113533),
            projection.ToScreen(7, -4, 0));
        AssertImageTransformMapsQuad(plan, new Size(80, 100));
    }

    [TestMethod]
    public void ChangedScaleAndScreenCenterComposeWithTheSameModelProjection()
    {
        var projection = new PlanarOverlayProjection(
            new Point(5, -2),
            0,
            30 * Math.PI / 180,
            60 * Math.PI / 180,
            1.5,
            new Point(300, 50));

        var plan = CreateDrawPlan(
            projection,
            new Rect(-1, -4, 8, 10),
            new Rect(-2, -5, 10, 12),
            new Size(80, 100));

        AssertQuad(
            plan.TextureQuad,
            new Point(286.205771365940, 47.0538475772934),
            new Point(296.598076211353, 44.0538475772934),
            new Point(304.098076211353, 50.5490381056767),
            new Point(293.705771365940, 53.5490381056767));
        AssertQuad(
            plan.FrameQuad,
            new Point(284.156733260263, 46.7793285244550),
            new Point(297.147114317030, 43.0293285244550),
            new Point(306.147114317030, 50.8235571585150),
            new Point(293.156733260263, 54.5735571585150));
        AssertPoint(
            new Point(304.098076211353, 50.5490381056767),
            projection.ToScreen(7, -4, 0));
        AssertImageTransformMapsQuad(plan, new Size(80, 100));
    }

    [TestMethod]
    public void EdgeOnProjectionRemainsFiniteAndKeepsIntentionalCollapse()
    {
        var projection = new PlanarOverlayProjection(
            new Point(0, 0),
            0,
            0,
            Math.PI / 2,
            10,
            new Point(50, 50));

        var plan = CreateDrawPlan(
            projection,
            new Rect(-2, -3, 4, 6),
            new Rect(-3, -4, 6, 8),
            new Size(40, 60));

        AssertQuad(
            plan.TextureQuad,
            new Point(30, 50),
            new Point(70, 50),
            new Point(70, 50),
            new Point(30, 50));
        AssertQuad(
            plan.FrameQuad,
            new Point(20, 50),
            new Point(80, 50),
            new Point(80, 50),
            new Point(20, 50));
        Assert.IsTrue(plan.TextureQuad.IsFinite);
        Assert.IsTrue(plan.FrameQuad.IsFinite);
        AssertImageTransformMapsQuad(plan, new Size(40, 60));
    }

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

    [TestMethod]
    public void ImageTransformTryRejectsFiniteCornersWhenAcrossOverflows()
    {
        var quad = new ProjectedTextureQuad(
            new Point(-double.MaxValue, 0),
            new Point(double.MaxValue, 0),
            new Point(double.MaxValue, 1),
            new Point(-double.MaxValue, 1));

        Assert.IsTrue(quad.IsFinite);
        Assert.IsFalse(quad.TryCreateImageToScreenTransform(
            new Size(1, 1),
            out _));
    }

    [TestMethod]
    public void ImageTransformTryUsesScaleAwareParallelogramTolerance()
    {
        var quad = new ProjectedTextureQuad(
            new Point(1e12, 0),
            new Point(1e12 + 3, 0),
            new Point(-1e90, 3),
            new Point(-1e90, 3));

        Assert.IsTrue(quad.TryCreateImageToScreenTransform(
            new Size(3, 3),
            out var transform));
        Assert.IsTrue(double.IsFinite(transform.M11));
        Assert.IsTrue(double.IsFinite(transform.M12));
        Assert.IsTrue(double.IsFinite(transform.M21));
        Assert.IsTrue(double.IsFinite(transform.M22));
        Assert.IsTrue(double.IsFinite(transform.M31));
        Assert.IsTrue(double.IsFinite(transform.M32));
    }

    [TestMethod]
    public void ImageTransformTryAllowsFiniteSingularEdgeOnCollapse()
    {
        var quad = new ProjectedTextureQuad(
            new Point(30, 50),
            new Point(70, 50),
            new Point(70, 50),
            new Point(30, 50));

        Assert.IsTrue(quad.TryCreateImageToScreenTransform(
            new Size(40, 60),
            out var transform));
        AssertPoint(new Point(30, 50), transform.Transform(new Point(0, 60)));
        AssertPoint(new Point(70, 50), transform.Transform(new Point(40, 60)));
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-9);
        Assert.AreEqual(expected.Y, actual.Y, 1e-9);
    }

    private static PlanarTextureDrawPlan CreateDrawPlan(
        PlanarOverlayProjection projection,
        Rect textureBounds,
        Rect frameBounds,
        Size pixelSize)
    {
        Assert.IsTrue(projection.TryCreateTextureDrawPlan(
            textureBounds,
            frameBounds,
            pixelSize,
            out var plan));
        return plan;
    }

    private static void AssertQuad(
        ProjectedTextureQuad actual,
        Point rasterTopLeft,
        Point rasterTopRight,
        Point rasterBottomRight,
        Point rasterBottomLeft)
    {
        AssertPoint(rasterTopLeft, actual.RasterTopLeft);
        AssertPoint(rasterTopRight, actual.RasterTopRight);
        AssertPoint(rasterBottomRight, actual.RasterBottomRight);
        AssertPoint(rasterBottomLeft, actual.RasterBottomLeft);
    }

    private static void AssertImageTransformMapsQuad(
        PlanarTextureDrawPlan plan,
        Size pixelSize)
    {
        AssertPoint(
            plan.TextureQuad.RasterTopLeft,
            plan.ImageToScreenTransform.Transform(new Point(0, 0)));
        AssertPoint(
            plan.TextureQuad.RasterTopRight,
            plan.ImageToScreenTransform.Transform(new Point(pixelSize.Width, 0)));
        AssertPoint(
            plan.TextureQuad.RasterBottomRight,
            plan.ImageToScreenTransform.Transform(
                new Point(pixelSize.Width, pixelSize.Height)));
        AssertPoint(
            plan.TextureQuad.RasterBottomLeft,
            plan.ImageToScreenTransform.Transform(new Point(0, pixelSize.Height)));
    }
}
