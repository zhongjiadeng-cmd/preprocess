using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
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
    public void PreparedPreviewInstallsAfterSourceFileIsRemoved()
    {
        var dxf = Path.Combine(_root, "staged.dxf");
        WriteDxf(dxf, (0d, 0d, 10d, 0d), (0d, 5d, 10d, 5d));
        var prepared = DxfPreviewControl.PrepareFile(dxf);
        File.Delete(dxf);
        using var preview = new DxfPreviewControl();

        preview.InstallPreparedFile(prepared, keepView: false);

        Assert.AreEqual(2, preview.LineCount);
        Assert.AreEqual("staged.dxf · 2 条 LINE", preview.Summary);
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

    [TestMethod]
    public void ZoomButtonsStepByTheSharedFactor()
    {
        using var preview = new DxfPreviewControl();

        preview.ZoomIn();
        Assert.AreEqual(
            GrayscalePreviewViewMath.ZoomButtonStep, preview.Zoom, 1e-9);

        preview.ZoomOut();
        Assert.AreEqual(1d, preview.Zoom, 1e-9, "放大一次再缩小一次应回到基准倍率");
    }

    [TestMethod]
    public void ZoomStaysInsideTheSharedBounds()
    {
        using var preview = new DxfPreviewControl();

        for (var i = 0; i < 200; i++)
            preview.ZoomIn();
        Assert.AreEqual(GrayscalePreviewViewMath.MaxZoom, preview.Zoom, 1e-9);

        for (var i = 0; i < 400; i++)
            preview.ZoomOut();
        Assert.AreEqual(GrayscalePreviewViewMath.MinZoom, preview.Zoom, 1e-9);
    }

    [TestMethod]
    public void ActualSizeKeepsPanWhileFitToViewResetsIt()
    {
        var dxf = Path.Combine(_root, "sized.dxf");
        WriteDxf(dxf, (0d, 0d, 10d, 0d), (0d, 5d, 10d, 5d));
        using var preview = new DxfPreviewControl();
        preview.LoadFile(dxf);

        // 锚点不在中心，缩放后必然留下非零平移，才能区分「保留视图」与「重排视图」。
        preview.ZoomAt(new Point(50, 30), 2);
        var panBefore = preview.PanOffset;
        Assert.IsTrue(Math.Abs(panBefore.X) > 1e-6 || Math.Abs(panBefore.Y) > 1e-6);

        preview.ActualSize();
        Assert.AreEqual(1d, preview.Zoom, 1e-9);
        Assert.AreEqual(panBefore.X, preview.PanOffset.X, 1e-6, "100% 只退倍率，不应丢掉正在看的位置");
        Assert.AreEqual(panBefore.Y, preview.PanOffset.Y, 1e-6);

        preview.FitToView();
        Assert.AreEqual(1d, preview.Zoom, 1e-9);
        Assert.AreEqual(0d, preview.PanOffset.X, 1e-9, "适应窗口要一并回到居中位置");
        Assert.AreEqual(0d, preview.PanOffset.Y, 1e-9);
    }

    [TestMethod]
    public void LoadFileWithKeepViewPreservesZoomAndPan()
    {
        var dxf = Path.Combine(_root, "kept.dxf");
        WriteDxf(dxf, (0d, 0d, 10d, 0d), (0d, 5d, 10d, 5d));
        using var preview = new DxfPreviewControl();

        preview.LoadFile(dxf);
        preview.ZoomAt(new Point(50, 30), 2);
        var zoom = preview.Zoom;
        var pan = preview.PanOffset;

        preview.LoadFile(dxf, keepView: true);

        Assert.AreEqual(zoom, preview.Zoom, 1e-9, "切层保持视图时倍率不变");
        Assert.AreEqual(pan.X, preview.PanOffset.X, 1e-6);
        Assert.AreEqual(pan.Y, preview.PanOffset.Y, 1e-6);

        preview.LoadFile(dxf, keepView: false);

        Assert.AreEqual(1d, preview.Zoom, 1e-9);
        Assert.AreEqual(0d, preview.PanOffset.X, 1e-9);
        Assert.AreEqual(0d, preview.PanOffset.Y, 1e-9);
    }

    [TestMethod]
    public void EmptyCanvasReportsNoContentAndIgnoresWheel()
    {
        using var preview = new DxfPreviewControl();

        Assert.IsFalse(preview.HasContent);
        Assert.AreEqual(GrayscalePreviewWheelMode.Auto, preview.WheelMode);
    }

    [TestMethod]
    public void LoadedFileReportsContentAndRaisesViewChanged()
    {
        var dxf = Path.Combine(_root, "signal.dxf");
        WriteDxf(dxf, (0d, 0d, 10d, 10d));
        using var preview = new DxfPreviewControl();
        var raised = 0;
        preview.ViewChanged += (_, _) => raised++;

        preview.LoadFile(dxf);

        Assert.IsTrue(preview.HasContent);
        Assert.IsTrue(raised > 0, "载入内容后宿主要能刷新缩放读数");
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
