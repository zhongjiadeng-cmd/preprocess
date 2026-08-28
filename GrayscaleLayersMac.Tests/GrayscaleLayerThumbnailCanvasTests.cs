using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class GrayscaleLayerThumbnailCanvasTests
{
    [TestMethod]
    public void GetIndexAtMapsRowsAndRejectsOutsideBounds()
    {
        Assert.AreEqual(0, GrayscaleLayerThumbnailCanvas.GetIndexAt(0, 3));
        Assert.AreEqual(1, GrayscaleLayerThumbnailCanvas.GetIndexAt(112, 3));
        Assert.AreEqual(2, GrayscaleLayerThumbnailCanvas.GetIndexAt(335, 3));
        Assert.AreEqual(-1, GrayscaleLayerThumbnailCanvas.GetIndexAt(-1, 3));
        Assert.AreEqual(-1, GrayscaleLayerThumbnailCanvas.GetIndexAt(336, 3));
        Assert.AreEqual(0, GrayscaleLayerThumbnailCanvas.GetIndexAt(0, 3, compact: true));
        Assert.AreEqual(1, GrayscaleLayerThumbnailCanvas.GetIndexAt(71, 3, compact: true));
        Assert.AreEqual(2, GrayscaleLayerThumbnailCanvas.GetIndexAt(72, 3, compact: true));
        Assert.AreEqual(-1, GrayscaleLayerThumbnailCanvas.GetIndexAt(108, 3, compact: true));
    }
}
