using System.Text.Json;

namespace GrayscaleLayersMac;

/// <summary>
/// 界面偏好的本地存储（JSON，原子写入）。
/// 除预览区分栏比例外，还按面板 key 记录每个日志面板上次的折叠状态。
/// </summary>
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

    public double LoadPreviewRatio() => LoadDocument().PreviewRatio;

    public bool TrySavePreviewRatio(double ratio)
    {
        if (!IsValidPreviewRatio(ratio))
            return false;

        return TrySaveDocument(LoadDocument() with { PreviewRatio = ratio });
    }

    /// <summary>读取某个日志面板上次的折叠状态；没有记录时按展开处理。</summary>
    public bool LoadLogCollapsed(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return LoadDocument().LogCollapsed.TryGetValue(key, out var collapsed) && collapsed;
    }

    /// <summary>写入某个日志面板的折叠状态，其余面板的记录与分栏比例保持不变。</summary>
    public bool TrySaveLogCollapsed(string key, bool collapsed)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var document = LoadDocument();
        var states = new Dictionary<string, bool>(document.LogCollapsed, StringComparer.Ordinal)
        {
            [key] = collapsed
        };
        return TrySaveDocument(document with { LogCollapsed = states });
    }

    /// <summary>读取三步流程页的图层缩略图侧栏上次的收起状态，没有记录时按展开处理。</summary>
    public bool LoadThumbnailCollapsed() => LoadDocument().ThumbnailCollapsed;

    /// <summary>写入三步流程页的图层缩略图侧栏的收起状态，分栏比例与日志面板状态保持不变。</summary>
    public bool TrySaveThumbnailCollapsed(bool collapsed) =>
        TrySaveDocument(LoadDocument() with { ThumbnailCollapsed = collapsed });

    public static bool IsValidPreviewRatio(double ratio) =>
        double.IsFinite(ratio) &&
        ratio >= MinimumPreviewRatio &&
        ratio <= MaximumPreviewRatio;

    private static Settings DefaultDocument() =>
        new(
            CurrentVersion,
            DefaultPreviewRatio,
            new Dictionary<string, bool>(StringComparer.Ordinal),
            ThumbnailCollapsed: false);

    private Settings LoadDocument()
    {
        try
        {
            if (!File.Exists(_path))
                return DefaultDocument();

            var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_path));
            if (document is null ||
                document.Version != CurrentVersion ||
                !IsValidPreviewRatio(document.PreviewRatio))
            {
                return DefaultDocument();
            }

            // 老版本文件没有 LogCollapsed / ThumbnailCollapsed 字段，反序列化后可能为 null，
            // 这里统一补齐默认值，让上层拿到的永远是非空结构。
            return new Settings(
                document.Version,
                document.PreviewRatio,
                document.LogCollapsed is null
                    ? new Dictionary<string, bool>(StringComparer.Ordinal)
                    : new Dictionary<string, bool>(document.LogCollapsed, StringComparer.Ordinal),
                document.ThumbnailCollapsed ?? false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return DefaultDocument();
        }
    }

    private bool TrySaveDocument(Settings document)
    {
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
                document,
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

    /// <summary>读进内存并补全默认值后的设置；非集合字段保证非默认值。</summary>
    private sealed record Settings(
        int Version,
        double PreviewRatio,
        Dictionary<string, bool> LogCollapsed,
        bool ThumbnailCollapsed);

    /// <summary>磁盘上的 JSON 形状。LogCollapsed / ThumbnailCollapsed 可空，兼容老版本文件。</summary>
    private sealed record SettingsDocument(
        int Version,
        double PreviewRatio,
        Dictionary<string, bool>? LogCollapsed,
        bool? ThumbnailCollapsed);
}
