namespace GrayscaleLayersMac;

/// <summary>
/// 发现可作为三步流程中间输入的产物文件，并统一执行最小文件系统校验。
/// 解码与格式校验仍由对应的 TIFF/DXF 读取器负责。
/// </summary>
internal static class PipelineArtifactDiscovery
{
    public static string[] FindLayerTiffs(string directory) =>
        FindFiles(
            directory,
            path =>
                Path.GetFileName(path).StartsWith("layer_", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(path), ".tiff", StringComparison.OrdinalIgnoreCase),
            "分层 TIFF",
            "layer_*.tiff");

    public static string[] FindDxfFiles(string directory) =>
        FindFiles(
            directory,
            path => string.Equals(
                Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase),
            "DXF",
            "*.dxf");

    private static string[] FindFiles(
        string directory,
        Func<string, bool> matches,
        string label,
        string expectedPattern)
    {
        var absoluteDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(absoluteDirectory))
            throw new DirectoryNotFoundException($"{label} 文件夹不存在：{absoluteDirectory}");

        var files = Directory
            .EnumerateFiles(absoluteDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(matches)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidDataException(
                $"文件夹中没有找到 {expectedPattern}：{absoluteDirectory}");

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
