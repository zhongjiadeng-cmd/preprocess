using System.Linq;
using System.Text.Json;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class LaserPmtConfigurationTests
{
    [TestMethod]
    public void ParsesExplicitValuesAndCalculatesCartesianProduct()
    {
        var rows = new[]
        {
            new LaserPmtParameterRow("power", "20, 40"),
            new LaserPmtParameterRow("scan_ahead", "true, false"),
            new LaserPmtParameterRow("layerFeedUm", "2, 3, 5")
        };

        Assert.IsTrue(LaserPmtConfiguration.TryParseRows(
            rows, out var parsed, out var count, out var error), error);
        Assert.AreEqual(12, count);
        CollectionAssert.AreEqual(new object[] { 20, 40 }, parsed[0].Values.ToArray());
        CollectionAssert.AreEqual(new object[] { true, false }, parsed[1].Values.ToArray());
    }

    [TestMethod]
    public void RejectsDuplicateParametersAndValues()
    {
        Assert.IsFalse(LaserPmtConfiguration.TryParseRows(
            [new("power", "20"), new("power", "30")],
            out _, out _, out var duplicateParameter));
        StringAssert.Contains(duplicateParameter, "重复");

        Assert.IsFalse(LaserPmtConfiguration.TryParseRows(
            [new("power", "20,20")],
            out _, out _, out var duplicateValue));
        StringAssert.Contains(duplicateValue, "重复值");
    }

    [TestMethod]
    public void RequestJsonPreservesTypedParameterValues()
    {
        var json = LaserPmtConfiguration.BuildRequestJson(
            "/tmp/base", "/tmp", "LaserPMT", 100, 80, 4,
            "p_", 1, 1, 4,
            [new("power", "20,40"), new("scan_ahead", "true,false")],
            "owner");
        using var document = JsonDocument.Parse(json);
        var parameters = document.RootElement.GetProperty("parameters");
        Assert.AreEqual(20, parameters[0].GetProperty("values")[0].GetInt32());
        Assert.IsTrue(parameters[1].GetProperty("values")[0].GetBoolean());
    }
}
