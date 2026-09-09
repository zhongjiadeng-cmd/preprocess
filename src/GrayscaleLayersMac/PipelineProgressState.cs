namespace GrayscaleLayersMac;

internal enum PipelineProgressStage
{
    Starting,
    Grayscale,
    Dxf,
    Machine,
    LaserPmt,
    Succeeded,
    Cancelled,
    Failed
}

internal sealed record PipelineProgressState(
    PipelineProgressStage Stage,
    int Current,
    int? Total,
    string? CurrentFileName,
    string Message,
    string CounterText) : IProgressOverlayState
{
    object IProgressOverlayState.StageKey => Stage;
    public bool IsTerminal =>
        Stage is PipelineProgressStage.Succeeded or
            PipelineProgressStage.Cancelled or
            PipelineProgressStage.Failed;
    public bool IsSuccess => Stage == PipelineProgressStage.Succeeded;
    public bool IsError => Stage == PipelineProgressStage.Failed;
    public bool IsCancelled => Stage == PipelineProgressStage.Cancelled;
    public bool IsIndeterminate => Total is null;
    public double? ProgressValue => Total is > 0 ? (double)Current / Total.Value : null;

    public string AutomationText => string.Join("，", new[]
    {
        Message,
        CounterText,
        CurrentFileName is null ? string.Empty : Path.GetFileName(CurrentFileName)
    }.Where(value => value.Length > 0));

    public static PipelineProgressState Starting(bool allSteps) => new(
        PipelineProgressStage.Starting,
        0,
        null,
        null,
        allSteps ? "正在准备执行全部流程…" : "正在准备执行所选步骤…",
        string.Empty);

    public static PipelineProgressState Step(
        PipelineProgressStage stage,
        string message,
        string counter) => new(stage, 0, null, null, message, counter);

    public static PipelineProgressState DxfLayer(
        int current,
        int total,
        string file,
        string counterPrefix)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(current, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        if (current > total)
            throw new ArgumentOutOfRangeException(nameof(current));

        return new(
            PipelineProgressStage.Dxf,
            current,
            total,
            file,
            "正在执行第 2 步：生成 DXF…",
            $"{counterPrefix} · {current}/{total}");
    }

    public static PipelineProgressState Succeeded(string message) =>
        new(PipelineProgressStage.Succeeded, 1, 1, null, message, "流程已完成");

    public static PipelineProgressState Cancelled() =>
        new(PipelineProgressStage.Cancelled, 0, null, null, "操作已取消", string.Empty);

    public static PipelineProgressState Failed(string? file, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(PipelineProgressStage.Failed, 0, null, file, message, "流程失败");
    }
}
