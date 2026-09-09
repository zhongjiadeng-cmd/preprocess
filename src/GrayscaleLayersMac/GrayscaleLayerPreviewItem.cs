using Avalonia.Media.Imaging;

namespace GrayscaleLayersMac;

/// <summary>
/// 纹理界面里的一层。
///
/// 索引 0 恒为源纹理（未分层前的原图），1..N 为灰度分层结果。把纹理和分层放进同一条
/// 序列，用户才能在同一块画布上逐层对照，而不是在两个标签页之间来回切。
/// </summary>
public sealed class GrayscaleLayerPreviewItem : IDisposable
{
    public GrayscaleLayerPreviewItem(string path, int index)
        : this(path, index, isSourceTexture: false)
    {
    }

    private GrayscaleLayerPreviewItem(
        string? path,
        int index,
        bool isSourceTexture,
        bool isPlaceholder = false)
    {
        FilePath = string.IsNullOrEmpty(path) ? string.Empty : Path.GetFullPath(path);
        IsSourceTexture = isSourceTexture;
        IsPlaceholder = isPlaceholder;
        Index = index;
        DisplayName = BuildDisplayName();
    }

    /// <summary>源纹理项（第 0 层）。</summary>
    public static GrayscaleLayerPreviewItem ForSourceTexture(string? sourcePath, int index = 0)
        => new(sourcePath, index, isSourceTexture: true);

    /// <summary>源纹理尚未导入时用来占住第 0 层的占位项。</summary>
    public static GrayscaleLayerPreviewItem SourcePlaceholder()
        => new(null, 0, isSourceTexture: true, isPlaceholder: true);

    public string FilePath { get; }
    public string DisplayName { get; private set; }
    public int Index { get; private set; }

    /// <summary>为真表示这是第 0 层的源纹理，而不是分层产物。</summary>
    public bool IsSourceTexture { get; }

    /// <summary>为真表示这是"未导入"占位项，没有可渲染的预览。</summary>
    public bool IsPlaceholder { get; }

    /// <summary>
    /// 重新编号。源纹理的导入/清除会改变整条序列的偏移，层号必须跟着走，
    /// 否则同一张分层图会在导入纹理前后显示成不同的层号。
    /// </summary>
    public void Reindex(int index)
    {
        if (IsPlaceholder)
            return;
        Index = index;
        DisplayName = BuildDisplayName();
    }

    private string BuildDisplayName() => IsPlaceholder
        ? "第 00 层 · 源纹理（未导入）"
        : IsSourceTexture
            ? $"第 {Index:D2} 层 · 源纹理"
            : $"第 {Index:D2} 层 · {Path.GetFileName(FilePath)}";

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
