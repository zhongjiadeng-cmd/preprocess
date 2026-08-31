using System.Text.Json;

namespace GrayscaleLayersMac;

internal sealed record DxfBlockDefinition(
    int BlockIndex, double CenterX, double CenterY, int LineCount);

internal sealed record DxfLineClassification(int BlockIndex, bool IsBorder);

internal sealed class DxfBlockMetadata
{
    private static readonly HashSet<string> TopLevelFields =
        new(StringComparer.Ordinal) { "version", "border_line_count", "blocks" };
    private static readonly HashSet<string> BlockFields =
        new(StringComparer.Ordinal) { "block_index", "center_x", "center_y", "line_count" };

    private readonly string _sidecarPath;
    private readonly int _totalLineCount;

    private DxfBlockMetadata(
        string sidecarPath,
        int borderLineCount,
        IReadOnlyList<DxfBlockDefinition> blocks,
        int totalLineCount)
    {
        _sidecarPath = sidecarPath;
        BorderLineCount = borderLineCount;
        Blocks = blocks;
        _totalLineCount = totalLineCount;
    }

    public int BorderLineCount { get; }
    public IReadOnlyList<DxfBlockDefinition> Blocks { get; }

    public static DxfBlockMetadata? LoadForDxf(string dxfPath)
    {
        // 新生成的产物把 JSON 放在 DXF 同级的 metadata/ 子目录下；
        // 导入或旧版产物仍可能把 JSON 与 DXF 同目录，因此优先查子目录，缺失再回退。
        var candidates = new[]
        {
            Path.Combine(
                Path.GetDirectoryName(dxfPath) ?? string.Empty,
                "metadata",
                Path.GetFileName(Path.ChangeExtension(dxfPath, ".blocks.json"))),
            Path.ChangeExtension(dxfPath, ".blocks.json"),
        };

        foreach (var sidecarPath in candidates)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(sidecarPath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException exception)
            {
                throw Invalid(sidecarPath, "无法读取", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw Invalid(sidecarPath, "无法读取", exception);
            }

            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw Invalid(sidecarPath, "必须是非空普通文件");

            try
            {
                if (new FileInfo(sidecarPath).Length == 0)
                    throw Invalid(sidecarPath, "不能为空");

                using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
                return Parse(sidecarPath, document.RootElement);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw Invalid(sidecarPath, "JSON 无效", exception);
            }
            catch (IOException exception)
            {
                throw Invalid(sidecarPath, "无法读取", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw Invalid(sidecarPath, "无法读取", exception);
            }
        }

        return null;
    }

    public void ValidateLineCount(int lineCount)
    {
        if (lineCount != _totalLineCount)
            throw Invalid(_sidecarPath, "LINE 实体数量与元数据不一致");
    }

    public DxfLineClassification ClassifyLine(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _totalLineCount)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        if (lineIndex < BorderLineCount)
            return new DxfLineClassification(0, true);

        var end = BorderLineCount;
        foreach (var block in Blocks)
        {
            end = checked(end + block.LineCount);
            if (lineIndex < end)
                return new DxfLineClassification(block.BlockIndex, false);
        }

        throw new InvalidOperationException("有效的 LINE 序号未映射到区块。");
    }

    private static DxfBlockMetadata Parse(string sidecarPath, JsonElement root)
    {
        var document = ReadObject(sidecarPath, root, TopLevelFields, "顶层对象");
        var version = ReadInt32(sidecarPath, document["version"], "version");
        if (version != 1)
            throw Invalid(sidecarPath, "version 必须为 1");

        var borderLineCount = ReadInt32(sidecarPath, document["border_line_count"], "border_line_count");
        if (borderLineCount is not 0 and not 4)
            throw Invalid(sidecarPath, "border_line_count 必须为 0 或 4");

        if (document["blocks"].ValueKind != JsonValueKind.Array)
            throw Invalid(sidecarPath, "blocks 必须是数组");

        var blocks = new List<DxfBlockDefinition>();
        var blockIndexes = new HashSet<int>();
        var totalLineCount = borderLineCount;

        foreach (var blockElement in document["blocks"].EnumerateArray())
        {
            var block = ReadObject(sidecarPath, blockElement, BlockFields, "blocks 项");
            var blockIndex = ReadInt32(sidecarPath, block["block_index"], "block_index");
            if (blockIndex < 0)
                throw Invalid(sidecarPath, "block_index 不能为负数");
            if (!blockIndexes.Add(blockIndex))
                throw Invalid(sidecarPath, "block_index 不能重复");

            var centerX = ReadFiniteDouble(sidecarPath, block["center_x"], "center_x");
            var centerY = ReadFiniteDouble(sidecarPath, block["center_y"], "center_y");
            var lineCount = ReadInt32(sidecarPath, block["line_count"], "line_count");
            if (lineCount < 0)
                throw Invalid(sidecarPath, "line_count 不能为负数");

            try
            {
                totalLineCount = checked(totalLineCount + lineCount);
            }
            catch (OverflowException exception)
            {
                throw Invalid(sidecarPath, "LINE 数量溢出", exception);
            }

            blocks.Add(new DxfBlockDefinition(blockIndex, centerX, centerY, lineCount));
        }

        if (blocks.Count == 0)
            throw Invalid(sidecarPath, "blocks 不能为空");

        return new DxfBlockMetadata(sidecarPath, borderLineCount, blocks.ToArray(), totalLineCount);
    }

    private static Dictionary<string, JsonElement> ReadObject(
        string sidecarPath,
        JsonElement element,
        HashSet<string> expectedFields,
        string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid(sidecarPath, $"{description} 必须是对象");

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.TryAdd(property.Name, property.Value))
                throw Invalid(sidecarPath, $"{description} 包含重复字段 {property.Name}");
        }

        if (fields.Count != expectedFields.Count || !fields.Keys.All(expectedFields.Contains))
            throw Invalid(sidecarPath, $"{description} 字段必须严格匹配");

        return fields;
    }

    private static int ReadInt32(string sidecarPath, JsonElement element, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
            throw Invalid(sidecarPath, $"{fieldName} 必须是 Int32 数字");
        return value;
    }

    private static double ReadFiniteDouble(string sidecarPath, JsonElement element, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            throw Invalid(sidecarPath, $"{fieldName} 必须是有限数字");
        }

        return value;
    }

    private static InvalidDataException Invalid(string sidecarPath, string message, Exception? innerException = null) =>
        new($"DXF 区块元数据 '{sidecarPath}' {message}。", innerException);
}
