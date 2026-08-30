namespace GrayscaleLayersMac;

internal enum ImportProgressStage
{
    Scanning,
    ValidatingTiff,
    ValidatingDxf,
    LoadingPreview,
    Succeeded,
    Failed
}

internal sealed record ImportProgressState(
    ImportProgressStage Stage,
    int Current,
    int? Total,
    string? CurrentFileName,
    string Message)
{
    public bool IsTerminal => Stage is ImportProgressStage.Succeeded or ImportProgressStage.Failed;
    public bool IsError => Stage == ImportProgressStage.Failed;
    public bool IsIndeterminate => Total is null;
    public double? ProgressValue => Total is > 0 ? (double)Current / Total.Value : null;

    public string CounterText => Stage switch
    {
        ImportProgressStage.ValidatingTiff => $"正在检查分层 TIFF · {Current}/{Total}",
        ImportProgressStage.ValidatingDxf => $"正在检查 DXF · {Current}/{Total}",
        ImportProgressStage.LoadingPreview => $"正在加载预览 · {Current}/{Total}",
        ImportProgressStage.Succeeded => $"已导入 {Current} 个文件",
        _ => string.Empty
    };

    public string AutomationText => string.Join("，", new[]
    {
        Message,
        CounterText,
        CurrentFileName is null ? string.Empty : Path.GetFileName(CurrentFileName)
    }.Where(value => value.Length > 0));

    public static ImportProgressState Scanning(string message) =>
        new(ImportProgressStage.Scanning, 0, null, null, message);

    public static ImportProgressState ValidatingTiff(int current, int total, string file) =>
        Counted(ImportProgressStage.ValidatingTiff, current, total, file, "正在检查分层 TIFF…");

    public static ImportProgressState ValidatingDxf(int current, int total, string file) =>
        Counted(ImportProgressStage.ValidatingDxf, current, total, file, "正在检查 DXF…");

    public static ImportProgressState LoadingPreview(int current, int total, string message) =>
        Counted(ImportProgressStage.LoadingPreview, current, total, null, message);

    public static ImportProgressState Succeeded(int total) =>
        Counted(ImportProgressStage.Succeeded, total, total, null, $"已导入 {total} 个文件");

    public static ImportProgressState Failed(string? file, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(ImportProgressStage.Failed, 0, null, file, message);
    }

    private static ImportProgressState Counted(
        ImportProgressStage stage, int current, int total, string? file, string message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        if (current > total)
            throw new ArgumentOutOfRangeException(nameof(current));
        return new(stage, current, total, file, message);
    }
}
