using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            var header = blocks[0].Text ?? string.Empty;
            var file = blocks[1].Text ?? string.Empty;
            StringAssert.Contains(header, "pmt_0001");
            StringAssert.Contains(header, "第 1 行 / 第 1 列");
            StringAssert.Contains(header, "左上 (2.5, 27.5) mm");
            StringAssert.Contains(header, "层间进给 3 μm");
            Assert.AreEqual("pmt_0001machine.json", file);
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
            var header = blocks[0].Text ?? string.Empty;
            StringAssert.Contains(header, "点击线框");
        }, CancellationToken.None);
    }

    [TestMethod]
    public void ParameterRowsExposeAllConfigurationsInOrder()
    {
        _session!.Dispatch(() =>
        {
            var details = new PmtDetailsEditor();
            details.LoadJob(BuildJob());
            var numbers = details.GetVisualDescendants().OfType<NumericUpDown>().Count();
            var checkBoxes = details.GetVisualDescendants().OfType<CheckBox>().Count();
            var expected = LaserPmtConfiguration.Parameters.Count;
            Assert.AreEqual(expected, numbers + checkBoxes,
                "PmtDetailsEditor 应列出 LaserPmtConfiguration.Parameters 中的每一项参数编辑器。");
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

    private static Button GetButton(PmtDetailsEditor editor, string text)
    {
        return editor.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => (button.Content as string) == text)
            ?? throw new AssertFailedException($"找不到按钮：{text}");
    }

    private static List<TextBlock> GetTextBlocks(PmtDetailsEditor editor)
    {
        return editor.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToList();
    }
}
