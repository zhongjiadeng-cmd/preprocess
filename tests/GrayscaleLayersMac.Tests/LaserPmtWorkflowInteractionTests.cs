using System;
using System.Collections.Generic;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowInteractionTests
{
    [TestMethod]
    public void AddsMovesResizesAndDeletesTimestamp()
    {
        var workflow = CreateWorkflow();
        var added = LaserPmtWorkflowEditor.AddTimestamp(
            workflow, "timestamp-1", "08310712", new(10, 40, 30, 8));
        var moved = LaserPmtWorkflowEditor.MoveTimestamp(added, "timestamp-1", 12, 43);
        var resized = LaserPmtWorkflowEditor.ResizeTimestamp(moved, "timestamp-1", 36, 9);
        var timestamp = resized.Targets.OfType<LaserPmtTimestampTarget>().Single();

        Assert.AreEqual(1, timestamp.CreationOrder);
        Assert.AreEqual(new LaserPmtWorkflowBounds(12, 43, 36, 9), timestamp.Bounds);
        Assert.AreEqual(2, resized.NextCreationOrder);
        Assert.AreEqual(0, LaserPmtWorkflowEditor.DeleteTarget(resized, "timestamp-1")
            .Targets.OfType<LaserPmtTimestampTarget>().Count());
    }

    [TestMethod]
    public void ReconcileNodeValuesRemovesOnlyDanglingConnections()
    {
        var workflow = CreateWorkflow();
        var changed = LaserPmtWorkflowEditor.UpdateParameterNodeValues(
            workflow, "power-node", "40, 60", () => "power-60");

        CollectionAssert.AreEqual(
            new[] { "power-40", "power-60" },
            changed.Workflow.ParameterNodes[0].Ports.Select(port => port.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "connection-20" }, changed.RemovedConnectionIds.ToArray());
        Assert.AreEqual(1, changed.Workflow.Connections.Count);
    }

    [TestMethod]
    public void BaseParameterRemovalRequiresTargetConnectionsUntilRestored()
    {
        var workflow = CreateWorkflow();
        var removed = LaserPmtWorkflowEditor.SetBaseParameterEnabled(workflow, "frequency", false);
        Assert.IsFalse(LaserPmtWorkflowCompiler.Compile(removed).IsValid);

        var restored = LaserPmtWorkflowEditor.SetBaseParameterEnabled(removed, "frequency", true);
        Assert.IsTrue(LaserPmtWorkflowCompiler.Compile(restored).IsValid);
    }

    [TestMethod]
    public void AddsAndRemovesFanOutConnections()
    {
        var workflow = CreateWorkflow();
        var timestamp = LaserPmtWorkflowEditor.AddTimestamp(
            workflow, "timestamp-1", "08310712", new(10, 40, 30, 8));
        var connected = LaserPmtWorkflowEditor.AddConnection(
            timestamp,
            new("connection-timestamp", "power-node", "power-40", "timestamp-1"));

        Assert.AreEqual(3, connected.Connections.Count);
        var removed = LaserPmtWorkflowEditor.RemoveConnection(connected, "connection-timestamp");
        Assert.AreEqual(2, removed.Connections.Count);
    }

    private static LaserPmtWorkflow CreateWorkflow()
    {
        var baseValues = LaserPmtConfiguration.Parameters.ToDictionary(
            definition => definition.Name,
            definition => definition.IsBoolean ? "false" :
                definition.Name == "layerFeedUm" ? "3" : "1",
            StringComparer.Ordinal);
        return new LaserPmtWorkflow(
            "machine-id", new(0, 0, 100, 80), 0.1, new(1, 0, 0),
            new("base", new(-200, 0), baseValues, new HashSet<string>(StringComparer.Ordinal)),
            [new("power-node", new(-100, 0), "power", "20, 40",
                [new("power-20", "20"), new("power-40", "40")])],
            [
                new LaserPmtTarget("pmt-1", 1, new(10, 10, 20, 10), false),
                new LaserPmtTarget("pmt-2", 2, new(40, 10, 20, 10), false)
            ],
            [
                new("connection-20", "power-node", "power-20", "pmt-1"),
                new("connection-40", "power-node", "power-40", "pmt-2")
            ],
            2, 3, 1);
    }
}
