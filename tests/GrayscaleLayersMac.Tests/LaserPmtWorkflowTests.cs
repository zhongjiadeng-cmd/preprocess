using System;
using System.Collections.Generic;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowTests
{
    [TestMethod]
    public void CreatesValidatedWorkflowSnapshot()
    {
        var workflow = CreateWorkflow();

        Assert.AreEqual("base", workflow.BaseNode.Id);
        Assert.AreEqual(1, workflow.ParameterNodes.Count);
        Assert.AreEqual(2, workflow.Targets.Count);
        Assert.AreEqual(1, workflow.Connections.Count);
        Assert.AreEqual(3, workflow.NextPmtNumber);
        Assert.AreEqual(2, workflow.NextCreationOrder);
    }

    [TestMethod]
    public void RejectsDuplicateStableIds()
    {
        var workflow = CreateWorkflow();
        var duplicateTarget = workflow.Targets[0] with { Id = workflow.Targets[1].Id };

        Assert.ThrowsExactly<ArgumentException>(() => new LaserPmtWorkflow(
            workflow.BaseMachineIdentity,
            workflow.Workpiece,
            workflow.HatchSpacing,
            workflow.Viewport,
            workflow.BaseNode,
            workflow.ParameterNodes,
            [duplicateTarget, workflow.Targets[1]],
            workflow.Connections,
            workflow.PmtColumns,
            workflow.NextPmtNumber,
            workflow.NextCreationOrder));
    }

    [TestMethod]
    public void RejectsDanglingPortsAndTargets()
    {
        var workflow = CreateWorkflow();
        var danglingPort = workflow.Connections[0] with { SourcePortId = "missing" };
        var danglingTarget = workflow.Connections[0] with { TargetId = "missing" };

        Assert.ThrowsExactly<ArgumentException>(() => ReplaceConnections(workflow, [danglingPort]));
        Assert.ThrowsExactly<ArgumentException>(() => ReplaceConnections(workflow, [danglingTarget]));
    }

    [TestMethod]
    public void RejectsSecondInputForSameTargetParameter()
    {
        var workflow = CreateWorkflow();
        var duplicateInput = workflow.Connections[0] with
        {
            Id = "connection-2",
            SourcePortId = "port-2"
        };

        Assert.ThrowsExactly<ArgumentException>(() =>
            ReplaceConnections(workflow, [workflow.Connections[0], duplicateInput]));
    }

    [TestMethod]
    public void RejectsInvalidTimestampAndNumberState()
    {
        var workflow = CreateWorkflow();
        var invalidTimestamp = ((LaserPmtTimestampTarget)workflow.Targets[1]) with { Text = "0831071x" };

        Assert.ThrowsExactly<ArgumentException>(() => new LaserPmtWorkflow(
            workflow.BaseMachineIdentity,
            workflow.Workpiece,
            workflow.HatchSpacing,
            workflow.Viewport,
            workflow.BaseNode,
            workflow.ParameterNodes,
            [workflow.Targets[0], invalidTimestamp],
            workflow.Connections,
            workflow.PmtColumns,
            1,
            workflow.NextCreationOrder));
    }

    private static LaserPmtWorkflow CreateWorkflow()
    {
        var baseNode = new LaserPmtBaseParameterNode(
            "base",
            new LaserPmtWorkflowPoint(-180, 20),
            new Dictionary<string, string>
            {
                ["power"] = "20",
                ["frequency"] = "300"
            },
            new HashSet<string>(StringComparer.Ordinal));
        var parameterNode = new LaserPmtSingleParameterNode(
            "power-node",
            new LaserPmtWorkflowPoint(-180, 160),
            "power",
            "30, 40",
            [
                new LaserPmtParameterPort("port-1", "30"),
                new LaserPmtParameterPort("port-2", "40")
            ]);
        return new LaserPmtWorkflow(
            "machine-id",
            new LaserPmtWorkflowBounds(0, 0, 100, 80),
            0.1,
            new LaserPmtCanvasViewport(1, 0, 0),
            baseNode,
            [parameterNode],
            [
                new LaserPmtTarget("target-1", 1, new LaserPmtWorkflowBounds(10, 10, 20, 10), false),
                new LaserPmtTimestampTarget("timestamp-1", 1, "08310712", new LaserPmtWorkflowBounds(10, 40, 30, 8))
            ],
            [new LaserPmtConnection("connection-1", "power-node", "port-1", "target-1")],
            2,
            3,
            2);
    }

    private static LaserPmtWorkflow ReplaceConnections(
        LaserPmtWorkflow workflow,
        IReadOnlyList<LaserPmtConnection> connections) => new(
            workflow.BaseMachineIdentity,
            workflow.Workpiece,
            workflow.HatchSpacing,
            workflow.Viewport,
            workflow.BaseNode,
            workflow.ParameterNodes,
            workflow.Targets,
            connections,
            workflow.PmtColumns,
            workflow.NextPmtNumber,
            workflow.NextCreationOrder);
}
