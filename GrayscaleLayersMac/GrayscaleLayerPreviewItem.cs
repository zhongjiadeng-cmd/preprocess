using Avalonia.Media.Imaging;

namespace GrayscaleLayersMac;

public sealed class GrayscaleLayerPreviewItem : IDisposable
{
    public GrayscaleLayerPreviewItem(string path, int index)
    {
        FilePath = Path.GetFullPath(path);
        DisplayName = $"第 {index:D2} 层 · {Path.GetFileName(path)}";
    }

    public string FilePath { get; }
    public string DisplayName { get; }
    public Bitmap? Thumbnail { get; private set; }
    public byte[]? PreviewPng { get; private set; }
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public string? Error { get; private set; }

    public void SetPreview(
        byte[] previewPng,
        int pixelWidth,
        int pixelHeight,
        Bitmap thumbnail)
    {
        ArgumentNullException.ThrowIfNull(previewPng);
        ArgumentNullException.ThrowIfNull(thumbnail);
        var previous = Thumbnail;
        Thumbnail = thumbnail;
        PreviewPng = previewPng;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Error = null;
        previous?.Dispose();
    }

    public void SetError(string error)
    {
        Error = string.IsNullOrWhiteSpace(error) ? "未知图片读取错误。" : error;
    }

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
        PreviewPng = null;
    }
}
