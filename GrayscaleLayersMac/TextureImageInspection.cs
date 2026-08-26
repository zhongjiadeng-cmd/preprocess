using System.Text.Json;

namespace GrayscaleLayersMac;

public sealed record TextureImageInspection(TextureImageInfo Info, byte[] PreviewPng)
{
    public const int DefaultMaximumPreviewBytes = 64 * 1024 * 1024;
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static TextureImageInspection ParseJson(
        string json,
        int maximumPreviewBytes = DefaultMaximumPreviewBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPreviewBytes, PngSignature.Length);

        var info = TextureImageInfo.ParseJson(json);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("preview_png_base64", out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("图片预览数据缺失。", nameof(json));
        }

        var base64 = element.GetString();
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new ArgumentException("图片预览数据缺失。", nameof(json));
        }

        if (base64.Length > GetMaximumBase64CharacterCount(maximumPreviewBytes))
        {
            throw new ArgumentException("图片预览数据过大。", nameof(json));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException error)
        {
            throw new ArgumentException("图片预览数据不是有效 Base64。", nameof(json), error);
        }

        if (bytes.Length > maximumPreviewBytes)
        {
            throw new ArgumentException("图片预览数据过大。", nameof(json));
        }

        if (bytes.Length < PngSignature.Length ||
            !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new ArgumentException("图片预览数据不是有效 PNG。", nameof(json));
        }

        return new TextureImageInspection(info, bytes);
    }

    public static int GetMaximumBase64CharacterCount(int maximumPreviewBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPreviewBytes, PngSignature.Length);
        return checked((int)(((long)maximumPreviewBytes + 2) / 3 * 4));
    }
}
