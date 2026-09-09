using System.Globalization;
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

    public string FormatMetadata()
    {
        var dpi = HasEmbeddedDpi
            ? $"{DpiX!.Value.ToString("0.###", CultureInfo.InvariantCulture)} × {DpiY!.Value.ToString("0.###", CultureInfo.InvariantCulture)}"
            : "未提供";
        return $"像素：{PixelWidth} × {PixelHeight} px\nDPI：{dpi}";
    }

    public string FormatPhysicalSize(decimal width, decimal height) =>
        $"物理尺寸：{width.ToString("0.###", CultureInfo.InvariantCulture)} × {height.ToString("0.###", CultureInfo.InvariantCulture)} mm";

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

        if (!TryConvertDpi(dpiX.Value, out var decimalDpiX) ||
            !TryConvertDpi(dpiY.Value, out var decimalDpiY))
        {
            error = "DPI 超出可计算范围。";
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
                PixelWidth / decimalDpiX * 25.4m, 3, MidpointRounding.AwayFromZero);
            var calculatedHeight = decimal.Round(
                PixelHeight / decimalDpiY * 25.4m, 3, MidpointRounding.AwayFromZero);

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

    private static bool TryConvertDpi(double dpi, out decimal decimalDpi)
    {
        decimalDpi = default;

        try
        {
            decimalDpi = (decimal)dpi;
            return decimalDpi > 0;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
