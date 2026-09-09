using System;
using System.Collections.Generic;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowCanvasSelectionTests
{
    [TestMethod]
    public void WorkpieceSelectionSurvivesLiveWorkflowUpdates()
    {
        var canvas = new LaserPmtWorkflowCanvas();
        var workflow = CreateWorkflow();
        canvas.Load(workflow);

        canvas.SelectWorkpiece();
        canvas.UpdateWorkflow(LaserPmtWorkflowEditor.SetWorkpiece(
            workflow, workflow.Workpiece with { Width = 120 }));

        Assert.IsTrue(canvas.IsWorkpieceSelected);
        Assert.IsTrue(canvas.HasEditableSelection);
        Assert.IsNull(canvas.SelectedId);
    }

    private static LaserPmtWorkflow CreateWorkflow()
    {
        var values = LaserPmtConfiguration.Parameters.ToDictionary(
            definition => definition.Name,
            definition => definition.IsBoolean ? "false" : "1",
            StringComparer.Ordinal);
        return new LaserPmtWorkflow(
            "machine", new(0, 0, 100, 80), 0.1, new(1, 0, 0),
            new("base", new(-100, 0), values, new HashSet<string>(StringComparer.Ordinal)),
            [], [new LaserPmtTarget("pmt", 1, new(10, 10, 20, 10), false)], [], 1, 2, 1);
    }
}
