using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PmtDetailsEditorTests
{
    private static HeadlessUnitTestSession? _session;

    [ClassInitialize]
    public static void StartHeadlessSession(TestContext _)
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(App));
    }

    [ClassCleanup]
    public static void StopHeadlessSession() => _session?.Dispose();

    [TestMethod]
    public void LoadingNullStateDisablesSaveAndResetButtons()
    {
        _session!.Dispatch(() =>
        {
            var details = new PmtDetailsEditor();
            details.LoadJob(null);
            Assert.IsFalse(GetButton(details, "保存覆盖").IsEnabled);
            Assert.IsFalse(GetButton(details, "还原基础").IsEnabled);
        }, CancellationToken.None);
    }

    [TestMethod]
    public void LoadingJobEnablesButtonsAndPopulatesIdentifierHeader()
    {
        _session!.Dispatch(() =>
        {
            var details = new PmtDetailsEditor();
            details.LoadJob(BuildJob());
            Assert.IsTrue(GetButton(details, "保存覆盖").IsEnabled);
            var blocks = GetTextBlocks(details);
            Assert.IsTrue(blocks.Any(block => block.Text == "pmt_0001"));
            Assert.IsTrue(blocks.Any(block =>
                (block.Text ?? string.Empty).Contains("第 1 行 / 第 1 列") &&
                (block.Text ?? string.Empty).Contains("左上 (2.5, 27.5) mm") &&
                (block.Text ?? string.Empty).Contains("层间进给 3 μm")));
            Assert.IsTrue(blocks.Any(block => block.Text == "pmt_0001machine.json"));
        }, CancellationToken.None);
    }

    [TestMethod]
    public void LoadingNullResetsHeaderTextToHint()
    {
        _session!.Dispatch(() =>
        {
            var details = new PmtDetailsEditor();
            details.LoadJob(BuildJob());
            details.LoadJob(null);
            var blocks = GetTextBlocks(details);
            Assert.IsTrue(blocks.Any(block =>
                (block.Text ?? string.Empty).Contains("点击线框")));
        }, CancellationToken.None);
    }

    [TestMethod]
    public void ParameterRowsExposeAllConfigurationsInOrder()
    {
        _session!.Dispatch(() =>
        {
            var details = new PmtDetailsEditor();
            details.LoadJob(BuildJob());
            var numbers = details.GetVisualDescendants().OfType<TextBox>().Count();
            var checkBoxes = details.GetVisualDescendants().OfType<CheckBox>().Count();
            var expected = LaserPmtConfiguration.Parameters.Count;
            Assert.AreEqual(expected, numbers + checkBoxes,
                "PmtDetailsEditor 应列出 LaserPmtConfiguration.Parameters 中的每一项参数编辑器。");
        }, CancellationToken.None);
    }

    [TestMethod]
    public void UsesCompactThreeSectionInspectorWithIconActions()
    {
        _session!.Dispatch(() =>
        {
            var details = new PmtDetailsEditor();

            Assert.AreEqual(220d, details.Width);
            var root = Assert.IsInstanceOfType<Grid>(details.Content);
            Assert.AreEqual(3, root.RowDefinitions.Count);
            Assert.IsInstanceOfType<ScrollViewer>(root.Children.Single(control => Grid.GetRow(control) == 1));

            var save = GetButton(details, "保存覆盖");
            var reset = GetButton(details, "还原基础");
            Assert.IsTrue(UiIcons.IsFluentIconControl(save.Content));
            Assert.IsTrue(UiIcons.IsFluentIconControl(reset.Content));
        }, CancellationToken.None);
    }

    [TestMethod]
    public void ParameterLabelsHideMachineFieldAliasesButKeepUnits()
    {
        _session!.Dispatch(() =>
        {
            var details = new PmtDetailsEditor();
            var labels = GetTextBlocks(details)
                .Select(block => block.Text ?? string.Empty)
                .ToArray();

            CollectionAssert.Contains(labels, "功率");
            CollectionAssert.Contains(labels, "扫描速度");
            CollectionAssert.Contains(labels, "层间进给（μm）");
            CollectionAssert.Contains(labels, "scanahead");
            CollectionAssert.Contains(labels, "skywritting");
            Assert.IsFalse(labels.Any(label => label.Contains("power", System.StringComparison.Ordinal)));
            Assert.IsFalse(labels.Any(label => label.Contains("scanSpeed", System.StringComparison.Ordinal)));
            Assert.IsFalse(labels.Any(label => label.Contains("预扫描", System.StringComparison.Ordinal)));
            Assert.IsFalse(labels.Any(label => label.Contains("空写", System.StringComparison.Ordinal)));
        }, CancellationToken.None);
    }

    private static LaserPmtJobLayout BuildJob() => new(
        0,
        "pmt_0001",
        0,
        0,
        2.5,
        27.5,
        20,
        10,
        "pmt_0001machine.json",
        3,
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["power"] = "20",
            ["frequency"] = "30",
            ["scan_ahead"] = "true"
        });

    private static Button GetButton(PmtDetailsEditor editor, string automationName)
    {
        return editor.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => AutomationProperties.GetName(button) == automationName)
            ?? throw new AssertFailedException($"找不到按钮：{automationName}");
    }

    private static List<TextBlock> GetTextBlocks(PmtDetailsEditor editor)
    {
        return editor.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToList();
    }
}
