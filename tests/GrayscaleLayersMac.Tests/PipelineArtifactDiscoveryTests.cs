using System;
using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PipelineArtifactDiscoveryTests
{
    [TestMethod]
    public void LayerDiscoveryReturnsOnlyNonEmptyLayerTiffsInStableOrder()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(directory.File("layer_02_gray.tiff"), [2]);
        File.WriteAllBytes(directory.File("layer_01_gray.TIFF"), [1]);
        File.WriteAllBytes(directory.File("preview.tiff"), [3]);
        File.WriteAllBytes(directory.File("layer_03_gray.tif"), [4]);

        var files = PipelineArtifactDiscovery.FindLayerTiffs(directory.Path);

        CollectionAssert.AreEqual(
            new[]
            {
                directory.File("layer_01_gray.TIFF"),
                directory.File("layer_02_gray.tiff")
            },
            files);
    }

    [TestMethod]
    public void DxfDiscoveryReturnsOnlyNonEmptyDxfFilesInStableOrder()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(directory.File("layer_02.DXF"), [2]);
        File.WriteAllBytes(directory.File("layer_01.dxf"), [1]);
        File.WriteAllBytes(directory.File("layer_03.txt"), [3]);

        var files = PipelineArtifactDiscovery.FindDxfFiles(directory.Path);

        CollectionAssert.AreEqual(
            new[]
            {
                directory.File("layer_01.dxf"),
                directory.File("layer_02.DXF")
            },
            files);
    }

    [TestMethod]
    public void MissingOrEmptyArtifactDirectoryIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var missing = directory.File("missing");

        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => PipelineArtifactDiscovery.FindLayerTiffs(missing));
        Assert.ThrowsExactly<InvalidDataException>(
            () => PipelineArtifactDiscovery.FindLayerTiffs(directory.Path));
        Assert.ThrowsExactly<InvalidDataException>(
            () => PipelineArtifactDiscovery.FindDxfFiles(directory.Path));
    }

    [TestMethod]
    public void EmptyMatchingArtifactRejectsTheWholeFolder()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(directory.File("layer_01.tiff"), [1]);
        File.WriteAllBytes(directory.File("layer_02.tiff"), []);

        var error = Assert.ThrowsExactly<InvalidDataException>(
            () => PipelineArtifactDiscovery.FindLayerTiffs(directory.Path));

        StringAssert.Contains(error.Message, "layer_02.tiff");
    }

    [TestMethod]
    public void MatchingSymbolicLinkIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var target = directory.File("target.dxf");
        File.WriteAllBytes(target, [1]);
        var link = directory.File("layer_01.dxf");
        File.CreateSymbolicLink(link, target);

        var error = Assert.ThrowsExactly<InvalidDataException>(
            () => PipelineArtifactDiscovery.FindDxfFiles(directory.Path));

        StringAssert.Contains(error.Message, "layer_01.dxf");
    }

    [TestMethod]
    public void QuietScanReturnsEmptyInsteadOfThrowing()
    {
        using var directory = new TemporaryDirectory();
        var missing = directory.File("missing");

        // 静默扫描供"按类型自动路由"使用：目录不存在或没有匹配都不该中断导入。
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            PipelineArtifactDiscovery.FindLayerTiffsOrEmpty(missing));
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            PipelineArtifactDiscovery.FindDxfFilesOrEmpty(missing));
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            PipelineArtifactDiscovery.FindLayerTiffsOrEmpty(directory.Path));
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            PipelineArtifactDiscovery.FindDxfFilesOrEmpty(directory.Path));
    }

    [TestMethod]
    public void QuietScanRoutesTiffsAndDxfsFromTheSameFolder()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(directory.File("layer_01_gray.tiff"), [1]);
        File.WriteAllBytes(directory.File("layer_02_gray.tiff"), [2]);
        File.WriteAllBytes(directory.File("part_01.dxf"), [3]);
        File.WriteAllBytes(directory.File("notes.txt"), [4]);

        CollectionAssert.AreEqual(
            new[]
            {
                directory.File("layer_01_gray.tiff"),
                directory.File("layer_02_gray.tiff")
            },
            PipelineArtifactDiscovery.FindLayerTiffsOrEmpty(directory.Path));
        CollectionAssert.AreEqual(
            new[] { directory.File("part_01.dxf") },
            PipelineArtifactDiscovery.FindDxfFilesOrEmpty(directory.Path));
    }

    [TestMethod]
    public void TypePredicatesClassifyByFileNameAndExtension()
    {
        Assert.IsTrue(PipelineArtifactDiscovery.IsLayerTiff("/tmp/layer_01_gray.tiff"));
        Assert.IsTrue(PipelineArtifactDiscovery.IsLayerTiff("/tmp/LAYER_02.TIFF"));
        Assert.IsFalse(PipelineArtifactDiscovery.IsLayerTiff("/tmp/preview.tiff"));
        Assert.IsFalse(PipelineArtifactDiscovery.IsLayerTiff("/tmp/layer_01_gray.tif"));
        Assert.IsFalse(PipelineArtifactDiscovery.IsLayerTiff("/tmp/layer_01.dxf"));

        Assert.IsTrue(PipelineArtifactDiscovery.IsDxf("/tmp/part_01.dxf"));
        Assert.IsTrue(PipelineArtifactDiscovery.IsDxf("/tmp/part_01.DXF"));
        Assert.IsFalse(PipelineArtifactDiscovery.IsDxf("/tmp/part_01.tiff"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
