using System;
using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class GrayscaleLayerPreviewControllerTests
{
    [TestMethod]
    public void RefreshFiltersAndSortsLayerTiffs()
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "layer_02_gray.tiff"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "layer_01_gray.tiff"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "other.tiff"), [1]);

            using var controller = new GrayscaleLayerPreviewController();
            var items = controller.Refresh(directory);

            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("layer_01_gray.tiff", Path.GetFileName(items[0].FilePath));
            Assert.AreEqual("layer_02_gray.tiff", Path.GetFileName(items[1].FilePath));
            Assert.AreSame(items[0], controller.SelectedItem);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RefreshMissingDirectoryReturnsEmptyAndCanBeCleared()
    {
        using var controller = new GrayscaleLayerPreviewController();

        var items = controller.Refresh(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.AreEqual(0, items.Count);
        Assert.IsNull(controller.SelectedItem);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gray-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
