using System;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class DxfLayerPreviewItemTests
{
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

    [TestMethod]
    public void NonIntegralRasterKeepsHatchPixelScaleAndTopLeftRegistration()
    {
        var registration = new DxfTextureRegistration(
            frameWidthMm: 2.5,
            frameHeightMm: 2.5,
            pixelWidthMm: 1,
            pixelHeightMm: 1,
            pixelColumns: 2,
            pixelRows: 2);
        var item = new DxfLayerPreviewItem(
            "第 01 层", "layer.dxf", "layer.preview.png", registration);

        Assert.AreEqual(-1.25, item.TextureRegistration!.RasterLeftMm, 1e-12);
        Assert.AreEqual(1.25, item.TextureRegistration.RasterTopMm, 1e-12);
        Assert.AreEqual(0.75, item.TextureRegistration.RasterRightMm, 1e-12);
        Assert.AreEqual(-0.75, item.TextureRegistration.RasterBottomMm, 1e-12);
        Assert.AreEqual(-0.25, item.TextureRegistration.RasterLeftMm +
            item.TextureRegistration.PixelWidthMm, 1e-12);
        Assert.AreEqual(2.5, item.WidthMm, 1e-12);
        Assert.AreEqual(2.5, item.HeightMm, 1e-12);
    }

    [TestMethod]
    public void ParsesMachineReadableRegistrationEmittedByPython()
    {
        const string line = "PREVIEW_REGISTRATION_JSON:{\"version\":1," +
            "\"target_width_mm\":2.5,\"target_height_mm\":1," +
            "\"pixel_width_mm\":1,\"pixel_height_mm\":0.5," +
            "\"pixel_columns\":2,\"pixel_rows\":2}";

        Assert.IsTrue(DxfTextureRegistration.TryParseProcessOutput(line, out var value));
        Assert.IsNotNull(value);
        Assert.AreEqual(2.5, value.FrameWidthMm, 1e-12);
        Assert.AreEqual(-0.5, value.RasterBottomMm, 1e-12);
        Assert.IsFalse(DxfTextureRegistration.TryParseProcessOutput("普通日志", out _));
    }

    [TestMethod]
    public void MalformedMachineReadableRegistrationIsRejectedWithoutThrowing()
    {
        const string oversizedVersion =
            "PREVIEW_REGISTRATION_JSON:{\"version\":9223372036854775807}";

        Assert.IsFalse(DxfTextureRegistration.TryParseProcessOutput(
            oversizedVersion,
            out var value));
        Assert.IsNull(value);
    }
}
