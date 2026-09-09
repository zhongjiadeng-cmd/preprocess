using System;
using System.Collections.Generic;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowCompilerTests
{
    [TestMethod]
    public void ReconcilesPortsByNormalizedValueAndReportsRemovedConnections()
    {
        var node = new LaserPmtSingleParameterNode(
            "power-node",
            new LaserPmtWorkflowPoint(0, 0),
            "power",
            "20, 30",
            [new("port-20", "20"), new("port-30", "30")]);
        var next = 0;

        var result = LaserPmtWorkflowCompiler.ReconcilePorts(
            node,
            "30, 40",
            () => $"new-{++next}");

        Assert.IsTrue(result.Success, result.Error);
        CollectionAssert.AreEqual(
            new[] { "port-30", "new-1" },
            result.Node!.Ports.Select(port => port.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "port-20" }, result.RemovedPortIds.ToArray());
    }

    [TestMethod]
    public void RejectsInvalidOrRepeatedPortValues()
    {
        var node = new LaserPmtSingleParameterNode(
            "power-node", new(0, 0), "power", "20", [new("port-20", "20")]);

        var result = LaserPmtWorkflowCompiler.ReconcilePorts(node, "20, 20", () => "unused");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "重复");
    }

    [TestMethod]
    public void OnePortOverridesSameParameterForMultipleTargets()
    {
        var workflow = CreateWorkflow(
            removed: new HashSet<string>(StringComparer.Ordinal),
            connections:
            [
                new("c1", "power-node", "power-40", "pmt-1"),
                new("c2", "power-node", "power-40", "timestamp-1")
            ]);

        var result = LaserPmtWorkflowCompiler.Compile(workflow);

        Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors.Select(error => error.Message)));
        Assert.AreEqual(40, result.Targets[0].Parameters["power"]);
        Assert.AreEqual(40, result.Targets[1].Parameters["power"]);
    }

    [TestMethod]
    public void MissingRemovedBaseParameterIdentifiesIncompleteTarget()
    {
        var workflow = CreateWorkflow(
            removed: new HashSet<string>(["frequency"], StringComparer.Ordinal),
            connections: [new("c1", "frequency-node", "frequency-350", "pmt-1")]);

        var result = LaserPmtWorkflowCompiler.Compile(workflow);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual("timestamp-1", result.Errors[0].TargetId);
        Assert.AreEqual("frequency", result.Errors[0].ParameterName);
    }

    [TestMethod]
    public void OrdersPmtsBeforeTimestampsUsingStableNumbers()
    {
        var workflow = CreateWorkflow(new HashSet<string>(StringComparer.Ordinal), []);
        var result = LaserPmtWorkflowCompiler.Compile(workflow);

        CollectionAssert.AreEqual(
            new[] { "pmt-1", "timestamp-1" },
            result.Targets.Select(target => target.TargetId).ToArray());
    }

    private static LaserPmtWorkflow CreateWorkflow(
        IReadOnlySet<string> removed,
        IReadOnlyList<LaserPmtConnection> connections)
    {
        var baseValues = LaserPmtConfiguration.Parameters.ToDictionary(
            definition => definition.Name,
            definition => definition.IsBoolean
                ? "false"
                : definition.Name == "layerFeedUm" ? "3" : "1",
            StringComparer.Ordinal);
        var nodes = new[]
        {
            new LaserPmtSingleParameterNode(
                "power-node", new(-100, 0), "power", "40", [new("power-40", "40")]),
            new LaserPmtSingleParameterNode(
                "frequency-node", new(-100, 80), "frequency", "350", [new("frequency-350", "350")])
        };
        return new LaserPmtWorkflow(
            "machine-id",
            new(0, 0, 100, 80),
            0.1,
            new(1, 0, 0),
            new("base", new(-200, 0), baseValues, removed),
            nodes,
            [
                new LaserPmtTarget("pmt-1", 1, new(10, 10, 20, 10), false),
                new LaserPmtTimestampTarget("timestamp-1", 1, "08310712", new(10, 40, 30, 8))
            ],
            connections,
            2,
            2,
            2);
    }
}
