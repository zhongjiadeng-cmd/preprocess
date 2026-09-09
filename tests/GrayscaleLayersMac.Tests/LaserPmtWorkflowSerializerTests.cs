using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtWorkflowSerializerTests
{
    [TestMethod]
    public void VersionTwoRoundTripPreservesWorkflowIdentityAndConnections()
    {
        var source = CreateWorkflow();

        var json = LaserPmtWorkflowSerializer.Serialize(source);
        var parsed = LaserPmtWorkflowSerializer.Parse(json);

        Assert.AreEqual(source.BaseMachineIdentity, parsed.BaseMachineIdentity);
        Assert.AreEqual(source.HatchSpacing, parsed.HatchSpacing);
        Assert.AreEqual(source.Viewport, parsed.Viewport);
        Assert.AreEqual(source.NextPmtNumber, parsed.NextPmtNumber);
        Assert.AreEqual(source.Numbering, parsed.Numbering);
        Assert.AreEqual(source.Targets[0], parsed.Targets[0]);
        Assert.AreEqual(source.Connections[0], parsed.Connections[0]);
        CollectionAssert.AreEqual(
            source.ParameterNodes[0].Ports.Select(port => port.Id).ToArray(),
            parsed.ParameterNodes[0].Ports.Select(port => port.Id).ToArray());
    }

    [TestMethod]
    public void RejectsCompiledTargetThatDoesNotMatchSourceWorkflow()
    {
        var json = LaserPmtWorkflowSerializer.Serialize(CreateWorkflow());
        var tampered = json.Replace("\"power\": 40", "\"power\": 41", StringComparison.Ordinal);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            LaserPmtWorkflowSerializer.Parse(tampered));
    }

    [TestMethod]
    public void RejectsDuplicatePropertiesAndUnsupportedVersion()
    {
        var json = LaserPmtWorkflowSerializer.Serialize(CreateWorkflow());
        var duplicate = json.Replace(
            "\"format_version\": 2,",
            "\"format_version\": 2, \"format_version\": 2,",
            StringComparison.Ordinal);
        var oldVersion = json.Replace("\"format_version\": 2", "\"format_version\": 1", StringComparison.Ordinal);

        Assert.ThrowsExactly<InvalidDataException>(() => LaserPmtWorkflowSerializer.Parse(duplicate));
        Assert.ThrowsExactly<InvalidDataException>(() => LaserPmtWorkflowSerializer.Parse(oldVersion));
    }

    [TestMethod]
    public void ExistingVersionOnePreviewParserRemainsReadOnlyCompatible()
    {
        const string legacy = """
        {
          "format_version":1,
          "coordinate_system":{"origin":"workpiece-top-left"},
          "workpiece":{"width":100,"height":80},
          "unit":{"width":20,"height":10},
          "matrix":{"rows":1,"columns":1,"horizontal_gap":40,"vertical_gap":35},
          "numbering":{"prefix":"p_"},
          "parameter_order":[],
          "jobs":[{
            "index":0,"identifier":"p_0001","row":0,"column":0,
            "bounds":{"left":40,"top":35,"width":20,"height":10},
            "machine_translation":{"x":40,"y":-45},"json_file":"p_0001machine.json",
            "laser_param_index":0,"layer_feed_um":3,"parameters":{},"patch_indices":[[0,0]]
          }]
        }
        """;

        Assert.AreEqual("p_0001", LaserPmtLayout.Parse(legacy).Jobs[0].Identifier);
        Assert.ThrowsExactly<InvalidDataException>(() => LaserPmtWorkflowSerializer.Parse(legacy));
    }

    private static LaserPmtWorkflow CreateWorkflow()
    {
        var baseValues = LaserPmtConfiguration.Parameters.ToDictionary(
            definition => definition.Name,
            definition => definition.IsBoolean ? "false" : "1",
            StringComparer.Ordinal);
        return new LaserPmtWorkflow(
            "sha256:machine-id",
            new(0, 0, 100, 80),
            0.12,
            new(1.25, 12, -4),
            new("base", new(-200, 20), baseValues, new HashSet<string>(StringComparer.Ordinal)),
            [new("power-node", new(-100, 80), "power", "40", [new("power-40", "40")])],
            [
                new LaserPmtTarget("pmt-1", 1, new(10, 10, 20, 10), true),
                new LaserPmtTimestampTarget("timestamp-1", 1, "08310712", new(10, 40, 30, 8))
            ],
            [new("connection-1", "power-node", "power-40", "pmt-1")],
            3,
            2,
            2);
    }
}
