using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PipelineProgressStateTests
{
    [TestMethod]
    public void StartingAndNonCountedStepsUseIndeterminateProgress()
    {
        var starting = PipelineProgressState.Starting(allSteps: true);
        var grayscale = PipelineProgressState.Step(
            PipelineProgressStage.Grayscale,
            "正在执行第 1 步：灰度分层…",
            "步骤 1/4");

        Assert.IsTrue(starting.IsIndeterminate);
        Assert.IsTrue(grayscale.IsIndeterminate);
        Assert.AreEqual("步骤 1/4", grayscale.CounterText);
    }

    [TestMethod]
    public void DxfLayerReportsFileAndDeterminateProgress()
    {
        var state = PipelineProgressState.DxfLayer(
            4, 10, "/tmp/layer_04.tiff", "步骤 2/4");

        Assert.IsFalse(state.IsIndeterminate);
        Assert.AreEqual(0.4, state.ProgressValue);
        Assert.AreEqual("步骤 2/4 · 4/10", state.CounterText);
        StringAssert.Contains(state.AutomationText, "layer_04.tiff");
    }

    [TestMethod]
    public void PipelineTerminalStatesExposeDistinctOutcomes()
    {
        Assert.IsTrue(PipelineProgressState.Succeeded("完成").IsSuccess);
        Assert.IsTrue(PipelineProgressState.Cancelled().IsCancelled);
        Assert.IsTrue(PipelineProgressState.Failed(null, "失败").IsError);
        Assert.IsTrue(PipelineProgressState.Cancelled().IsTerminal);
    }
}
