using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

/// <summary>
/// 图层侧栏的命中判定与高度计算。行距是私有常量，测试里一律从测量结果反推，
/// 免得调行高时要同步改两处数字。
/// </summary>
[TestClass]
public sealed class DxfLayerRailCanvasTests
{
    [TestMethod]
    public void SetItemsResizesTheRailAndNullIsTreatedAsEmpty()
    {
        var rail = new DxfLayerRailCanvas();

        rail.SetItems(MakeItems(4));
        var expandedPitch = rail.Height / 4;
        Assert.IsTrue(expandedPitch > 0);

        rail.SetCompact(true);
        var compactPitch = rail.Height / 4;
        Assert.IsTrue(compactPitch < expandedPitch,
            "收拢后行距变小，总高随之收缩");

        rail.SetItems(null!);
        Assert.AreEqual(0d, rail.Height, 1e-9);
    }

    [TestMethod]
    public void GetIndexAt_MapsEachRowInExpandedPitch()
    {
        var pitch = Pitch(compact: false);

        Assert.AreEqual(0, DxfLayerRailCanvas.GetIndexAt(0, 4));
        Assert.AreEqual(0, DxfLayerRailCanvas.GetIndexAt(pitch - 0.5, 4));
        Assert.AreEqual(1, DxfLayerRailCanvas.GetIndexAt(pitch, 4));
        Assert.AreEqual(3, DxfLayerRailCanvas.GetIndexAt(pitch * 3 + 10, 4));
    }

    [TestMethod]
    public void GetIndexAt_UsesTheTighterPitchWhenCompact()
    {
        var compact = Pitch(compact: true);
        var expanded = Pitch(compact: false);

        Assert.AreEqual(1, DxfLayerRailCanvas.GetIndexAt(compact, 4, compact: true));
        Assert.AreEqual(
            0,
            DxfLayerRailCanvas.GetIndexAt(compact, 4),
            "展开态行距更大，同一个 y 还停在第 0 层上");
        Assert.IsTrue(compact < expanded);
    }

    [TestMethod]
    public void GetIndexAt_OutOfRangeReturnsMinusOne()
    {
        var pitch = Pitch(compact: false);

        Assert.AreEqual(-1, DxfLayerRailCanvas.GetIndexAt(-1, 4));
        Assert.AreEqual(-1, DxfLayerRailCanvas.GetIndexAt(pitch * 4, 4), "越过最后一行");
        Assert.AreEqual(-1, DxfLayerRailCanvas.GetIndexAt(0, 0), "没有层时任何位置都不命中");
    }

    [TestMethod]
    public void NewRailStartsExpandedWithNothingSelected()
    {
        var rail = new DxfLayerRailCanvas();

        rail.SetItems(MakeItems(2));

        Assert.AreEqual(-1, rail.SelectedIndex);
        Assert.IsFalse(rail.IsCompact, "侧栏默认是展开态");
    }

    /// <summary>借一次 SetItems 的测量结果反推行距。</summary>
    private static double Pitch(bool compact)
    {
        var rail = new DxfLayerRailCanvas();
        rail.SetItems(MakeItems(1));
        var expanded = rail.Height;
        rail.SetCompact(compact);
        return rail.Height > 0 ? rail.Height : expanded;
    }

    private static DxfLayerPreviewItem[] MakeItems(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new DxfLayerPreviewItem(
                $"第 {index + 1:D2} 层",
                $"/tmp/layer_{index:D2}.dxf",
                null,
                null))
            .ToArray();
}
