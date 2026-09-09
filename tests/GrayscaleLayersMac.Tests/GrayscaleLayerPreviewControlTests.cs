using System.Linq;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class GrayscaleLayerPreviewControlTests
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
    public void ViewportAndContextToolsAreSeparatedByPurpose()
    {
        _session!.Dispatch(() =>
        {
            using var preview = new GrayscaleLayerPreviewControl((_, _) =>
                throw new System.InvalidOperationException("测试不会读取文件。"));

            var viewportNames = preview.ViewportTools.GetLogicalDescendants()
                .OfType<Button>()
                .Select(AutomationProperties.GetName)
                .Where(name => name is not null)
                .ToArray();
            var contextNames = preview.ContextTools.GetLogicalDescendants()
                .OfType<Button>()
                .Select(AutomationProperties.GetName)
                .Where(name => name is not null)
                .ToArray();

            CollectionAssert.IsSubsetOf(
                new[] { "缩小", "放大", "适应窗口", "实际尺寸" },
                viewportNames!);
            CollectionAssert.IsSubsetOf(
                new[] { "上一层", "下一层" },
                contextNames!);
            Assert.DoesNotContain("上一层", viewportNames!);
            Assert.DoesNotContain("缩小", contextNames!);
        }, CancellationToken.None);
    }
}
