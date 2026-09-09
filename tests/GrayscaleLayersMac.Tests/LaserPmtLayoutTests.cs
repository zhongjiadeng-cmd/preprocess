using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtLayoutTests
{
    private const string Valid = """
    {
      "format_version": 1,
      "coordinate_system": {"origin":"workpiece-top-left"},
      "workpiece": {"width": 100, "height": 80},
      "unit": {"width": 20, "height": 10},
      "matrix": {"rows": 1, "columns": 2, "horizontal_gap": 20, "vertical_gap": 35},
      "numbering": {"prefix":"p_"},
      "parameter_order": ["power"],
      "jobs": [
        {
          "index": 0, "identifier": "p_0001", "row": 0, "column": 0,
          "bounds": {"left": 20, "top": 35, "width": 20, "height": 10},
          "machine_translation": {"x":20,"y":-45},
          "json_file": "p_0001machine.json", "laser_param_index": 0,
          "layer_feed_um": 3, "parameters": {"power": 20}, "patch_indices": [0]
        },
        {
          "index": 1, "identifier": "p_0002", "row": 0, "column": 1,
          "bounds": {"left": 60, "top": 35, "width": 20, "height": 10},
          "machine_translation": {"x":60,"y":-45},
          "json_file": "p_0002machine.json", "laser_param_index": 1,
          "layer_feed_um": 4, "parameters": {"power": 40}, "patch_indices": [1]
        }
      ]
    }
    """;

    [TestMethod]
    public void ParsesGeneratedLayoutForPreview()
    {
        var layout = LaserPmtLayout.Parse(Valid);
        Assert.AreEqual(100, layout.WorkpieceWidth);
        Assert.AreEqual(2, layout.Jobs.Count);
        Assert.AreEqual("p_0002", layout.Jobs[1].Identifier);
        Assert.AreEqual("40", layout.Jobs[1].Parameters["power"]);
    }

    [TestMethod]
    public void RejectsUnsupportedVersionAndOutOfBoundsJob()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            LaserPmtLayout.Parse(Valid.Replace("\"format_version\": 1", "\"format_version\": 2")));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            LaserPmtLayout.Parse(Valid.Replace("\"left\": 60", "\"left\": 90")));
    }

    [TestMethod]
    public void RejectsDuplicateJsonProperties()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            LaserPmtLayout.Parse(Valid.Replace(
                "\"width\": 100, \"height\": 80",
                "\"width\": 100, \"width\": 100, \"height\": 80")));
    }
}
