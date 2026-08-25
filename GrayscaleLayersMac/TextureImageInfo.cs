using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrayscaleLayersMac;

public sealed record TextureImageInfo(
    [property: JsonPropertyName("pixel_width")] int PixelWidth,
    [property: JsonPropertyName("pixel_height")] int PixelHeight,
    [property: JsonPropertyName("dpi_x")] double? DpiX,
    [property: JsonPropertyName("dpi_y")] double? DpiY)
{
    public bool HasEmbeddedDpi => DpiX.HasValue && DpiY.HasValue;

    public static TextureImageInfo ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var info = JsonSerializer.Deserialize<TextureImageInfo>(json)
            ?? throw new ArgumentException("图片信息不能为空。", nameof(json));

        if (info.PixelWidth <= 0 || info.PixelHeight <= 0)
        {
            throw new ArgumentException("像素尺寸必须为正数。", nameof(json));
        }

        if (info.DpiX.HasValue != info.DpiY.HasValue)
        {
            throw new ArgumentException("内置 DPI 必须同时包含两个轴。", nameof(json));
        }

        if (info.HasEmbeddedDpi && (!IsValidDpi(info.DpiX!.Value) || !IsValidDpi(info.DpiY!.Value)))
        {
            throw new ArgumentException("内置 DPI 必须是有限的正数。", nameof(json));
        }

        return info;
    }

    public bool TryCalculateMillimeters(
        double? fallbackDpi,
        decimal minimum,
        decimal maximum,
        out decimal width,
        out decimal height,
        out string error)
    {
        width = default;
        height = default;
        error = string.Empty;

        if (PixelWidth <= 0 || PixelHeight <= 0)
        {
            error = "像素尺寸必须为正数。";
            return false;
        }

        if (DpiX.HasValue != DpiY.HasValue)
        {
            error = "内置 DPI 必须同时包含两个轴。";
            return false;
        }

        var dpiX = HasEmbeddedDpi ? DpiX!.Value : fallbackDpi;
        var dpiY = HasEmbeddedDpi ? DpiY!.Value : fallbackDpi;
        if (!dpiX.HasValue || !dpiY.HasValue || !IsValidDpi(dpiX.Value) || !IsValidDpi(dpiY.Value))
        {
            error = "DPI 必须是有限的正数。";
            return false;
        }

        if (minimum > maximum)
        {
            error = "尺寸允许范围无效。";
            return false;
        }

        try
        {
            var calculatedWidth = decimal.Round(
                PixelWidth / (decimal)dpiX.Value * 25.4m, 3, MidpointRounding.AwayFromZero);
            var calculatedHeight = decimal.Round(
                PixelHeight / (decimal)dpiY.Value * 25.4m, 3, MidpointRounding.AwayFromZero);

            if (calculatedWidth < minimum || calculatedWidth > maximum ||
                calculatedHeight < minimum || calculatedHeight > maximum)
            {
                error = $"计算尺寸超出允许范围 [{minimum}, {maximum}]。";
                return false;
            }

            width = calculatedWidth;
            height = calculatedHeight;
            return true;
        }
        catch (OverflowException)
        {
            error = "计算尺寸超出允许范围。";
            return false;
        }
    }

    private static bool IsValidDpi(double dpi) => double.IsFinite(dpi) && dpi > 0;
}
