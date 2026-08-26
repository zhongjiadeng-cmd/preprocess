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
}
