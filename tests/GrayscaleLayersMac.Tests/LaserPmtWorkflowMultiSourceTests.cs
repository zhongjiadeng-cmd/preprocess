using System;
using System.Collections.Generic;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowMultiSourceTests
{
    [TestMethod]
    public void Compile_ResolvesSourceThenBatchThenDirectOverride()
    {
        var workflow = CreateWorkflow();

        var result = LaserPmtWorkflowCompiler.Compile(workflow);

        Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors.Select(error => error.Message)));
        Assert.AreEqual(30, result.Targets[0].Parameters["power"]);
        Assert.AreEqual(100, result.Targets[0].Parameters["frequency"]);
        Assert.AreEqual(40, result.Targets[1].Parameters["power"]);
        Assert.AreEqual(200, result.Targets[1].Parameters["frequency"]);
        Assert.AreEqual("source-a", result.Targets[0].SourceId);
        Assert.AreEqual("source-b", result.Targets[1].SourceId);
    }

    [TestMethod]
    public void AssignSource_PreservesCenterAndAppliesNativeSizeWhenLocked()
    {
        var workflow = CreateWorkflow();
        var before = (LaserPmtTarget)workflow.Targets[0];
        var centerX = before.Bounds.Left + before.Bounds.Width / 2;
        var centerY = before.Bounds.Top + before.Bounds.Height / 2;

        var updated = LaserPmtWorkflowEditor.AssignPmtSource(
            workflow, [before.Id], "source-b");
        var after = (LaserPmtTarget)updated.Targets[0];

        Assert.AreEqual("source-b", after.SourceId);
        Assert.AreEqual(30d, after.Bounds.Width);
        Assert.AreEqual(15d, after.Bounds.Height);
        Assert.AreEqual(centerX, after.Bounds.Left + after.Bounds.Width / 2, 1e-9);
        Assert.AreEqual(centerY, after.Bounds.Top + after.Bounds.Height / 2, 1e-9);
    }

    [TestMethod]
    public void SizeLock_RequiresUnlockAndExposesIndependentScale()
    {
        var workflow = CreateWorkflow();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LaserPmtWorkflowEditor.ResizePmt(workflow, "pmt-a", 12, 8));

        var unlocked = LaserPmtWorkflowEditor.SetPmtSizeLock(
            workflow, "pmt-a", locked: false);
        var resized = LaserPmtWorkflowEditor.ResizePmt(unlocked, "pmt-a", 12, 8);
        var result = LaserPmtWorkflowCompiler.Compile(resized);

        Assert.AreEqual(1.2d, result.Targets[0].ScaleX, 1e-9);
        Assert.AreEqual(1.6d, result.Targets[0].ScaleY, 1e-9);
    }

    [TestMethod]
    public void BaseParameterEditsTargetOnlyTheSelectedSourceNode()
    {
        var workflow = CreateWorkflow();

        var valued = LaserPmtWorkflowEditor.SetBaseParameterValue(
            workflow, "base-b", "power", "77");
        var disabled = LaserPmtWorkflowEditor.SetBaseParameterEnabled(
            valued, "base-b", "frequency", false);

        Assert.AreEqual("10", disabled.BaseNodes.Single(node => node.Id == "base-a").Parameters["power"]);
        Assert.AreEqual("77", disabled.BaseNodes.Single(node => node.Id == "base-b").Parameters["power"]);
        Assert.IsFalse(disabled.BaseNodes.Single(node => node.Id == "base-a")
            .RemovedParameters.Contains("frequency"));
        Assert.IsTrue(disabled.BaseNodes.Single(node => node.Id == "base-b")
            .RemovedParameters.Contains("frequency"));
    }

    private static LaserPmtWorkflow CreateWorkflow()
    {
        var sourceAValues = Values(power: 10, frequency: 100);
        var sourceBValues = Values(power: 20, frequency: 200);
        var sources = new[]
        {
            new LaserPmtWorkflowSource(
                "source-a", "machine-a", "A source", "A", 0xFF0EA5E9, 10, 5, "hash-a"),
            new LaserPmtWorkflowSource(
                "source-b", "machine-b", "B source", "B", 0xFFF97316, 30, 15, "hash-b")
        };
        var baseNodes = new[]
        {
            new LaserPmtBaseParameterNode(
                "base-a", new(-200, 0), sourceAValues, new HashSet<string>(StringComparer.Ordinal))
                { SourceId = "source-a" },
            new LaserPmtBaseParameterNode(
                "base-b", new(-200, 100), sourceBValues, new HashSet<string>(StringComparer.Ordinal))
                { SourceId = "source-b" }
        };
        var powerNode = new LaserPmtSingleParameterNode(
            "power-node", new(-80, 0), "power", "30", [new("power-30", "30")]);
        var pmtA = new LaserPmtTarget("pmt-a", 1, new(10, 10, 10, 5), false)
        {
            SourceId = "source-a",
            NativeWidth = 10,
            NativeHeight = 5
        };
        var pmtB = new LaserPmtTarget("pmt-b", 2, new(50, 10, 30, 15), false)
        {
            SourceId = "source-b",
            NativeWidth = 30,
            NativeHeight = 15,
            DirectParameterOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["power"] = "40"
            }
        };
        return new LaserPmtWorkflow(
            sources,
            new(0, 0, 120, 80),
            0.1,
            new(1, 0, 0),
            baseNodes,
            [powerNode],
            [pmtA, pmtB],
            [
                new("connection-a", "power-node", "power-30", "pmt-a"),
                new("connection-b", "power-node", "power-30", "pmt-b")
            ],
            2,
            3,
            1,
            new LaserPmtWorkflowNumbering(string.Empty, 1, 1));
    }

    private static IReadOnlyDictionary<string, string> Values(int power, int frequency) =>
        LaserPmtConfiguration.Parameters.ToDictionary(
            definition => definition.Name,
            definition => definition.Name switch
            {
                "power" => power.ToString(),
                "frequency" => frequency.ToString(),
                _ when definition.IsBoolean => "false",
                _ => "1"
            },
            StringComparer.Ordinal);
}
