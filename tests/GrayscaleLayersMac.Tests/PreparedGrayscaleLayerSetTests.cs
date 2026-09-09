using System;
using System.IO;
using System.Threading;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PreparedGrayscaleLayerSetTests
{
    private static HeadlessUnitTestSession? _session;

    [ClassInitialize]
    public static void StartHeadlessSession(TestContext _)
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(App));
    }

    [ClassCleanup]
    public static void StopHeadlessSession() => _session?.Dispose();

    [TestMethod]
    public void DisposeDisposesUncommittedItemThumbnails()
    {
        _session!.Dispatch(() =>
        {
            var item = CreatePreviewItem();
            var thumbnail = item.Thumbnail;

            using (var prepared = new PreparedGrayscaleLayerSet([item]))
            {
            }

            Assert.IsNull(item.Thumbnail);
            Assert.IsNull(item.PreviewPng);
            Assert.ThrowsExactly<ObjectDisposedException>(() => thumbnail!.Save(new MemoryStream()));
        }, CancellationToken.None);
    }

    [TestMethod]
    public void TakeItemsTransfersOwnershipAndMakesLaterDisposalANoOp()
    {
        _session!.Dispatch(() =>
        {
            var item = CreatePreviewItem();
            var thumbnail = item.Thumbnail;
            var prepared = new PreparedGrayscaleLayerSet([item]);

            var taken = prepared.TakeItems();
            prepared.Dispose();

            Assert.AreSame(item, taken[0]);
            Assert.AreSame(thumbnail, item.Thumbnail);
            Assert.IsNotNull(item.PreviewPng);
            item.Dispose();
        }, CancellationToken.None);
    }

    private static GrayscaleLayerPreviewItem CreatePreviewItem()
    {
        var previewPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL0ggAAAABJRU5ErkJggg==");
        using var stream = new MemoryStream(previewPng, writable: false);
        var thumbnail = Bitmap.DecodeToWidth(stream, 120, BitmapInterpolationMode.MediumQuality);
        var item = new GrayscaleLayerPreviewItem("/tmp/new.tiff", 1);
        item.SetPreview(previewPng, 1, 1, thumbnail);
        return item;
    }
}
