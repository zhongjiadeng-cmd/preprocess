using System.Globalization;

namespace GrayscaleLayersMac;

public enum TexturePreviewPhase
{
    Empty,
    Loading,
    Ready,
    Failed,
    Closed
}

public enum TexturePreviewAutoSizeTrigger
{
    ImageImport,
    FallbackDpiEdit
}

public enum TexturePreviewDecodeAxis
{
    Width,
    Height
}

public sealed record TexturePreviewState(
    TexturePreviewPhase Phase,
    string MetadataText,
    string PhysicalSizeText)
{
    public static TexturePreviewState Empty { get; } = new(
        TexturePreviewPhase.Empty,
        "尚未选择图片",
        "物理尺寸：等待读取图片信息");

    public static TexturePreviewState Loading { get; } = new(
        TexturePreviewPhase.Loading,
        "正在读取图片信息…",
        string.Empty);

    public static TexturePreviewState Closed { get; } = new(
        TexturePreviewPhase.Closed,
        string.Empty,
        string.Empty);
}

public readonly record struct TexturePreviewOperation(
    long RequestId,
    CancellationToken CancellationToken);

public readonly record struct TexturePreviewDecodeConstraint(
    TexturePreviewDecodeAxis Axis,
    int PixelLimit);

public readonly record struct TexturePreviewSizeUpdate(
    bool ShouldWriteTargets,
    decimal Width,
    decimal Height,
    string? PhysicalSizeText)
{
    public static TexturePreviewSizeUpdate None { get; } = new(
        false,
        default,
        default,
        null);

    public static TexturePreviewSizeUpdate Preserve(string physicalSizeText) => new(
        false,
        default,
        default,
        physicalSizeText);

    public static TexturePreviewSizeUpdate Write(
        decimal width,
        decimal height,
        string physicalSizeText) => new(
            true,
            width,
            height,
            physicalSizeText);
}

public static class TextureFallbackDpi
{
    public static bool TryParseOptional(string? text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim();
        if (!double.TryParse(
                trimmed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            !double.TryParse(
                trimmed,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out parsed))
        {
            return false;
        }

        if (!double.IsFinite(parsed) || parsed <= 0)
            return false;

        value = parsed;
        return true;
    }
}

public static class TexturePreviewDecodePolicy
{
    public static TexturePreviewDecodeConstraint Select(
        TextureImageInfo info,
        int pixelLimit)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.PixelWidth <= 0 || info.PixelHeight <= 0)
            throw new ArgumentException("像素尺寸必须为正数。", nameof(info));
        if (pixelLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelLimit));

        return new TexturePreviewDecodeConstraint(
            info.PixelWidth >= info.PixelHeight
                ? TexturePreviewDecodeAxis.Width
                : TexturePreviewDecodeAxis.Height,
            pixelLimit);
    }
}

public sealed class TexturePreviewController : IDisposable
{
    private const string WaitingForDpi = "物理尺寸：等待填写有效 DPI";
    private const string StableFailureSummary =
        "无法读取图片。请确认文件未损坏且格式受支持，然后重试。";

    private readonly object _sync = new();
    private readonly Action<IDisposable?> _displayPreview;
    private readonly Action<TexturePreviewSizeUpdate> _writeTargets;
    private long _nextRequestId;
    private long _activeRequestId;
    private CancellationTokenSource? _activeCancellation;
    private IDisposable? _ownedPreview;
    private TextureImageInfo? _currentInfo;
    private TexturePreviewState _state = TexturePreviewState.Empty;
    private bool _isClosed;

    public TexturePreviewController(
        Action<IDisposable?> displayPreview,
        Action<TexturePreviewSizeUpdate> writeTargets)
    {
        _displayPreview = displayPreview ?? throw new ArgumentNullException(nameof(displayPreview));
        _writeTargets = writeTargets ?? throw new ArgumentNullException(nameof(writeTargets));
    }

    public TexturePreviewState State
    {
        get
        {
            lock (_sync)
                return _state;
        }
    }

    public TextureImageInfo? CurrentInfo
    {
        get
        {
            lock (_sync)
                return _currentInfo;
        }
    }

    public TexturePreviewOperation BeginImport()
    {
        CancellationTokenSource? superseded;
        TexturePreviewOperation operation;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isClosed, this);

