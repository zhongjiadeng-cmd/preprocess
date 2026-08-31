using System.Globalization;
using System.Text.Json;

namespace GrayscaleLayersMac;

public sealed record LaserPmtParameterDefinition(
    string Name,
    string DisplayName,
    bool IsBoolean,
    int Minimum,
    int Maximum);

public sealed record LaserPmtParameterRow(string Name, string ValuesText);

public static class LaserPmtConfiguration
{
    public const int MaximumJobs = 1000;

    public static IReadOnlyList<LaserPmtParameterDefinition> Parameters { get; } =
    [
        new("power", "功率（power）", false, 0, int.MaxValue),
        new("frequency", "频率（frequency）", false, 0, int.MaxValue),
        new("pulseWidthIdx", "脉宽索引（pulseWidthIdx）", false, 0, int.MaxValue),
        new("scanSpeed", "扫描速度（scanSpeed）", false, 0, int.MaxValue),
        new("jump_vel", "跳转速度（jump_vel）", false, 0, int.MaxValue),
        new("jump_delay", "跳转延迟（jump_delay）", false, 0, int.MaxValue),
        new("scan_ahead", "scanahead", true, 0, 1),
        new("accScale", "加速度比例（accScale）", false, 0, int.MaxValue),
        new("cornerScale", "转角比例（cornerScale）", false, 0, int.MaxValue),
        new("endScale", "结束比例（endScale）", false, 0, int.MaxValue),
        new("sky_writing", "skywritting", true, 0, 1),
        new("timeLag", "时间滞后（timeLag）", false, 0, int.MaxValue),
        new("laserOnShift", "开光偏移（laserOnShift）", false, 0, int.MaxValue),
        new("delaseroff", "关光延迟（delaseroff）", false, 0, int.MaxValue),
        new("delaseron", "开光延迟（delaseron）", false, 0, int.MaxValue),
        new("layerFeedUm", "层间进给（μm）", false, 1, 100000)
    ];

    public static bool TryParseRows(
        IReadOnlyList<LaserPmtParameterRow> rows,
        out IReadOnlyList<(string Name, IReadOnlyList<object> Values)> parsed,
        out int combinationCount,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var definitions = Parameters.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string Name, IReadOnlyList<object> Values)>();
        long count = 1;
        foreach (var row in rows)
        {
            if (!definitions.TryGetValue(row.Name, out var definition))
            {
                parsed = [];
                combinationCount = 0;
                error = $"不支持的 LaserPMT 参数：{row.Name}";
                return false;
            }
            if (!seen.Add(row.Name))
            {
                parsed = [];
                combinationCount = 0;
                error = $"参数不能重复添加：{definition.DisplayName}";
                return false;
            }

            var tokens = row.ValuesText.Split(',', StringSplitOptions.TrimEntries);
            if (tokens.Length == 0 || tokens.Any(token => token.Length == 0))
            {
                parsed = [];
                combinationCount = 0;
                error = $"{definition.DisplayName} 需要输入逗号分隔的参数值。";
                return false;
            }
            var values = new List<object>(tokens.Length);
            foreach (var token in tokens)
            {
                object value;
                if (definition.IsBoolean)
                {
                    if (!bool.TryParse(token, out var boolean))
                    {
                        parsed = [];
                        combinationCount = 0;
                        error = $"{definition.DisplayName} 只接受 true 或 false。";
                        return false;
                    }
                    value = boolean;
                }
                else
                {
                    if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var integer) ||
                        integer < definition.Minimum || integer > definition.Maximum)
                    {
                        parsed = [];
                        combinationCount = 0;
                        error = $"{definition.DisplayName} 的值必须是 {definition.Minimum}–{definition.Maximum} 的整数。";
                        return false;
                    }
                    value = integer;
                }
                if (values.Contains(value))
                {
                    parsed = [];
                    combinationCount = 0;
                    error = $"{definition.DisplayName} 包含重复值：{token}";
                    return false;
                }
                values.Add(value);
            }
            count = checked(count * values.Count);
            if (count > MaximumJobs)
            {
                parsed = [];
                combinationCount = 0;
                error = $"参数组合不能超过 {MaximumJobs} 个。";
                return false;
            }
            result.Add((row.Name, values));
        }

        parsed = result;
        combinationCount = (int)count;
        error = string.Empty;
        return true;
    }

    public static string BuildRequestJson(
        string baseMachineDirectory,
        string outputDirectory,
        string outputName,
        double workpieceWidth,
        double workpieceHeight,
        int columns,
        string prefix,
        int start,
        int increment,
        int padding,
        IReadOnlyList<LaserPmtParameterRow> rows,
        string ownerToken)
    {
        if (!TryParseRows(rows, out var parsed, out _, out var error))
            throw new ArgumentException(error, nameof(rows));
        var parameters = parsed.Select(item => new Dictionary<string, object?>
        {
            ["name"] = item.Name,
            ["values"] = item.Values
        }).ToArray();
        var request = new Dictionary<string, object?>
        {
            ["base_machine_dir"] = baseMachineDirectory,
            ["output_dir"] = outputDirectory,
            ["output_name"] = outputName,
            ["workpiece_width"] = workpieceWidth,
            ["workpiece_height"] = workpieceHeight,
            ["columns"] = columns,
            ["numbering"] = new Dictionary<string, object?>
            {
                ["prefix"] = prefix,
                ["start"] = start,
                ["increment"] = increment,
                ["padding"] = padding
            },
            ["parameters"] = parameters,
            ["owner_token"] = ownerToken
        };
        return JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
    }
}
