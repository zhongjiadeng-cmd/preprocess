using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PipelineImportFlowContractTests
{
    [TestMethod]
    public async Task PickerCancellationNeverShowsOverlay()
    {
        var calls = new FlowCalls();

        var imported = await MainWindow.RunPreparedImportAsync(
            _ => Task.FromResult<PipelineImportSelection?>(null),
            "无法导入文件",
            CreateActions(calls));

        Assert.IsFalse(imported);
        Assert.AreEqual(0, calls.ShowCount);
        Assert.AreEqual(0, calls.PrepareCount);
        Assert.AreEqual(0, calls.MessageCount);
    }

    [TestMethod]
    public async Task MixedValidationFailureCommitsNeitherArtifactType()
    {
        var calls = new FlowCalls
        {
            Prepare = (_, _, progress, _) =>
            {
                progress.Report(ImportProgressState.ValidatingDxf(2, 2, "/tmp/bad.dxf"));
                throw new InvalidDataException("无法读取 DXF bad.dxf：没有 LINE 实体。");
            }
        };

        var imported = await MainWindow.RunPreparedImportAsync(
            _ => Task.FromResult<PipelineImportSelection?>(MixedSelection()),
            "无法导入文件",
            CreateActions(calls));

        Assert.IsFalse(imported);
        Assert.AreEqual(0, calls.TiffCommitCount);
        Assert.AreEqual(0, calls.DxfCommitCount);
        Assert.AreEqual(1, calls.FailureCount);
        Assert.AreEqual(0, calls.MessageCount);
    }

    [TestMethod]
    public async Task SuccessCommitsTiffThenDxfAndPreservesSummaryWording()
    {
        var calls = new FlowCalls();

        var imported = await MainWindow.RunPreparedImportAsync(
            _ => Task.FromResult<PipelineImportSelection?>(MixedSelection()),
            "无法导入文件",
            CreateActions(calls));

        Assert.IsTrue(imported);
        CollectionAssert.AreEqual(
            new[] { "tiff", "dxf", "success" },
            calls.CommitAndSuccessOrder.ToArray());
        Assert.AreEqual(1, calls.TiffCommitCount);
        Assert.AreEqual(1, calls.DxfCommitCount);
        Assert.AreEqual(1, calls.SuccessCount);
        CollectionAssert.AreEqual(
            new[]
            {
                "已导入 2 个文件",
                "分层 TIFF：已导入 1 层。\nDXF：已导入 1 层。",
                ""
            },
            calls.Logs.ToArray());
    }

    [TestMethod]
    public async Task ExpectedFailureUsesOverlayWithoutShowingMessageDialog()
    {
        var calls = new FlowCalls
        {
            Prepare = (_, _, _, _) =>
                throw new InvalidDataException("无法读取分层 TIFF bad.tiff：预览无效。")
        };

        var imported = await MainWindow.RunPreparedImportAsync(
            _ => Task.FromResult<PipelineImportSelection?>(new PipelineImportSelection(
                () => (["/tmp/bad.tiff"], []),
                "已导入 1 个文件",
                "没有可导入文件。",
                "/tmp",
                null)),
            "无法导入文件",
            CreateActions(calls));

        Assert.IsFalse(imported);
        Assert.AreEqual(1, calls.FailureCount);
        Assert.AreEqual(0, calls.MessageCount);
        CollectionAssert.AreEqual(
            new[] { "导入失败：无法读取分层 TIFF bad.tiff：预览无效。" },
            calls.Logs.ToArray());
    }

    private static PipelineImportSelection MixedSelection() => new(
        () => (["/tmp/a.tiff"], ["/tmp/a.dxf"]),
        "已导入 2 个文件",
        "没有可导入文件。",
        "/tmp",
        "/tmp");

    private static PreparedImportFlowActions CreateActions(FlowCalls calls) => new(
        (tiffs, dxfs, progress, cancellationToken) =>
        {
            calls.PrepareCount++;
            return calls.Prepare(tiffs, dxfs, progress, cancellationToken);
        },
        (_, _) => Task.FromResult(new PreparedGrayscaleLayerSet([])),
        (_, _) =>
        {
            calls.TiffCommitCount++;
            calls.CommitAndSuccessOrder.Add("tiff");
        },
        (_, _) =>
        {
            calls.DxfCommitCount++;
            calls.CommitAndSuccessOrder.Add("dxf");
        },
        _ => calls.ShowCount++,
        _ => { },
        (_, _) =>
        {
            calls.SuccessCount++;
            calls.CommitAndSuccessOrder.Add("success");
            return Task.CompletedTask;
        },
        _ => calls.FailureCount++,
        calls.Logs.Add,
        _ =>
        {
            calls.MessageCount++;
            return Task.CompletedTask;
        });

    private sealed class FlowCalls
    {
        public Func<
            string[],
            string[],
            IProgress<ImportProgressState>,
            CancellationToken,
            Task<PreparedPipelineImport>> Prepare { get; init; } =
            (tiffs, dxfs, _, _) => Task.FromResult(new PreparedPipelineImport(
                tiffs.Length == 0
                    ? []
                    : [new KeyValuePair<string, TextureImageInspection>(
                        tiffs[0],
                        new TextureImageInspection(
                            new TextureImageInfo(1, 1, null, null),
                            [137, 80, 78, 71]))],
                dxfs));

        public int ShowCount { get; set; }
        public int PrepareCount { get; set; }
        public int TiffCommitCount { get; set; }
        public int DxfCommitCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int MessageCount { get; set; }
        public List<string> CommitAndSuccessOrder { get; } = [];
        public List<string> Logs { get; } = [];
    }
}
