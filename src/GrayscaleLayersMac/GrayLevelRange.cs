using System.Collections.Generic;
using System.Globalization;

namespace GrayscaleLayersMac;

/// <summary>
/// 灰度分层时的灰阶区间：下限以下的像素每层都是黑色，上限及以上的像素每层都是白色。
/// </summary>
public static class GrayLevelRange
{
    public const int Minimum = 0;
    public const int Maximum = 255;

    /// <summary>校验区间能否支撑 <paramref name="layers"/> 层等间隔阈值。</summary>
    public static bool TryValidate(int lower, int upper, int layers, out string error)
    {
        if (lower < Minimum || lower > Maximum - 1 || upper < Minimum + 1 || upper > Maximum)
        {
            error = $"灰阶下限必须是 {Minimum}–{Maximum - 1} 的整数，" +
                    $"灰阶上限必须是 {Minimum + 1}–{Maximum} 的整数。";
            return false;
        }

        if (lower >= upper)
        {
            error = $"灰阶上限必须大于下限，当前为 [{lower}, {upper}]。";
            return false;
        }

        if (upper - lower < layers)
        {
            error = $"灰阶区间 [{lower}, {upper}] 只有 {upper - lower} 个灰阶，" +
                    $"不足以分成 {layers} 层；请减少分层数量（最多 {upper - lower} 层）或放宽灰阶范围。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>把上限抬到至少比下限高一级，用于两个输入框联动。</summary>
    public static int EnsureUpperAbove(int lower, int upper) =>
        upper < lower + 1 ? Math.Min(Maximum, lower + 1) : upper;

    /// <summary>把下限压到至少比上限低一级，用于两个输入框联动。</summary>
    public static int EnsureLowerBelow(int lower, int upper) =>
        lower > upper - 1 ? Math.Max(Minimum, upper - 1) : lower;

    /// <summary>把区间写成 grayscale_layers.py 的命令行参数。</summary>
    public static void AppendArguments(IList<string> arguments, int lower, int upper)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        arguments.Add("--min-level");
        arguments.Add(lower.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--max-level");
        arguments.Add(upper.ToString(CultureInfo.InvariantCulture));
    }
}
