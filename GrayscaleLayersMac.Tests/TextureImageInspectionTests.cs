using System;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class TextureImageInspectionTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [TestMethod]
    public void ParseJson_ReturnsMetadataAndPngBytes()
    {
        var value = TextureImageInspection.ParseJson(
            $$"""{"pixel_width":1500,"pixel_height":1500,"dpi_x":1270,"dpi_y":1270,"preview_png_base64":"{{OnePixelPng}}"}""");
        Assert.AreEqual(1500, value.Info.PixelWidth);
        CollectionAssert.AreEqual(new byte[] {137,80,78,71,13,10,26,10}, value.PreviewPng[..8]);
    }

    [TestMethod]
    public void ParseJson_RejectsInvalidOrOversizedPreview()
    {
        var invalid = """{"pixel_width":1,"pixel_height":1,"dpi_x":null,"dpi_y":null,"preview_png_base64":"bad"}""";
        Assert.ThrowsExactly<ArgumentException>(() => TextureImageInspection.ParseJson(invalid));
        var valid = $$"""{"pixel_width":1,"pixel_height":1,"dpi_x":null,"dpi_y":null,"preview_png_base64":"{{OnePixelPng}}"}""";
        Assert.ThrowsExactly<ArgumentException>(() => TextureImageInspection.ParseJson(valid, 8));
    }

    [TestMethod]
    public void ParseJson_RejectsMaximumPreviewBytesBelowPngSignature()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TextureImageInspection.ParseJson("{}", 7));
    }
}