            superseded = _activeCancellation;
            var cancellation = new CancellationTokenSource();
            _activeCancellation = cancellation;
            _activeRequestId = ++_nextRequestId;
            ClearOwnedPreview();
            _currentInfo = null;
            _state = TexturePreviewState.Loading;
            operation = new TexturePreviewOperation(
                _activeRequestId,
                cancellation.Token);
        }

        CancelAndDispose(superseded);
        return operation;
    }

    public bool TryCompleteImport(
        TexturePreviewOperation operation,
        IDisposable preview,
        TextureImageInfo info,
        string? fallbackDpiText,
        decimal minimum,
        decimal maximum,
        out TexturePreviewSizeUpdate sizeUpdate)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(info);

        var calculatedUpdate = EvaluateAutoSize(
            TexturePreviewAutoSizeTrigger.ImageImport,
            info,
            fallbackDpiText,
            minimum,
            maximum);
        CancellationTokenSource? completedCancellation = null;

        lock (_sync)
        {
            if (!IsCurrent(operation))
            {
                preview.Dispose();
                sizeUpdate = TexturePreviewSizeUpdate.None;
                return false;
            }

            try
            {
                _displayPreview(preview);
            }
            catch
            {
                preview.Dispose();
                throw;
            }

            var previous = _ownedPreview;
            _ownedPreview = preview;
            _currentInfo = info;
            _state = new TexturePreviewState(
                TexturePreviewPhase.Ready,
                info.FormatMetadata(),
                calculatedUpdate.PhysicalSizeText ?? string.Empty);
            completedCancellation = CompleteActiveOperation();
            previous?.Dispose();
            sizeUpdate = calculatedUpdate;
        }

        completedCancellation?.Dispose();
        if (sizeUpdate.ShouldWriteTargets)
            _writeTargets(sizeUpdate);
        return true;
    }

    public TexturePreviewSizeUpdate ApplyFallbackDpiEdit(
        string? fallbackDpiText,
        decimal minimum,
        decimal maximum)
    {
        TexturePreviewSizeUpdate update;
        lock (_sync)
        {
            if (_isClosed || _currentInfo is null)
                return TexturePreviewSizeUpdate.None;

            update = EvaluateAutoSize(
                TexturePreviewAutoSizeTrigger.FallbackDpiEdit,
                _currentInfo,
                fallbackDpiText,
                minimum,
                maximum);
            if (update.PhysicalSizeText is not null)
                _state = _state with { PhysicalSizeText = update.PhysicalSizeText };
        }

        if (update.ShouldWriteTargets)
            _writeTargets(update);
        return update;
    }

    public bool TryFail(TexturePreviewOperation operation, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        CancellationTokenSource? completedCancellation;

        lock (_sync)
        {
            if (!IsCurrent(operation))
                return false;

            _state = new TexturePreviewState(
                TexturePreviewPhase.Failed,
                StableFailureSummary,
                string.Empty);
            completedCancellation = CompleteActiveOperation();
        }

        completedCancellation?.Dispose();
        return true;
    }

    public void Close()
    {
        CancellationTokenSource? activeCancellation;
        IDisposable? ownedPreview;

        lock (_sync)
        {
            if (_isClosed)
                return;

            _isClosed = true;
            activeCancellation = CompleteActiveOperation();
            _displayPreview(null);
            ownedPreview = _ownedPreview;
            _ownedPreview = null;
            _currentInfo = null;
            _state = TexturePreviewState.Closed;
        }

        CancelAndDispose(activeCancellation);
        ownedPreview?.Dispose();
    }

    public void Dispose() => Close();

    private bool IsCurrent(TexturePreviewOperation operation) =>
        !_isClosed &&
        _activeCancellation is not null &&
        operation.RequestId == _activeRequestId;

    private CancellationTokenSource? CompleteActiveOperation()
    {
        var completed = _activeCancellation;
        _activeCancellation = null;
        _activeRequestId = 0;
        return completed;
    }

    private void ClearOwnedPreview()
    {
        _displayPreview(null);
        var previous = _ownedPreview;
        _ownedPreview = null;
        previous?.Dispose();
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static TexturePreviewSizeUpdate EvaluateAutoSize(
        TexturePreviewAutoSizeTrigger trigger,
        TextureImageInfo info,
        string? fallbackDpiText,
        decimal minimum,
        decimal maximum)
    {
        if (trigger == TexturePreviewAutoSizeTrigger.ImageImport && !info.HasEmbeddedDpi)
            return TexturePreviewSizeUpdate.Preserve(WaitingForDpi);

        if (trigger == TexturePreviewAutoSizeTrigger.FallbackDpiEdit && info.HasEmbeddedDpi)
            return TexturePreviewSizeUpdate.None;

        double? fallbackDpi = null;
        if (!info.HasEmbeddedDpi &&
            (!TextureFallbackDpi.TryParseOptional(fallbackDpiText, out fallbackDpi) ||
             !fallbackDpi.HasValue))
        {
            return TexturePreviewSizeUpdate.Preserve(WaitingForDpi);
        }

        if (!info.TryCalculateMillimeters(
                fallbackDpi,
                minimum,
                maximum,
                out var width,
                out var height,
                out var error))
        {
            return TexturePreviewSizeUpdate.Preserve($"物理尺寸：{error}");
        }

        return TexturePreviewSizeUpdate.Write(
            width,
            height,
            info.FormatPhysicalSize(width, height));
    }
}
