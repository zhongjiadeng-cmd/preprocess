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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
