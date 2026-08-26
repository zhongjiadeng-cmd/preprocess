using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class TextureImageInspectionTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [TestMethod]
    public void ParseJson_ReturnsMetadataAndPngBytes()
    {
        var value = TextureImageInspection.ParseJson(
            $$"""{"pixel_width":1500,"pixel_height":1500,"dpi_x":1270,"dpi_y":1270,"preview_png_base64":"{{OnePixelPng}}"}""");
        Assert.AreEqual(1500, value.Info.PixelWidth);
        CollectionAssert.AreEqual(new byte[] {137,80,78,71,13,10,26,10}, value.PreviewPng[..8]);
    }

    [TestMethod]
    public void ParseJson_RejectsInvalidOrOversizedPreview()
    {
        var invalid = """{"pixel_width":1,"pixel_height":1,"dpi_x":null,"dpi_y":null,"preview_png_base64":"bad"}""";
        Assert.ThrowsExactly<ArgumentException>(() => TextureImageInspection.ParseJson(invalid));
        var valid = $$"""{"pixel_width":1,"pixel_height":1,"dpi_x":null,"dpi_y":null,"preview_png_base64":"{{OnePixelPng}}"}""";
        Assert.ThrowsExactly<ArgumentException>(() => TextureImageInspection.ParseJson(valid, 8));
    }

    [TestMethod]
    public void ParseJson_RejectsAnOverlongBase64FieldBeforeDecodingIt()
    {
        const string overlongInvalidBase64 = "AAAAAAAAAAAAA";
        var json = $$"""{"pixel_width":1,"pixel_height":1,"dpi_x":null,"dpi_y":null,"preview_png_base64":"{{overlongInvalidBase64}}"}""";

        var error = Assert.ThrowsExactly<ArgumentException>(() =>
            TextureImageInspection.ParseJson(json, 8));

        StringAssert.Contains(error.Message, "过大");
    }

    [TestMethod]
    public void ParseJson_RejectsMaximumPreviewBytesBelowPngSignature()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TextureImageInspection.ParseJson("{}", 7));
    }

    [TestMethod]
    public async Task BoundedTextReader_DrainsInputBeforeRejectingAnOverlongStream()
    {
        using var reader = new TrackingTextReader("0123456789");

        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            BoundedTextReader.ReadToEndAsync(reader, 4));

        StringAssert.Contains(error.Message, "过大");
        Assert.AreEqual(10, reader.CharactersRead);
        Assert.IsTrue(reader.ReachedEndOfStream);
    }

    private sealed class TrackingTextReader(string text) : StringReader(text)
    {
        public int CharactersRead { get; private set; }
        public bool ReachedEndOfStream { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
            if (result.IsCompletedSuccessfully)
            {
                Track(result.Result);
                return result;
            }

            return TrackAsync(result);
        }

        private async ValueTask<int> TrackAsync(ValueTask<int> result)
        {
            var count = await result;
            Track(count);
            return count;
        }

        private void Track(int count)
        {
            CharactersRead += count;
            ReachedEndOfStream |= count == 0;
        }
    }
}
