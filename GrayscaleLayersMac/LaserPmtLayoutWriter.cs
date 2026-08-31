using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrayscaleLayersMac;

/// <summary>
/// 把 <paramref name="newParameters"/> 替换为指定 job 的覆盖参数，写回
/// <c>pmt-layout.json</c> 中对应 job 的 <c>parameters</c> 字段；其它顶层字段、
/// 其它 job 与 <paramref name="layoutPath"/> 同目录下的 <c>pmt_xxxxmachine.json</c>
/// <strong>暂不修改</strong>（改机器物理文件需要在 Step 3 输出目录的
/// <c>machine.json</c> 模板基础上重新生成，由调用方在"全部执行"流程里完成）。
/// 所有写盘走临时文件 + <see cref="File.Move(string,string,bool)"/>，避免半写入。
/// </summary>
public static class LaserPmtLayoutWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] RequiredTopKeys =
    {
        "format_version", "coordinate_system", "workpiece", "unit",
        "matrix", "numbering", "parameter_order", "jobs"
    };

    /// <summary>
    /// 写回 <c>pmt-layout.json</c>，把目标 job 的 <c>parameters</c> 替换为
    /// <paramref name="newParameters"/>；保留所有其它字段与顺序。
    /// </summary>
    /// <param name="layoutPath">PMT 布局清单的绝对路径。</param>
    /// <param name="jobIdentifier">目标 job 编号（如 <c>pmt_0001</c>）。</param>
    /// <param name="newParameters">
    /// 完整覆盖字典；key 必须是 <see cref="LaserPmtConfiguration.Parameters"/> 中的合法名称，
    /// 空字符串表示"沿用基础加工参数"，不会写入结果。
    /// </param>
    /// <param name="validateTypes">
    /// 为 true 时检查布尔参数必须是 <c>true</c>/<c>false</c>、整数参数必须能解析。
    /// </param>
    /// <exception cref="FileNotFoundException">布局文件不存在。</exception>
    /// <exception cref="InvalidDataException">布局结构不符合 schema 或参数非法。</exception>
    public static void UpdateJob(
        string layoutPath,
        string jobIdentifier,
        IReadOnlyDictionary<string, string> newParameters,
        bool validateTypes = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobIdentifier);
        ArgumentNullException.ThrowIfNull(newParameters);

        if (validateTypes)
            ValidateParameterValues(newParameters);

        var layoutJson = File.ReadAllText(layoutPath);
        var layoutRoot = JsonNode.Parse(layoutJson)?.AsObject()
            ?? throw new InvalidDataException("PMT 布局根节点必须是对象。");

        EnsureLayoutSchema(layoutRoot);

        var jobsNode = layoutRoot["jobs"]?.AsArray()
            ?? throw new InvalidDataException("PMT 布局缺少 jobs 数组。");

        JsonNode? target = null;
        foreach (var entry in jobsNode)
        {
            var jobObject = entry?.AsObject()
                ?? throw new InvalidDataException("PMT job 节点必须是对象。");
            if (string.Equals(jobObject["identifier"]?.GetValue<string>(), jobIdentifier, StringComparison.Ordinal))
            {
                target = jobObject;
                break;
            }
        }
        if (target is null)
            throw new InvalidDataException($"PMT 布局中找不到编号 {jobIdentifier}。");

        var parametersNode = BuildParametersNode(newParameters);
        target["parameters"] = parametersNode;
        WriteAtomic(layoutPath, layoutRoot.ToJsonString(WriteOptions));
    }

    /// <summary>
    /// 在不修改任何文件的情况下，把内存中的 <see cref="LaserPmtJobLayout"/>
    /// 用新字典生成一份全新的 <see cref="LaserPmtLayout"/> 实例，便于 UI 调试或撤销栈。
    /// </summary>
    public static LaserPmtJobLayout WithParameters(
        LaserPmtJobLayout job,
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(parameters);
        var normalized = new Dictionary<string, string>(parameters, StringComparer.Ordinal);
        return job with { Parameters = normalized };
    }

    private static JsonObject BuildParametersNode(IReadOnlyDictionary<string, string> values)
    {
        var node = new JsonObject();
        if (values.Count == 0)
            return node;
        var known = LaserPmtConfiguration.Parameters
            .ToDictionary(item => item.Name, StringComparer.Ordinal);
        var insertOrder = LaserPmtConfiguration.Parameters
            .Select(item => item.Name)
            .ToList();
        foreach (var name in values.Keys)
            if (!known.ContainsKey(name))
                insertOrder.Add(name);
        foreach (var name in insertOrder)
        {
            if (!known.ContainsKey(name))
                throw new InvalidDataException($"不支持的 LaserPMT 参数：{name}");
            if (!values.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;
            node[name] = raw.Trim();
        }
        return node;
    }

    private static void EnsureLayoutSchema(JsonObject root)
    {
        foreach (var requiredKey in RequiredTopKeys)
        {
            if (root[requiredKey] is null)
                throw new InvalidDataException($"PMT 布局缺少顶层字段：{requiredKey}。");
        }
        if (root["format_version"]?.GetValue<int>() != LaserPmtLayout.CurrentFormatVersion)
            throw new InvalidDataException("不支持的 PMT 布局版本。");
    }

    private static void ValidateParameterValues(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (name, raw) in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var definition = LaserPmtConfiguration.Parameters
                .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"不支持的 LaserPMT 参数：{name}");
            if (definition.IsBoolean)
            {
                if (!bool.TryParse(raw, out _))
                    throw new InvalidDataException($"{definition.DisplayName} 只接受 true 或 false。");
            }
            else
            {
                if (!int.TryParse(raw, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException($"{definition.DisplayName} 的值必须是整数。");
            }
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("无效的写入路径。");
        Directory.CreateDirectory(directory);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content, new System.Text.UTF8Encoding(false));
        try
        {
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
            throw;
        }
    }
}
