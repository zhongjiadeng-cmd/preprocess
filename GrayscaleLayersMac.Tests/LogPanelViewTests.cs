using System;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LogPanelViewTests
{
    [TestMethod]
    public void Collapse_HidesLogBoxAndShowsLatestLine()
    {
        var log = UiTheme.CreateLogBox();
        log.Text = "第一条日志" + Environment.NewLine + "第二条日志";

        var panel = UiTheme.LogPanel(log, "流程日志");

        Assert.IsFalse(panel.IsCollapsed);
        Assert.IsTrue(log.IsVisible);

        panel.SetCollapsed(true);

        Assert.IsTrue(panel.IsCollapsed);
        Assert.IsFalse(log.IsVisible);
        Assert.AreEqual("第二条日志", panel.SummaryText);

        panel.SetCollapsed(false);

        Assert.IsFalse(panel.IsCollapsed);
        Assert.IsTrue(log.IsVisible);
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
}
