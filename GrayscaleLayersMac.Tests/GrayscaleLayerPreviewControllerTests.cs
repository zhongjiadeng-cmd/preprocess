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
    public void RefreshFilesUsesTheGivenOrderAndDoesNotRequireTheLayerNaming()
    {
        var directory = CreateDirectory();
        try
        {
            var first = Path.Combine(directory, "part_a.tiff");
            var second = Path.Combine(directory, "part_b.tiff");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [1]);
            File.WriteAllBytes(Path.Combine(directory, "ignored.tiff"), [1]);

            using var controller = new GrayscaleLayerPreviewController();
            controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture(null));

            // 手动选中的分层 TIFF 不必叫 layer_*.tiff；排序结果即层序，
            // 没有列进来的同目录文件不受影响。
            var items = controller.RefreshFiles([second, first]);

            Assert.AreEqual(3, items.Count);
            Assert.IsTrue(items[0].IsSourceTexture);
            Assert.AreEqual("part_a.tiff", Path.GetFileName(items[1].FilePath));
            Assert.AreEqual("part_b.tiff", Path.GetFileName(items[2].FilePath));
            Assert.AreEqual(1, items[1].Index);
            Assert.AreEqual(2, items[2].Index);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RefreshFilesReplacesPreviousLayersAndSkipsMissingFiles()
    {
        var directory = CreateDirectory();
        try
        {
            var dropped = Path.Combine(directory, "layer_01_gray.tiff");
            var kept = Path.Combine(directory, "layer_02_gray.tiff");
            File.WriteAllBytes(dropped, [1]);
            File.WriteAllBytes(kept, [1]);

            using var controller = new GrayscaleLayerPreviewController();
            controller.Refresh(directory);
            Assert.AreEqual(3, controller.Items.Count);

            // 与文件夹导入同一套语义：整体替换，且不存在的文件直接跳过。
            controller.RefreshFiles([kept, Path.Combine(directory, "gone.tiff")]);

            Assert.AreEqual(2, controller.Items.Count);
            Assert.AreEqual("layer_02_gray.tiff", Path.GetFileName(controller.Items[1].FilePath));
            Assert.AreEqual(1, controller.Items[1].Index);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceLayersDoesNotChangeTheSourceSlotAndSelectsFirstNewLayer()
    {
        using var controller = new GrayscaleLayerPreviewController();
        controller.SetSource(GrayscaleLayerPreviewItem.ForSourceTexture("/tmp/source.png"));
        var replacement = new[] { new GrayscaleLayerPreviewItem("/tmp/new.tiff", 1) };

        controller.ReplaceLayers(replacement);

        Assert.AreEqual("source.png", Path.GetFileName(controller.Items[0].FilePath));
        Assert.AreSame(replacement[0], controller.SelectedItem);
    }

    [TestMethod]
    public void ReplaceLayersValidationFailureLeavesVisibleLayersUnchanged()
    {
        using var controller = new GrayscaleLayerPreviewController();
        var existing = new GrayscaleLayerPreviewItem("/tmp/existing.tiff", 1);
        controller.ReplaceLayers([existing]);
        var invalidSource = GrayscaleLayerPreviewItem.ForSourceTexture("/tmp/invalid.png");

        try
        {
            Assert.ThrowsExactly<ArgumentException>(() => controller.ReplaceLayers([invalidSource]));

            Assert.AreSame(existing, controller.SelectedItem);
            Assert.AreSame(existing, controller.Items[1]);
        }
        finally
        {
            invalidSource.Dispose();
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
