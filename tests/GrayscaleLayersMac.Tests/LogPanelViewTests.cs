using System;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LogPanelViewTests
{
    [TestMethod]
    public void Collapse_CollapsesLogAreaAndShowsLatestLine()
    {
        var log = UiTheme.CreateLogBox();
        log.Text = "第一条日志" + Environment.NewLine + "第二条日志";

        var panel = UiTheme.LogPanel(log, "流程日志");

        Assert.IsFalse(panel.IsCollapsed);
        Assert.AreEqual(UiTheme.LogAreaExpandedHeight, panel.LogAreaHeight);
        Assert.AreEqual(1, panel.LogAreaOpacity);
        Assert.AreEqual(0, panel.ChevronAngle);

        panel.SetCollapsed(true);

        Assert.IsTrue(panel.IsCollapsed);
        Assert.AreEqual(0, panel.LogAreaHeight);
        Assert.AreEqual(0, panel.LogAreaOpacity);
        Assert.IsFalse(panel.LogAreaHitTestVisible);
        Assert.AreEqual("第二条日志", panel.SummaryText);

        panel.SetCollapsed(false);

        Assert.IsFalse(panel.IsCollapsed);
        Assert.AreEqual(UiTheme.LogAreaExpandedHeight, panel.LogAreaHeight);
        Assert.AreEqual(1, panel.LogAreaOpacity);
        Assert.IsTrue(panel.LogAreaHitTestVisible);
    }

    [TestMethod]
    public void Collapse_RotatesHandleChevronAndFadesSummary()
    {
        var log = UiTheme.CreateLogBox();
        log.Text = "正在生成第 3 层…";
        var panel = UiTheme.LogPanel(log, "流程日志");

        // 展开态：箭头朝下（收起方向），最新一条日志不可见。
        Assert.AreEqual(0, panel.ChevronAngle);
        Assert.AreEqual(0, panel.SummaryOpacity);
        StringAssert.Contains(panel.HandleTooltip, "下缩");

        panel.SetCollapsed(true);

        // 折叠态：箭头旋转 180° 朝上（展开方向），最新一条日志淡入。
        Assert.AreEqual(180, panel.ChevronAngle);
        Assert.AreEqual(1, panel.SummaryOpacity);
        StringAssert.Contains(panel.HandleTooltip, "上拉");

        panel.SetCollapsed(false);

        Assert.AreEqual(0, panel.ChevronAngle);
        Assert.AreEqual(0, panel.SummaryOpacity);
    }

    [TestMethod]
    public void Summary_SkipsTrailingBlankLines()
    {
        var log = UiTheme.CreateLogBox();
        log.Text = "步骤 2/3 完成" + Environment.NewLine + Environment.NewLine;

        var panel = UiTheme.LogPanel(log, "流程日志");
        panel.SetCollapsed(true);

        Assert.AreEqual("步骤 2/3 完成", panel.SummaryText);
    }

    [TestMethod]
    public void EmptyLog_ShowsPlaceholder()
    {
        var log = UiTheme.CreateLogBox();
        var panel = UiTheme.LogPanel(log, "流程日志");
        panel.SetCollapsed(true);

        Assert.AreEqual("暂无日志", panel.SummaryText);
    }

    [TestMethod]
    public void CollapsedChanged_FiresOnEachToggleOnly()
    {
        var log = UiTheme.CreateLogBox();
        var panel = UiTheme.LogPanel(log, "流程日志");
        var raised = 0;
        panel.CollapsedChanged += (_, _) => raised++;

        panel.SetCollapsed(true);
        panel.SetCollapsed(true);
        panel.SetCollapsed(false);

        Assert.AreEqual(2, raised);
    }

    [TestMethod]
    public void ClearActionUsesNamedFluentIconButton()
    {
        var panel = UiTheme.LogPanel(UiTheme.CreateLogBox(), "流程日志");

        Assert.IsTrue(panel.ClearButtonUsesFluentIcon);
        Assert.AreEqual("清空日志", panel.ClearButtonName);
    }

    [TestMethod]
    public void LogTextUsesTheCjkCapableUiFont()
    {
        var log = UiTheme.CreateLogBox();

        Assert.AreEqual(UiTheme.UiFont, log.FontFamily);
    }
}
