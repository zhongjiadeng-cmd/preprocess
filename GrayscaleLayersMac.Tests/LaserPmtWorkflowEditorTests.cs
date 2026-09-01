using System;
using System.Collections.Generic;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowEditorTests
{
    [TestMethod]
    public void DeletePmtKeepsNumberGapAndRemovesItsConnections()
    {
        var workflow = CreateWorkflow();

        var edited = LaserPmtWorkflowEditor.DeletePmt(workflow, "pmt-2");

        CollectionAssert.AreEqual(
            new[] { 1, 3 },
            edited.Targets.OfType<LaserPmtTarget>().Select(item => item.Number).ToArray());
        Assert.AreEqual(4, edited.NextPmtNumber);
        Assert.IsFalse(edited.Connections.Any(connection => connection.TargetId == "pmt-2"));
    }

    [TestMethod]
    public void IncreasingCountAppendsNeverUsedNumbersWithoutMovingExistingPmts()
    {
        var workflow = LaserPmtWorkflowEditor.DeletePmt(CreateWorkflow(), "pmt-2");
        var before = workflow.Targets.OfType<LaserPmtTarget>()
            .ToDictionary(item => item.Id, item => item.Bounds, StringComparer.Ordinal);
        var id = 0;

        var edited = LaserPmtWorkflowEditor.SetPmtCount(
            workflow, 4, 20, 10, () => $"new-pmt-{++id}");

        CollectionAssert.AreEqual(
            new[] { 1, 3, 4, 5 },
            edited.Targets.OfType<LaserPmtTarget>().Select(item => item.Number).ToArray());
        Assert.AreEqual(before["pmt-1"], edited.Targets.Single(item => item.Id == "pmt-1").Bounds);
        Assert.AreEqual(before["pmt-3"], edited.Targets.Single(item => item.Id == "pmt-3").Bounds);
        Assert.AreEqual(6, edited.NextPmtNumber);
    }

    [TestMethod]
    public void ReducingCountDeletesHighestNumbersOnly()
    {
        var edited = LaserPmtWorkflowEditor.SetPmtCount(
            CreateWorkflow(), 1, 20, 10, () => throw new AssertFailedException());

        CollectionAssert.AreEqual(
            new[] { 1 },
            edited.Targets.OfType<LaserPmtTarget>().Select(item => item.Number).ToArray());
    }

    [TestMethod]
    public void MovingPmtMarksItManualAndChangingColumnsPreservesPosition()
    {
        var moved = LaserPmtWorkflowEditor.MovePmt(CreateWorkflow(), "pmt-1", 17, 23);
        var movedPmt = moved.Targets.OfType<LaserPmtTarget>().Single(item => item.Id == "pmt-1");
        var changedColumns = LaserPmtWorkflowEditor.SetPmtColumns(moved, 5);

        Assert.IsTrue(movedPmt.WasManuallyMoved);
        Assert.AreEqual(new LaserPmtWorkflowBounds(17, 23, 20, 10), movedPmt.Bounds);
        Assert.AreEqual(movedPmt.Bounds, changedColumns.Targets.Single(item => item.Id == "pmt-1").Bounds);
    }

    [TestMethod]
    public void AutoArrangePreservesNumbersAndConnections()
    {
        var workflow = LaserPmtWorkflowEditor.MovePmt(CreateWorkflow(), "pmt-1", 70, 60);

        var arranged = LaserPmtWorkflowEditor.AutoArrangePmts(workflow, 20, 10);

        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            arranged.Targets.OfType<LaserPmtTarget>().Select(item => item.Number).ToArray());
        Assert.IsTrue(arranged.Targets.OfType<LaserPmtTarget>().All(item => !item.WasManuallyMoved));
        Assert.AreEqual(workflow.Connections[0], arranged.Connections[0]);
    }

    [TestMethod]
    public void GeometryAllowsEdgeContactButRejectsOverlapAndOutOfBounds()
    {
        var touching = CreateWorkflow(
            first: new(0, 0, 20, 10),
            second: new(20, 0, 20, 10),
            timestamp: new(0, 10, 30, 8));
        Assert.AreEqual(0, LaserPmtWorkflowEditor.ValidateGeometry(touching).Count);

        var overlapping = CreateWorkflow(
            first: new(0, 0, 20, 10),
            second: new(19.9996, 0, 20, 10),
            timestamp: new(90, 75, 20, 8));
        var errors = LaserPmtWorkflowEditor.ValidateGeometry(overlapping, coordinateDecimals: 3);

        Assert.IsTrue(errors.Any(error => error.Code == LaserPmtGeometryErrorCode.Overlap));
        Assert.IsTrue(errors.Any(error => error.Code == LaserPmtGeometryErrorCode.OutOfBounds));
    }

    private static LaserPmtWorkflow CreateWorkflow(
        LaserPmtWorkflowBounds? first = null,
        LaserPmtWorkflowBounds? second = null,
        LaserPmtWorkflowBounds? timestamp = null)
    {
        var baseValues = LaserPmtConfiguration.Parameters.ToDictionary(
            definition => definition.Name,
            definition => definition.IsBoolean ? "false" : "1",
            StringComparer.Ordinal);
        return new LaserPmtWorkflow(
            "machine-id",
            new(0, 0, 100, 80),
            0.1,
            new(1, 0, 0),
            new("base", new(-200, 0), baseValues, new HashSet<string>(StringComparer.Ordinal)),
            [new("power-node", new(-100, 0), "power", "40", [new("power-40", "40")])],
            [
                new LaserPmtTarget("pmt-1", 1, first ?? new(10, 10, 20, 10), false),
                new LaserPmtTarget("pmt-2", 2, second ?? new(40, 10, 20, 10), false),
                new LaserPmtTarget("pmt-3", 3, new(70, 10, 20, 10), false),
                new LaserPmtTimestampTarget("timestamp-1", 1, "08310712", timestamp ?? new(10, 50, 30, 8))
            ],
            [new("connection-1", "power-node", "power-40", "pmt-2")],
            3,
            4,
            2);
    }
}
