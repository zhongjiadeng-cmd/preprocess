using System.Text.Json;

namespace GrayscaleLayersMac;

internal sealed class WorkspaceSplitSettings
{
    private const int CurrentVersion = 1;

    public const double DefaultPreviewRatio = 0.58;
    public const double MinimumPreviewRatio = 0.05;
    public const double MaximumPreviewRatio = 0.95;

    private readonly string _path;

    public WorkspaceSplitSettings(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static WorkspaceSplitSettings CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GrayscaleLayersMac");
        return new WorkspaceSplitSettings(Path.Combine(directory, "ui-settings.json"));
    }

    public double LoadPreviewRatio()
    {
        try
        {
            if (!File.Exists(_path))
                return DefaultPreviewRatio;

            var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_path));
            if (document is null ||
                document.Version != CurrentVersion ||
                !IsValidPreviewRatio(document.PreviewRatio))
            {
                return DefaultPreviewRatio;
            }

            return document.PreviewRatio;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return DefaultPreviewRatio;
        }
    }

    public bool TrySavePreviewRatio(double ratio)
    {
        if (!IsValidPreviewRatio(ratio))
            return false;

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(
                new SettingsDocument(CurrentVersion, ratio),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // UI 偏好保存失败不应影响主流程。
                }
            }
        }
    }

    public static bool IsValidPreviewRatio(double ratio) =>
        double.IsFinite(ratio) &&
        ratio >= MinimumPreviewRatio &&
        ratio <= MaximumPreviewRatio;

    private sealed record SettingsDocument(int Version, double PreviewRatio);
}
