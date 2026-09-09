using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtBaseMetadataTests
{
    [TestMethod]
    public void ParsesInspectorOutputIntoWorkflowValues()
    {
        var parameters = string.Join(",", LaserPmtConfiguration.Parameters.Select(
            definition => $"\"{definition.Name}\":{(definition.IsBoolean ? "false" : "1")}"));
        var metadata = LaserPmtBaseMetadata.Parse(
            $"{{\"base_machine_identity\":\"machine\",\"unit\":{{\"width\":4,\"height\":2}}," +
            $"\"parameters\":{{{parameters}}}}}");

        Assert.AreEqual("machine", metadata.Identity);
        Assert.AreEqual(4d, metadata.UnitWidth);
        Assert.AreEqual(2d, metadata.UnitHeight);
        Assert.AreEqual("false", metadata.Parameters["scan_ahead"]);
    }
}
