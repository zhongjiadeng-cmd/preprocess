using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class DxfPreviewControlTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"DxfPreviewControlTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void MissingCompanionLoadsWithoutInferredBlockSummary()
    {
        var dxf = Path.Combine(_root, "plain.dxf");
        WriteDxf(dxf, (0d, 0d, 0d, 10d), (0d, 5d, 5d, 5d), (0d, 10d, 5d, 10d));
        using var preview = new DxfPreviewControl();

        preview.LoadFile(dxf);

        Assert.AreEqual("plain.dxf · 3 条 LINE", preview.Summary);
        Assert.AreEqual(3, preview.LineCount);
    }

    [TestMethod]
    public void ValidCompanionReportsDeclaredBlocksIncludingEmptyBlock()
    {
        var dxf = Path.Combine(_root, "blocked.dxf");
        WriteDxf(
            dxf,
            (0d, 0d, 10d, 0d),
            (0d, 1d, 10d, 1d),
            (0d, 2d, 10d, 2d),
            (0d, 3d, 10d, 3d),
            (0d, 4d, 10d, 4d),
            (0d, 5d, 10d, 5d));
        File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), """
            {"version":1,"border_line_count":4,"blocks":[
              {"block_index":4,"center_x":0,"center_y":0,"line_count":1},
              {"block_index":8,"center_x":1,"center_y":1,"line_count":0},
              {"block_index":2,"center_x":2,"center_y":2,"line_count":1}]}
            """);
        using var preview = new DxfPreviewControl();

        preview.LoadFile(dxf);

        Assert.AreEqual("blocked.dxf · 6 条 LINE · 加工块 3 个", preview.Summary);
    }

    [TestMethod]
    public void PresentCompanionWithMismatchedLineCountFailsPreview()
    {
        var dxf = Path.Combine(_root, "mismatched.dxf");
        WriteDxf(dxf, (0d, 0d, 10d, 0d));
        File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), """
            {"version":1,"border_line_count":0,"blocks":[
              {"block_index":1,"center_x":0,"center_y":0,"line_count":2}]}
            """);
        using var preview = new DxfPreviewControl();

        Assert.ThrowsExactly<InvalidDataException>(() => preview.LoadFile(dxf));
    }

    [TestMethod]
    public void SamplingAcrossBlockBoundaryKeepsSourceOrdinalClassifications()
    {
        var dxf = Path.Combine(_root, "sampled.dxf");
        WriteDxf(
            dxf,
            (0d, 0d, 10d, 0d),
            (0d, 1d, 10d, 1d),
            (0d, 2d, 10d, 2d),
            (0d, 3d, 10d, 3d),
            (0d, 4d, 10d, 4d),
            (0d, 5d, 10d, 5d));
        File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), """
            {"version":1,"border_line_count":0,"blocks":[
              {"block_index":7,"center_x":0,"center_y":0,"line_count":2},
              {"block_index":3,"center_x":1,"center_y":1,"line_count":4}]}
            """);
        var metadata = DxfBlockMetadata.LoadForDxf(dxf);

        var scan = DxfPreviewControl.ScanFile(dxf, collectEvery: 2, metadata);

        Assert.AreEqual(6, scan.Count);
        CollectionAssert.AreEqual(
            new[] { 7, 3, 3 },
            scan.Segments.Select(segment => segment.BlockIndex).ToArray());
        Assert.IsFalse(scan.Segments.Any(segment => segment.IsBorder));
    }

    private static void WriteDxf(
        string path,
        params (double X1, double Y1, double X2, double Y2)[] lines)
    {
        var content = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
        foreach (var line in lines)
        {
            content.Append("0\nLINE\n10\n")
                .Append(line.X1.ToString(CultureInfo.InvariantCulture))
                .Append("\n20\n")
                .Append(line.Y1.ToString(CultureInfo.InvariantCulture))
                .Append("\n11\n")
                .Append(line.X2.ToString(CultureInfo.InvariantCulture))
                .Append("\n21\n")
                .Append(line.Y2.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
        content.Append("0\nENDSEC\n0\nEOF\n");
        File.WriteAllText(path, content.ToString());
    }
}
