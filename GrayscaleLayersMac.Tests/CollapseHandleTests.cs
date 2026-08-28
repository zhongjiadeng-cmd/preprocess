using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class CollapseHandleTests
{
    [TestMethod]
    public void HorizontalHandle_展开态箭头朝下_折叠后翻到朝上()
    {
        var handle = new CollapseHandle(CollapseHandleOrientation.Horizontal, "下缩", "上拉");

        Assert.AreEqual(0d, handle.ChevronAngle, "展开时朝下（下缩）");
        Assert.AreEqual("下缩", handle.TooltipText);

        handle.SetCollapsed(true);

        Assert.AreEqual(180d, handle.ChevronAngle, "折叠时朝上（上拉）");
        Assert.AreEqual("上拉", handle.TooltipText);
    }

    [TestMethod]
    public void VerticalHandle_展开态箭头朝左_折叠后翻到朝右()
    {
        var handle = new CollapseHandle(CollapseHandleOrientation.Vertical, "收起", "展开");

        Assert.AreEqual(90d, handle.ChevronAngle, "展开时朝左（收起）");

        handle.SetCollapsed(true);

        Assert.AreEqual(270d, handle.ChevronAngle, "折叠时朝右（展开）");
        Assert.AreEqual("展开", handle.TooltipText);
    }

    [TestMethod]
    public void 纵向把手胶囊是竖长条_横向是横长条()
    {
        var vertical = new CollapseHandle(CollapseHandleOrientation.Vertical, "a", "b");
        Assert.AreEqual(20d, vertical.Width);
        Assert.AreEqual(56d, vertical.Height);

        var horizontal = new CollapseHandle(CollapseHandleOrientation.Horizontal, "a", "b");
        Assert.AreEqual(56d, horizontal.Width);
        Assert.AreEqual(20d, horizontal.Height);
    }

    [TestMethod]
    public void 状态切换才触发Toggled_重复设同一状态不触发()
    {
        var handle = new CollapseHandle(CollapseHandleOrientation.Vertical, "a", "b");
        var toggles = 0;
        handle.Toggled += (_, _) => toggles++;

        handle.SetCollapsed(true);
        handle.SetCollapsed(true);
        handle.SetCollapsed(false);

        Assert.AreEqual(2, toggles, "只有两次真实切换应触发回调");
    }

    [TestMethod]
    public void 把手带上panelhandle类名_复用统一的胶囊样式()
    {
        var handle = new CollapseHandle(CollapseHandleOrientation.Vertical, "a", "b");

        Assert.IsTrue(handle.Classes.Contains("panel-handle"));
    }
}
