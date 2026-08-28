using System;
using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class GrayscaleLayerPreviewControllerTests
{
    [TestMethod]
    public void RefreshPutsSourceTextureAtSlotZeroAndLayersAfterIt()
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "layer_02_gray.tiff"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "layer_01_gray.tiff"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "other.tiff"), [1]);

            using var controller = new GrayscaleLayerPreviewController();
            controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture("/tmp/tex.png"));
            var items = controller.Refresh(directory);

            // 第 0 层是源纹理，1..N 才是分层结果。
            Assert.AreEqual(3, items.Count);
            Assert.IsTrue(items[0].IsSourceTexture);
            Assert.AreEqual("layer_01_gray.tiff", Path.GetFileName(items[1].FilePath));
            Assert.AreEqual("layer_02_gray.tiff", Path.GetFileName(items[2].FilePath));
            Assert.AreEqual(1, items[1].Index);
            Assert.AreEqual(2, items[2].Index);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RefreshSelectsFirstLayerSoResultIsVisibleImmediately()
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "layer_01_gray.tiff"), [1]);

            using var controller = new GrayscaleLayerPreviewController();
            controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture(null));
            controller.Refresh(directory);

            Assert.AreSame(controller.Items[1], controller.SelectedItem);
            Assert.AreEqual(1, controller.SelectedIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void WithoutSourceTextureSlotZeroIsAPlaceholderAndLayersKeepTheirNumbers()
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "layer_01_gray.tiff"), [1]);

            using var controller = new GrayscaleLayerPreviewController();
            var items = controller.Refresh(directory);

            Assert.IsTrue(items[0].IsSourceTexture);
            Assert.IsTrue(items[0].IsPlaceholder);
            Assert.AreEqual(1, items[1].Index);

            // 导入纹理后占位被替换，但分层编号不变——层号不应随纹理导入而跳变。
            controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture(null));
            Assert.IsFalse(controller.Items[0].IsPlaceholder);
            Assert.AreEqual(1, controller.Items[1].Index);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SettingSourceFocusesSlotZeroWhenUserHasNotPickedALayer()
    {
        using var controller = new GrayscaleLayerPreviewController();

        controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture(null));

        Assert.AreEqual(0, controller.SelectedIndex);
        Assert.IsTrue(controller.SelectedItem!.IsSourceTexture);
    }

    [TestMethod]
    public void ClearingSourceKeepsSelectedLayerAtTheSameNumber()
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "layer_01_gray.tiff"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "layer_02_gray.tiff"), [1]);

            using var controller = new GrayscaleLayerPreviewController();
            controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture(null));
            controller.Refresh(directory);
            controller.Select(2);

            controller.SetSource(null);

            // 第 0 层退回占位，但选中的仍是原来那层，层号也不跳。
            Assert.AreEqual("layer_02_gray.tiff", Path.GetFileName(controller.SelectedItem!.FilePath));
            Assert.AreEqual(2, controller.SelectedIndex);
            Assert.AreEqual(2, controller.SelectedItem.Index);
            Assert.IsTrue(controller.Items[0].IsPlaceholder);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void WithoutReservedSlotLayersStartAtZero()
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "layer_01_gray.tiff"), [1]);

            using var controller = new GrayscaleLayerPreviewController(reserveSourceSlot: false);
            var items = controller.Refresh(directory);

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(0, items[0].Index);
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

        // 只有源纹理占位，没有分层。
        Assert.AreEqual(1, items.Count);
        Assert.IsTrue(items[0].IsPlaceholder);
        Assert.IsNull(controller.SelectedItem);

        controller.Clear();
        Assert.AreEqual(1, controller.Items.Count);
        Assert.IsNull(controller.SelectedItem);
    }

    [TestMethod]
    public void ClearDropsLayersButKeepsTheSourceSlot()
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "layer_01_gray.tiff"), [1]);
            var controller = new GrayscaleLayerPreviewController();
            controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture(null));
            controller.Refresh(directory);
            Assert.AreEqual(2, controller.Items.Count);

            controller.Clear();

            Assert.AreEqual(1, controller.Items.Count);
            Assert.IsTrue(controller.Items[0].IsPlaceholder);
            controller.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gray-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
