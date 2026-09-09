namespace GrayscaleLayersMac;

internal interface IMotionPreferenceSource
{
    bool ReduceMotion { get; }
}

internal sealed class EnvironmentMotionPreferenceSource : IMotionPreferenceSource
{
    private const string VariableName = "GRAYSCALE_LAYERS_REDUCE_MOTION";

    public bool ReduceMotion
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(VariableName);
            return value is not null &&
                (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }
    }
}

/// <summary>
/// 集中管理动态效果偏好。Avalonia 11 当前没有跨平台的 reduce-motion API，
/// 因此使用可替换来源：发布版保持克制动画，自动化与受管环境可通过环境变量开启减少动态。
/// </summary>
internal static class MotionPreferences
{
    private static IMotionPreferenceSource _source = new EnvironmentMotionPreferenceSource();

    public static bool ReduceMotion => _source.ReduceMotion;

    public static bool AnimateSpatialProperties => !ReduceMotion;

    public static TimeSpan ColorDuration(TimeSpan normal) =>
        ReduceMotion ? TimeSpan.FromMilliseconds(80) : normal;

    public static TimeSpan FadeDuration(TimeSpan normal) =>
        ReduceMotion ? TimeSpan.FromMilliseconds(80) : normal;

    internal static IDisposable OverrideForTesting(bool reduceMotion)
    {
        var previous = _source;
        _source = new FixedMotionPreferenceSource(reduceMotion);
        return new RestoreSource(previous);
    }

    private sealed record FixedMotionPreferenceSource(bool ReduceMotion) : IMotionPreferenceSource;

    private sealed class RestoreSource(IMotionPreferenceSource previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _source = previous;
        }
    }
}
