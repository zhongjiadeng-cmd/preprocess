using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PipelineImportPreparationTests
{
    [TestMethod]
    public async Task MixedImportReportsOneMonotonicTotal()
    {
        var states = new List<ImportProgressState>();

        var result = await PipelineImportPreparation.PrepareAsync(
            ["/tmp/a.tiff", "/tmp/b.tiff"], ["/tmp/c.dxf"],
            FakeInspectionAsync, _ => { },
            new InlineProgress<ImportProgressState>(states.Add), CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            states.Where(x => x.Stage is ImportProgressStage.ValidatingTiff or ImportProgressStage.ValidatingDxf)
                .Select(x => x.Current).ToArray());
        Assert.IsTrue(states.All(x => x.Total is null or 3));
        Assert.AreEqual(3, result.TotalCount);
        CollectionAssert.AreEqual(
            new[] { "/tmp/a.tiff", "/tmp/b.tiff" },
            result.TiffInspections.Select(pair => pair.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "/tmp/c.dxf" }, result.DxfPaths.ToArray());
    }

    [TestMethod]
    public async Task DxfValidationFailureDoesNotReturnPreparedImportOrAdvanceToLoading()
    {
        var states = new List<ImportProgressState>();

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PipelineImportPreparation.PrepareAsync(
                ["/tmp/a.tiff"], ["/tmp/bad.dxf"],
                FakeInspectionAsync,
                _ => throw new InvalidDataException("DXF 中没有 LINE 实体。"),
                new InlineProgress<ImportProgressState>(states.Add), CancellationToken.None));

        StringAssert.Contains(error.Message, "bad.dxf");
        Assert.IsFalse(states.Any(x => x.Stage is ImportProgressStage.LoadingPreview or ImportProgressStage.Succeeded));
        CollectionAssert.AreEqual(
            new[] { ImportProgressStage.ValidatingTiff, ImportProgressStage.ValidatingDxf },
            states.Select(x => x.Stage).ToArray());
    }

    [TestMethod]
    public async Task TiffFailureIncludesFileNameAndStopsBeforeDxfValidation()
    {
        var states = new List<ImportProgressState>();

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PipelineImportPreparation.PrepareAsync(
                ["/tmp/bad.tiff"], ["/tmp/a.dxf"],
                (_, _) => throw new InvalidDataException("预览无效。"),
                _ => Assert.Fail("DXF should not be validated after a TIFF failure."),
                new InlineProgress<ImportProgressState>(states.Add), CancellationToken.None));

        StringAssert.Contains(error.Message, "bad.tiff");
        CollectionAssert.AreEqual(
            new[] { ImportProgressStage.ValidatingTiff },
            states.Select(x => x.Stage).ToArray());
    }

    private static Task<TextureImageInspection> FakeInspectionAsync(
        string _, CancellationToken __) => Task.FromResult(new TextureImageInspection(
            new TextureImageInfo(1, 1, null, null),
            [137, 80, 78, 71, 13, 10, 26, 10]));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
