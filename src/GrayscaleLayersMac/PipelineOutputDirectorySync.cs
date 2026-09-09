namespace GrayscaleLayersMac;

/// <summary>
/// 三步流程里两个输出目录的联动规则：
/// <b>DXF 目录默认跟随分层 TIFF 目录</b>，用户一旦把 DXF 目录改成别处
/// （不再是上一次跟随写入的那个值），之后就不再自动覆盖，改由用户全权决定。
/// </summary>
internal static class PipelineOutputDirectorySync
{
    /// <summary>
    /// 判断分层目录变化后，DXF 目录是否还可以被自动同步。
    /// 只有「DXF 目录为空」或「DXF 目录仍是上一次自动同步的值」时才可以。
    /// </summary>
    /// <param name="dxfOutputPath">DXF 目录输入框里的当前值。</param>
    /// <param name="lastSyncedPath">上一次由分层目录自动同步过去的路径；从未同步过则为 null。</param>
    public static bool ShouldFollowLayerDirectory(string? dxfOutputPath, string? lastSyncedPath)
    {
        if (string.IsNullOrWhiteSpace(dxfOutputPath))
            return true;
        return lastSyncedPath is not null && PathsEqual(dxfOutputPath, lastSyncedPath);
    }

    /// <summary>
    /// 目录路径比较：忽略首尾空白、结尾的分隔符与大小写（macOS/Windows 的卷默认都不区分大小写）。
    /// 任一侧为空视为不相等——空路径没有可比的对象。
    /// </summary>
    public static bool PathsEqual(string? left, string? right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        if (string.IsNullOrEmpty(normalizedLeft) || string.IsNullOrEmpty(normalizedRight))
            return false;
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var trimmed = path.Trim();
        if (trimmed.Length > 1)
            trimmed = trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return string.Empty;
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
        catch (NotSupportedException)
        {
            return trimmed;
        }
        catch (PathTooLongException)
        {
            return trimmed;
        }
    }
}
