using System.Globalization;
using System.Text.Json;

namespace GrayscaleLayersMac;

public sealed record LaserPmtBaseMetadata(
    string Identity,
    double UnitWidth,
    double UnitHeight,
    IReadOnlyDictionary<string, string> Parameters)
{
    public static LaserPmtBaseMetadata Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var identity = root.GetProperty("base_machine_identity").GetString();
        var unit = root.GetProperty("unit");
        var parameters = root.GetProperty("parameters").EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => property.Value.GetInt32().ToString(CultureInfo.InvariantCulture),
                _ => throw new InvalidDataException($"基础参数格式无效：{property.Name}")
            },
            StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(identity) ||
            parameters.Count != LaserPmtConfiguration.Parameters.Count)
            throw new InvalidDataException("基础加工元数据不完整。");
        return new LaserPmtBaseMetadata(
            identity,
            unit.GetProperty("width").GetDouble(),
            unit.GetProperty("height").GetDouble(),
            parameters);
    }
}
