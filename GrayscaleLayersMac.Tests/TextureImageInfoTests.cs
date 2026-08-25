using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class TextureImageInfoTests
{
    [TestMethod]
    public void FormatSummary_ShowsPixelsAxisDpiAndPhysicalSize()
    {
        var info = new TextureImageInfo(600, 300, 300, 150);
        Assert.AreEqual("像素：600 × 300 px\nDPI：300 × 150", info.FormatMetadata());
        Assert.AreEqual("物理尺寸：50.8 × 50.8 mm",
            info.FormatPhysicalSize(50.8m, 50.8m));
    }

    [TestMethod]
    public void FormatSummary_ExplainsMissingDpi()
    {
        var info = new TextureImageInfo(40, 20, null, null);
        Assert.AreEqual("像素：40 × 20 px\nDPI：未提供", info.FormatMetadata());
    }

    [TestMethod]
    public void ParseJsonAndCalculate_UsesAxisDpi()
    {
        var info = TextureImageInfo.ParseJson(
            """{"pixel_width":600,"pixel_height":300,"dpi_x":300,"dpi_y":150}""");
        var ok = info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out var width, out var height, out var error);
        Assert.IsTrue(ok, error);
        Assert.AreEqual(50.8m, width);
        Assert.AreEqual(50.8m, height);
    }

    [TestMethod]
    public void Calculate_MissingDpiNeedsFallback()
    {
        var info = TextureImageInfo.ParseJson(
            """{"pixel_width":100,"pixel_height":50,"dpi_x":null,"dpi_y":null}""");
        Assert.IsFalse(info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out _, out _, out _));
        Assert.IsTrue(info.TryCalculateMillimeters(
            100, 0.01m, 100000m, out var width, out var height, out _));
        Assert.AreEqual(25.4m, width);
        Assert.AreEqual(12.7m, height);
    }

    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void Calculate_RejectsInvalidFallback(double dpi)
    {
        var info = new TextureImageInfo(100, 50, null, null);
        Assert.IsFalse(info.TryCalculateMillimeters(
            dpi, 0.01m, 100000m, out _, out _, out _));
    }

    [TestMethod]
    public void Calculate_RejectsFallbackDpiThatRoundsToZeroAsDecimal()
    {
        var info = new TextureImageInfo(100, 50, null, null);

        var ok = info.TryCalculateMillimeters(
            double.Epsilon, 0.01m, 100000m, out _, out _, out var error);

        Assert.IsFalse(ok);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void Calculate_RejectsEmbeddedDpiThatRoundsToZeroAsDecimal()
    {
        var info = new TextureImageInfo(100, 50, double.Epsilon, double.Epsilon);

        var ok = info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out _, out _, out var error);

        Assert.IsFalse(ok);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void Calculate_RejectsResultOutsideControlRange()
    {
        var info = new TextureImageInfo(1_000_000, 1_000_000, 1, 1);
        Assert.IsFalse(info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out _, out _, out var error));
        StringAssert.Contains(error, "允许范围");
    }
}
