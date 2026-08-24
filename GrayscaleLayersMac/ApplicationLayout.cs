namespace GrayscaleLayersMac;

internal static class ApplicationLayout
{
    internal static string GetScriptsDirectory(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var macOsDirectory = new DirectoryInfo(normalized);
        var contentsDirectory = macOsDirectory.Parent;
        var appDirectory = contentsDirectory?.Parent;

        if (macOsDirectory.Name == "MacOS" &&
            contentsDirectory?.Name == "Contents" &&
            string.Equals(appDirectory?.Extension, ".app", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(
                normalized, "..", "Resources", "scripts"));
        }

        return normalized;
    }

    internal static string GetScriptPath(string baseDirectory, string scriptName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptName);
        return Path.Combine(GetScriptsDirectory(baseDirectory), scriptName);
    }
}
