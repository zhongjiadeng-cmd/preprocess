using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtLayoutWriterTests
{
    private const string LayoutJson = """
    {
      "format_version": 1,
      "coordinate_system": {"origin":"workpiece-top-left", "preview_x":"right", "preview_y":"down", "machine_x":"right", "machine_y":"up"},
      "workpiece": {"width": 100, "height": 80},
      "unit": {"width": 20, "height": 10},
      "matrix": {"rows": 1, "columns": 2, "horizontal_gap": 20, "vertical_gap": 35},
      "numbering": {"prefix":"p_", "start": 1, "increment": 1, "padding": 4},
      "parameter_order": ["power","frequency","scan_ahead","layerFeedUm"],
      "jobs": [
        {
          "index": 0, "identifier": "p_0001", "row": 0, "column": 0,
          "bounds": {"left": 20, "top": 35, "width": 20, "height": 10},
          "machine_translation": {"x":20,"y":-45},
          "json_file": "p_0001machine.json", "laser_param_index": 0,
          "layer_feed_um": 3, "parameters": {"power": "20"}, "patch_indices": [0]
        },
        {
          "index": 1, "identifier": "p_0002", "row": 0, "column": 1,
          "bounds": {"left": 60, "top": 35, "width": 20, "height": 10},
          "machine_translation": {"x":60,"y":-45},
          "json_file": "p_0002machine.json", "laser_param_index": 1,
          "layer_feed_um": 4, "parameters": {"power": "40", "frequency": "30"}, "patch_indices": [1]
        }
      ]
    }
    """;

    [TestMethod]
    public void UpdateJobReplacesJobParametersAndKeepsOtherJobUntouched()
    {
        var tempRoot = CreateTempLayout(out var layoutPath);
        try
        {
            var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["power"] = "55",
                ["scan_ahead"] = "false",
                ["frequency"] = "350"
            };

            LaserPmtLayoutWriter.UpdateJob(layoutPath, "p_0001", overrides);

            var updated = LaserPmtLayout.Load(layoutPath);
            Assert.AreEqual("55", updated.Jobs[0].Parameters["power"]);
            Assert.AreEqual("false", updated.Jobs[0].Parameters["scan_ahead"]);
            Assert.AreEqual("350", updated.Jobs[0].Parameters["frequency"]);
            Assert.AreEqual("40", updated.Jobs[1].Parameters["power"]);
            Assert.AreEqual("30", updated.Jobs[1].Parameters["frequency"]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public void UpdateJobDropsBlankParametersSoInheritsBaseline()
    {
        var tempRoot = CreateTempLayout(out var layoutPath);
        try
        {
            var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["power"] = "",
                ["frequency"] = "120"
            };
            LaserPmtLayoutWriter.UpdateJob(layoutPath, "p_0002", overrides);

            var layout = LaserPmtLayout.Load(layoutPath);
            Assert.IsFalse(layout.Jobs[1].Parameters.ContainsKey("power"));
            Assert.AreEqual("120", layout.Jobs[1].Parameters["frequency"]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public void UpdateJobRejectsInvalidParameterValuesAndUnknownNames()
    {
        var tempRoot = CreateTempLayout(out var layoutPath);
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => LaserPmtLayoutWriter.UpdateJob(
                layoutPath,
                "p_0001",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["scan_ahead"] = "maybe" }));

            Assert.ThrowsExactly<InvalidDataException>(() => LaserPmtLayoutWriter.UpdateJob(
                layoutPath,
                "p_0001",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["unknown"] = "1" }));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public void UpdateJobMissesPreservesOriginalDocument()
    {
        var tempRoot = CreateTempLayout(out var layoutPath);
        try
        {
            var original = File.ReadAllText(layoutPath);

            Assert.ThrowsExactly<InvalidDataException>(() => LaserPmtLayoutWriter.UpdateJob(
                layoutPath,
                "p_unknown",
                new Dictionary<string, string>(StringComparer.Ordinal)));

            Assert.AreEqual(original, File.ReadAllText(layoutPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public void UpdateJobPreservesLayoutTopLevelFields()
    {
        var tempRoot = CreateTempLayout(out var layoutPath);
        try
        {
            LaserPmtLayoutWriter.UpdateJob(
                layoutPath,
                "p_0001",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["power"] = "70" });

            using var document = JsonDocument.Parse(File.ReadAllText(layoutPath));
            var root = document.RootElement;
            Assert.AreEqual(1, root.GetProperty("format_version").GetInt32());
            Assert.AreEqual(100d, root.GetProperty("workpiece").GetProperty("width").GetDouble());
            Assert.AreEqual(2, root.GetProperty("matrix").GetProperty("columns").GetInt32());
            Assert.AreEqual("p_", root.GetProperty("numbering").GetProperty("prefix").GetString());
            Assert.AreEqual("workpiece-top-left",
                root.GetProperty("coordinate_system").GetProperty("origin").GetString());
            var jobs = root.GetProperty("jobs");
            Assert.AreEqual("70", jobs[0].GetProperty("parameters").GetProperty("power").GetString());
            Assert.AreEqual("40", jobs[1].GetProperty("parameters").GetProperty("power").GetString());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempLayout(out string layoutPath)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"pmt-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        layoutPath = Path.Combine(tempRoot, "pmt-layout.json");
        File.WriteAllText(layoutPath, LayoutJson);
        return tempRoot;
    }
}
