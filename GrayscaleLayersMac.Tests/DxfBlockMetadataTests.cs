using System;
using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class DxfBlockMetadataTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"DxfBlockMetadataTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void MissingCompanionReturnsNull()
    {
        Assert.IsNull(DxfBlockMetadata.LoadForDxf(Path.Combine(_root, "plain.dxf")));
    }

    [TestMethod]
    public void ReadsV1DocumentAndClassifiesOriginalLineOrdinals()
    {
        var dxf = Path.Combine(_root, "layer.dxf");
        File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), HappyJson);

        var metadata = DxfBlockMetadata.LoadForDxf(dxf);

        Assert.IsNotNull(metadata);
        metadata.ValidateLineCount(5);
        Assert.AreEqual(3, metadata.Blocks.Count);
        Assert.AreEqual(new DxfBlockDefinition(7, 1.5, -2, 2), metadata.Blocks[0]);
        Assert.AreEqual(new DxfBlockDefinition(9, 3, 4.5, 0), metadata.Blocks[1]);
        Assert.AreEqual(new DxfBlockDefinition(3, 5, 6, 1), metadata.Blocks[2]);
        Assert.AreEqual(new DxfLineClassification(0, true), metadata.ClassifyLine(0));
        Assert.AreEqual(new DxfLineClassification(7, false), metadata.ClassifyLine(2));
        Assert.AreEqual(new DxfLineClassification(7, false), metadata.ClassifyLine(3));
        Assert.AreEqual(new DxfLineClassification(3, false), metadata.ClassifyLine(4));
    }

    [TestMethod]
    public void NonContiguousSampleOrdinalsKeepSourceBlockMapping()
    {
        var metadata = LoadHappyFixture();

        Assert.AreEqual(7, metadata.ClassifyLine(2).BlockIndex);
        Assert.AreEqual(3, metadata.ClassifyLine(4).BlockIndex);
    }

    [TestMethod]
    [DataRow("{", "malformed document")]
    [DataRow("{\"version\":1,\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1}]}", "duplicate version")]
    [DataRow("{\"version\":2,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1}]}", "unsupported version")]
    [DataRow("{\"version\":1,\"border_line_count\":true,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1}]}", "boolean border count")]
    [DataRow("{\"version\":1,\"border_line_count\":-1,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1}]}", "negative border count")]
    [DataRow("{\"version\":1,\"border_line_count\":0}", "missing top-level field")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1}],\"extra\":true}", "extra top-level field")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[]}", "empty blocks")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0}]}", "missing block field")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1,\"extra\":true}]}", "extra block field")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":-1,\"center_x\":0,\"center_y\":0,\"line_count\":1}]}", "negative block index")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1},{\"block_index\":0,\"center_x\":1,\"center_y\":1,\"line_count\":1}]}", "duplicate block index")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":true}]}", "boolean line count")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1.5}]}", "fractional line count")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":-1}]}", "negative line count")]
    [DataRow("{\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":1e999,\"center_y\":0,\"line_count\":1}]}", "non-finite center")]
    public void PresentInvalidJsonIsRejected(string json, string _)
    {
        var dxf = Path.Combine(_root, "invalid.dxf");
        File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), json);

        var error = Assert.ThrowsExactly<InvalidDataException>(() => DxfBlockMetadata.LoadForDxf(dxf));

        StringAssert.Contains(error.Message, ".blocks.json");
    }

    [TestMethod]
    public void EmptyCompanionIsRejected()
    {
        var dxf = Path.Combine(_root, "empty.dxf");
        File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), string.Empty);

        Assert.ThrowsExactly<InvalidDataException>(() => DxfBlockMetadata.LoadForDxf(dxf));
    }

    [TestMethod]
    public void DirectoryCompanionIsRejected()
    {
        var dxf = Path.Combine(_root, "directory.dxf");
        Directory.CreateDirectory(Path.ChangeExtension(dxf, ".blocks.json"));

        Assert.ThrowsExactly<InvalidDataException>(() => DxfBlockMetadata.LoadForDxf(dxf));
    }

    [TestMethod]
    public void ReparsePointCompanionIsRejected()
    {
        var dxf = Path.Combine(_root, "linked.dxf");
        var sidecar = Path.ChangeExtension(dxf, ".blocks.json");
        var target = Path.Combine(_root, "target.json");
        File.WriteAllText(target, HappyJson);
        File.CreateSymbolicLink(sidecar, target);

        Assert.ThrowsExactly<InvalidDataException>(() => DxfBlockMetadata.LoadForDxf(dxf));
    }

    [TestMethod]
    public void ValidateLineCountRejectsUnexpectedTotal()
    {
        var metadata = LoadHappyFixture();

        Assert.ThrowsExactly<InvalidDataException>(() => metadata.ValidateLineCount(4));
    }

    [TestMethod]
    public void ClassifyLineRejectsOutOfRangeOrdinals()
    {
        var metadata = LoadHappyFixture();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => metadata.ClassifyLine(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => metadata.ClassifyLine(5));
    }

    private DxfBlockMetadata LoadHappyFixture()
    {
        var dxf = Path.Combine(_root, "sample.dxf");
        File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), HappyJson);
        return DxfBlockMetadata.LoadForDxf(dxf)!;
    }

    private const string HappyJson = """
        {"version":1,"border_line_count":2,"blocks":[
          {"block_index":7,"center_x":1.5,"center_y":-2,"line_count":2},
          {"block_index":9,"center_x":3,"center_y":4.5,"line_count":0},
          {"block_index":3,"center_x":5,"center_y":6,"line_count":1}
        ]}
        """;
}
