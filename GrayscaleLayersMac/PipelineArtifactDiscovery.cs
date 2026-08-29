namespace GrayscaleLayersMac;

/// <summary>
/// 发现可作为三步流程中间输入的产物文件，并统一执行最小文件系统校验。
/// 解码与格式校验仍由对应的 TIFF/DXF 读取器负责。
/// </summary>
internal static class PipelineArtifactDiscovery
{
    public static string[] FindLayerTiffs(string directory) =>
        FindFiles(directory, IsLayerTiff, "分层 TIFF", "layer_*.tiff");

    public static string[] FindDxfFiles(string directory) =>
        FindFiles(directory, IsDxf, "DXF", "*.dxf");

    /// <summary>判断一个路径是否为分层 TIFF（layer_*.tiff）。</summary>
    public static bool IsLayerTiff(string path) =>
        Path.GetFileName(path).StartsWith("layer_", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetExtension(path), ".tiff", StringComparison.OrdinalIgnoreCase);

    /// <summary>判断一个路径是否为 DXF 文件。</summary>
    public static bool IsDxf(string path) =>
        string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 静默扫描：目录不存在或没有匹配文件时返回空数组，不抛异常。
    /// 供"按类型自动路由"的导入入口使用——同一个文件夹里可能既有 TIFF 也有 DXF，
    /// 任何一类缺失都不应中断另一类的导入。文件本身无效（不是常规非空文件）仍然抛错。
    /// </summary>
    public static string[] FindLayerTiffsOrEmpty(string directory) =>
        ScanFiles(directory, IsLayerTiff, "分层 TIFF");

    /// <inheritdoc cref="FindLayerTiffsOrEmpty"/>
    public static string[] FindDxfFilesOrEmpty(string directory) =>
        ScanFiles(directory, IsDxf, "DXF");

    private static string[] FindFiles(
        string directory,
        Func<string, bool> matches,
        string label,
        string expectedPattern)
    {
        var absoluteDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(absoluteDirectory))
            throw new DirectoryNotFoundException($"{label} 文件夹不存在：{absoluteDirectory}");

        var files = ScanFiles(directory, matches, label);
        if (files.Length == 0)
            throw new InvalidDataException(
                $"文件夹中没有找到 {expectedPattern}：{absoluteDirectory}");

        return files;
    }

    private static string[] ScanFiles(
        string directory,
        Func<string, bool> matches,
        string label)
    {
        var absoluteDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(absoluteDirectory))
            return [];

        var files = Directory
            .EnumerateFiles(absoluteDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(matches)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in files)
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists ||
                (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                file.Length == 0)
            {
                throw new InvalidDataException(
                    $"{label} 文件必须是常规非空文件：{Path.GetFileName(path)}");
            }
        }

        return files;
    }
}
