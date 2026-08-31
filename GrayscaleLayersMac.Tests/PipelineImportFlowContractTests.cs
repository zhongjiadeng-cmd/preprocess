using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
        Assert.AreEqual("dxf", calls.VisiblePreview);
        Assert.AreSame(calls.NewDxfBatch, calls.PublishedDxfBatch);
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

    [TestMethod]
    public async Task QueuedSynchronizationContextCannotOverwriteFailureWithOldProgress()
    {
        var calls = new FlowCalls
        {
            Prepare = (_, _, progress, _) =>
            {
                progress.Report(ImportProgressState.ValidatingDxf(2, 2, "/tmp/bad.dxf"));
                throw new InvalidDataException("DXF 无效。");
            }
        };
        var queued = new QueuedSynchronizationContext();
        var previous = SynchronizationContext.Current;
        bool imported;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queued);
            imported = await MainWindow.RunPreparedImportAsync(
                _ => Task.FromResult<PipelineImportSelection?>(MixedSelection()),
                "无法导入文件",
                CreateActions(calls));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        queued.Drain();

        Assert.IsFalse(imported);
        Assert.AreEqual(ImportProgressStage.Failed, calls.VisibleStages.Last());
    }

    [TestMethod]
    public async Task QueuedSynchronizationContextCannotOverwriteSuccessWithLoadingProgress()
    {
        var calls = new FlowCalls();
        var queued = new QueuedSynchronizationContext();
        var previous = SynchronizationContext.Current;
        bool imported;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queued);
            imported = await MainWindow.RunPreparedImportAsync(
                _ => Task.FromResult<PipelineImportSelection?>(MixedSelection()),
                "无法导入文件",
                CreateActions(calls));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        queued.Drain();

        Assert.IsTrue(imported);
        Assert.AreEqual(ImportProgressStage.Succeeded, calls.VisibleStages.Last());
    }

    [TestMethod]
    public async Task DxfPreparationFailureLeavesOldBatchAndViewUntouched()
    {
        var calls = new FlowCalls
        {
            DxfCommitError = new InvalidDataException("无法安装 DXF 预览。")
        };

        var imported = await MainWindow.RunPreparedImportAsync(
            _ => Task.FromResult<PipelineImportSelection?>(MixedSelection()),
            "无法导入文件",
            CreateActions(calls));

        Assert.IsFalse(imported);
        Assert.AreEqual(0, calls.TiffCommitCount,
            "DXF 首层安装失败必须发生在混合批次任何提交之前。");
        Assert.AreEqual(0, calls.DxfCommitCount);
        Assert.AreEqual(0, calls.SuccessCount);
        Assert.AreEqual(1, calls.FailureCount);
        Assert.AreEqual(ImportProgressStage.Failed, calls.VisibleStages.Last());
        Assert.AreEqual("old", calls.VisiblePreview);
        Assert.AreSame(calls.OldDxfBatch, calls.PublishedDxfBatch);
    }

    [TestMethod]
    public async Task TiffCommitFailureDoesNotPublishOrRevealPreparedDxf()
    {
        var calls = new FlowCalls
        {
            TiffCommitError = new InvalidDataException("TIFF 提交失败。")
        };

        var imported = await MainWindow.RunPreparedImportAsync(
            _ => Task.FromResult<PipelineImportSelection?>(MixedSelection()),
            "无法导入文件",
            CreateActions(calls));

        Assert.IsFalse(imported);
        Assert.AreEqual(0, calls.TiffCommitCount);
        Assert.AreEqual(0, calls.DxfCommitCount);
        Assert.AreEqual(0, calls.SuccessCount);
        Assert.AreEqual(1, calls.FailureCount);
        Assert.AreEqual("old", calls.VisiblePreview);
        Assert.AreSame(calls.OldDxfBatch, calls.PublishedDxfBatch);
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
            if (calls.TiffCommitError is not null)
                throw calls.TiffCommitError;
            calls.TiffCommitCount++;
            calls.CommitAndSuccessOrder.Add("tiff");
            calls.VisiblePreview = "texture";
        },
        (_, _) =>
        {
            if (calls.DxfCommitError is not null)
                throw calls.DxfCommitError;
            return () =>
            {
                calls.DxfCommitCount++;
                calls.CommitAndSuccessOrder.Add("dxf");
                calls.PublishedDxfBatch = calls.NewDxfBatch;
                calls.VisiblePreview = "dxf";
            };
        },
        state =>
        {
            calls.ShowCount++;
            calls.VisibleStages.Add(state.Stage);
        },
        state => calls.VisibleStages.Add(state.Stage),
        (state, _) =>
        {
            calls.SuccessCount++;
            calls.CommitAndSuccessOrder.Add("success");
            calls.VisibleStages.Add(state.Stage);
            return Task.CompletedTask;
        },
        state =>
        {
            calls.FailureCount++;
            calls.VisibleStages.Add(state.Stage);
        },
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
            Task<PreparedPipelineImport>> Prepare
        { get; init; } =
            (tiffs, dxfs, _, _) => Task.FromResult(new PreparedPipelineImport(
                tiffs.Length == 0
                    ? []
                    : [new KeyValuePair<string, TextureImageInspection>(
                        tiffs[0],
                        new TextureImageInspection(
                            new TextureImageInfo(1, 1, null, null),
                            [137, 80, 78, 71]))],
                dxfs.Select(FakeDxfPreview).ToArray()));

        public int ShowCount { get; set; }
        public int PrepareCount { get; set; }
        public int TiffCommitCount { get; set; }
        public int DxfCommitCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int MessageCount { get; set; }
        public Exception? DxfCommitError { get; init; }
        public Exception? TiffCommitError { get; init; }
        public object OldDxfBatch { get; } = new();
        public object NewDxfBatch { get; } = new();
        public object PublishedDxfBatch { get; set; }
        public string VisiblePreview { get; set; } = "old";
        public List<string> CommitAndSuccessOrder { get; } = [];
        public List<string> Logs { get; } = [];
        public List<ImportProgressStage> VisibleStages { get; } = [];

        public FlowCalls()
        {
            PublishedDxfBatch = OldDxfBatch;
        }
    }

    private static DxfPreviewControl.PreparedDxfPreview FakeDxfPreview(string path) => new(
        path,
        1,
        new Rect(0, 0, 1, 1),
        [new DxfPreviewControl.Segment(0, 0, 0, 1, 1, 0, 0, false)],
        0,
        0,
        $"{Path.GetFileName(path)} · 1 条 LINE");

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = [];

        public override void Post(SendOrPostCallback d, object? state) =>
            _callbacks.Enqueue((d, state));

        public void Drain()
        {
            while (_callbacks.TryDequeue(out var callback))
                callback.Callback(callback.State);
        }
    }
}
