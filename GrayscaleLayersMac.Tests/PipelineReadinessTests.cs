using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PipelineReadinessTests
{
    [TestMethod]
    public void Describe_AllMissingListsEveryRequirement()
    {
        Assert.AreEqual(
            "尚需设置：原始灰度图、分层 TIFF 目录、DXF 目录。",
            PipelineReadiness.Describe(false, null, null, null));
    }

    [TestMethod]
    public void Describe_PartialInputListsOnlyMissingRequirements()
    {
        Assert.AreEqual(
            "尚需设置：DXF 目录。",
            PipelineReadiness.Describe(false, "input.png", "layers", " "));
    }

    [TestMethod]
    public void Describe_CompleteInputIsReady()
    {
        Assert.AreEqual(
            "已准备：可以执行全部四步流程。",
            PipelineReadiness.Describe(false, "input.png", "layers", "dxf"));
    }

    [TestMethod]
    public void Describe_RunningTakesPriority()
    {
        Assert.AreEqual(
            "正在执行流程；可以继续查看预览与日志。",
            PipelineReadiness.Describe(true, null, null, null));
    }
}
