using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class ImportProgressStateTests
{
    [TestMethod]
    public void ScanningIsIndeterminateAndHasNoCounter()
    {
        var state = ImportProgressState.Scanning("正在扫描文件…");
        Assert.IsTrue(state.IsIndeterminate);
        Assert.IsNull(state.ProgressValue);
        Assert.AreEqual(string.Empty, state.CounterText);
    }

    [TestMethod]
    public void ValidationFormatsMonotonicCountAndAccessibleText()
    {
        var state = ImportProgressState.ValidatingTiff(4, 10, "/tmp/layer_04.tiff");
        Assert.AreEqual(0.4, state.ProgressValue);
        Assert.AreEqual("正在检查分层 TIFF · 4/10", state.CounterText);
        StringAssert.Contains(state.AutomationText, "layer_04.tiff");
    }

    [TestMethod]
    public void FailureAndSuccessAreTerminalButOnlyFailureIsError()
    {
        Assert.IsTrue(ImportProgressState.Succeeded(10).IsTerminal);
        Assert.IsFalse(ImportProgressState.Succeeded(10).IsError);
        Assert.IsTrue(ImportProgressState.Failed("坏文件", "无法读取").IsError);
    }
}
