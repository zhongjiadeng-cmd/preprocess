using System.Text.Json;

namespace GrayscaleLayersMac;

public sealed record DxfTextureRegistration
{
    public const string ProcessOutputPrefix = "PREVIEW_REGISTRATION_JSON:";

    public double FrameWidthMm { get; }
    public double FrameHeightMm { get; }
    public double PixelWidthMm { get; }
    public double PixelHeightMm { get; }
    public int PixelColumns { get; }
    public int PixelRows { get; }

    public double RasterLeftMm => -FrameWidthMm / 2;
    public double RasterTopMm => FrameHeightMm / 2;
    public double RasterRightMm => RasterLeftMm + PixelColumns * PixelWidthMm;
    public double RasterBottomMm => RasterTopMm - PixelRows * PixelHeightMm;

    public DxfTextureRegistration(
        double frameWidthMm,
        double frameHeightMm,
        double pixelWidthMm,
        double pixelHeightMm,
        int pixelColumns,
        int pixelRows)
    {
        if (!double.IsFinite(frameWidthMm) || frameWidthMm <= 0 ||
            !double.IsFinite(frameHeightMm) || frameHeightMm <= 0 ||
            !double.IsFinite(pixelWidthMm) || pixelWidthMm <= 0 ||
            !double.IsFinite(pixelHeightMm) || pixelHeightMm <= 0 ||
            pixelColumns <= 0 || pixelRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidthMm));
        }

        FrameWidthMm = frameWidthMm;
        FrameHeightMm = frameHeightMm;
        PixelWidthMm = pixelWidthMm;
        PixelHeightMm = pixelHeightMm;
        PixelColumns = pixelColumns;
        PixelRows = pixelRows;
    }

    public static bool TryParseProcessOutput(
        string line,
        out DxfTextureRegistration? registration)
    {
        registration = null;
        if (!line.StartsWith(ProcessOutputPrefix, StringComparison.Ordinal))
            return false;

        try
        {
            using var document = JsonDocument.Parse(line[ProcessOutputPrefix.Length..]);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
                return false;
            registration = new DxfTextureRegistration(
                root.GetProperty("target_width_mm").GetDouble(),
                root.GetProperty("target_height_mm").GetDouble(),
                root.GetProperty("pixel_width_mm").GetDouble(),
                root.GetProperty("pixel_height_mm").GetDouble(),
                root.GetProperty("pixel_columns").GetInt32(),
                root.GetProperty("pixel_rows").GetInt32());
            return true;
        }
        catch (Exception error) when (
            error is JsonException or InvalidOperationException or
            KeyNotFoundException or ArgumentOutOfRangeException or FormatException)
        {
            registration = null;
            return false;
        }
    }
}
