using System.Text.Json;

namespace GrayscaleLayersMac;

public sealed record LaserPmtJobLayout(
    int Index,
    string Identifier,
    int Row,
    int Column,
    double Left,
    double Top,
    double Width,
    double Height,
    string JsonFile,
    int LayerFeedUm,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record LaserPmtLayout(
    double WorkpieceWidth,
    double WorkpieceHeight,
    int Rows,
    int Columns,
    double HorizontalGap,
    double VerticalGap,
    IReadOnlyList<LaserPmtJobLayout> Jobs)
{
    public const int CurrentFormatVersion = 1;
    public const int MaximumManifestBytes = 16 * 1024 * 1024;

    public static LaserPmtLayout Load(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumManifestBytes)
            throw new InvalidDataException("PMT 布局清单不存在、为空或过大。");
        return Parse(File.ReadAllText(path));
    }

    public static LaserPmtLayout Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        var root = document.RootElement;
        RequireObject(root, "PMT 布局根节点");
        RequireUniqueProperties(root, "PMT 布局根节点");
        if (ReadInt(root, "format_version") != CurrentFormatVersion)
            throw new InvalidDataException("不支持的 PMT 布局版本。");

        var workpiece = ReadObject(root, "workpiece");
        var matrix = ReadObject(root, "matrix");
        var width = ReadPositiveDouble(workpiece, "width");
        var height = ReadPositiveDouble(workpiece, "height");
        var rows = ReadPositiveInt(matrix, "rows");
        var columns = ReadPositiveInt(matrix, "columns");
        var horizontalGap = ReadNonNegativeDouble(matrix, "horizontal_gap");
        var verticalGap = ReadNonNegativeDouble(matrix, "vertical_gap");
        var jobsElement = root.GetProperty("jobs");
        if (jobsElement.ValueKind != JsonValueKind.Array ||
            jobsElement.GetArrayLength() is < 1 or > LaserPmtConfiguration.MaximumJobs)
            throw new InvalidDataException("PMT 布局任务数量无效。");

        var jobs = new List<LaserPmtJobLayout>(jobsElement.GetArrayLength());
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var jobElement in jobsElement.EnumerateArray())
        {
            RequireObject(jobElement, "PMT 任务");
            RequireUniqueProperties(jobElement, "PMT 任务");
            var index = ReadInt(jobElement, "index");
            if (index != jobs.Count)
                throw new InvalidDataException("PMT 任务必须按连续索引排序。");
            var identifier = ReadString(jobElement, "identifier");
            if (!identifiers.Add(identifier))
                throw new InvalidDataException("PMT 任务编号重复。");
            var bounds = ReadObject(jobElement, "bounds");
            var left = ReadNonNegativeDouble(bounds, "left");
            var top = ReadNonNegativeDouble(bounds, "top");
            var jobWidth = ReadPositiveDouble(bounds, "width");
            var jobHeight = ReadPositiveDouble(bounds, "height");
            if (left + jobWidth > width + 1e-6 || top + jobHeight > height + 1e-6)
                throw new InvalidDataException("PMT 任务超出工件边界。");
            var parametersElement = jobElement.GetProperty("parameters");
            RequireObject(parametersElement, "PMT 参数");
            RequireUniqueProperties(parametersElement, "PMT 参数");
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in parametersElement.EnumerateObject())
                parameters.Add(property.Name, property.Value.ToString());
            jobs.Add(new LaserPmtJobLayout(
                index,
                identifier,
                ReadNonNegativeInt(jobElement, "row"),
                ReadNonNegativeInt(jobElement, "column"),
                left,
                top,
                jobWidth,
                jobHeight,
                ReadString(jobElement, "json_file"),
                ReadPositiveInt(jobElement, "layer_feed_um"),
                parameters));
        }
        if (jobs.Any(job => job.Row >= rows || job.Column >= columns))
            throw new InvalidDataException("PMT 任务行列索引超出矩阵范围。");
        return new LaserPmtLayout(
            width, height, rows, columns, horizontalGap, verticalGap, jobs);
    }

    private static JsonElement ReadObject(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        RequireObject(value, name);
        RequireUniqueProperties(value, name);
        return value;
    }

    private static void RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{label} 必须是对象。");
    }

    private static void RequireUniqueProperties(JsonElement value, string label)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name))
                throw new InvalidDataException($"{label} 包含重复字段：{property.Name}");
    }

    private static string ReadString(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"{name} 必须是非空字符串。");
        return value.GetString()!;
    }

    private static int ReadInt(JsonElement owner, string name)
    {
        if (!owner.GetProperty(name).TryGetInt32(out var value))
            throw new InvalidDataException($"{name} 必须是整数。");
        return value;
    }

    private static int ReadNonNegativeInt(JsonElement owner, string name)
    {
        var value = ReadInt(owner, name);
        if (value < 0)
            throw new InvalidDataException($"{name} 不能为负数。");
        return value;
    }

    private static int ReadPositiveInt(JsonElement owner, string name)
    {
        var value = ReadInt(owner, name);
        if (value <= 0)
            throw new InvalidDataException($"{name} 必须大于零。");
        return value;
    }

    private static double ReadNonNegativeDouble(JsonElement owner, string name)
    {
        if (!owner.GetProperty(name).TryGetDouble(out var value) || !double.IsFinite(value) || value < 0)
            throw new InvalidDataException($"{name} 必须是非负有限数值。");
        return value;
    }

    private static double ReadPositiveDouble(JsonElement owner, string name)
    {
        var value = ReadNonNegativeDouble(owner, name);
        if (value <= 0)
            throw new InvalidDataException($"{name} 必须大于零。");
        return value;
    }
}
