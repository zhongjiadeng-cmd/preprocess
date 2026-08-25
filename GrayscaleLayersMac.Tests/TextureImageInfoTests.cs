using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class TextureImageInfoTests
{
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
    public void Calculate_RejectsResultOutsideControlRange()
    {
        var info = new TextureImageInfo(1_000_000, 1_000_000, 1, 1);
        Assert.IsFalse(info.TryCalculateMillimeters(
            null, 0.01m, 100000m, out _, out _, out var error));
        StringAssert.Contains(error, "允许范围");
    }
}
