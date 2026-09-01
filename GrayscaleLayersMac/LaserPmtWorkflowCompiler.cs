using System.Collections.ObjectModel;

namespace GrayscaleLayersMac;

public sealed record LaserPmtPortReconciliation(
    LaserPmtSingleParameterNode? Node,
    IReadOnlyList<string> RemovedPortIds,
    string Error)
{
    public bool Success => Node is not null && Error.Length == 0;
}

public enum LaserPmtCompiledTargetKind
{
    Pmt,
    Timestamp
}

public sealed record LaserPmtCompiledTarget(
    string TargetId,
    LaserPmtCompiledTargetKind Kind,
    string Identifier,
    int? PmtNumber,
    long? CreationOrder,
    string? TimestampText,
    LaserPmtWorkflowBounds Bounds,
    IReadOnlyDictionary<string, object> Parameters);

public sealed record LaserPmtCompilationError(
    string? TargetId,
    string? ParameterName,
    string Message);

public sealed record LaserPmtCompilationResult(
    IReadOnlyList<LaserPmtCompiledTarget> Targets,
    IReadOnlyList<LaserPmtCompilationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class LaserPmtWorkflowCompiler
{
    public static LaserPmtPortReconciliation ReconcilePorts(
        LaserPmtSingleParameterNode node,
        string valuesText,
        Func<string> createPortId)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(createPortId);
        if (!LaserPmtConfiguration.TryParseExplicitValues(
                node.ParameterName, valuesText, out var parsed, out var error))
            return new LaserPmtPortReconciliation(null, [], error);

        var normalized = parsed.Select(LaserPmtConfiguration.FormatParameterValue).ToArray();
        var existingByValue = new Dictionary<string, Queue<LaserPmtParameterPort>>(StringComparer.Ordinal);
        foreach (var port in node.Ports)
        {
            if (!existingByValue.TryGetValue(port.Value, out var queue))
            {
                queue = new Queue<LaserPmtParameterPort>();
                existingByValue.Add(port.Value, queue);
            }
            queue.Enqueue(port);
        }

        var nextPorts = new List<LaserPmtParameterPort>(normalized.Length);
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in normalized)
        {
            LaserPmtParameterPort port;
            if (existingByValue.TryGetValue(value, out var queue) && queue.Count > 0)
                port = queue.Dequeue();
            else
                port = new LaserPmtParameterPort(CreateNonEmptyId(createPortId), value);
            retained.Add(port.Id);
            nextPorts.Add(port with { Value = value });
        }
        var removed = node.Ports
            .Where(port => !retained.Contains(port.Id))
            .Select(port => port.Id)
            .ToArray();
        return new LaserPmtPortReconciliation(
            node with { ValuesText = valuesText, Ports = nextPorts },
            removed,
            string.Empty);
    }

    public static LaserPmtCompilationResult Compile(LaserPmtWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var errors = new List<LaserPmtCompilationError>();
        var definitions = LaserPmtConfiguration.Parameters;
        var nodes = workflow.ParameterNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var ports = workflow.ParameterNodes
            .SelectMany(node => node.Ports.Select(port => (port.Id, Node: node, Port: port)))
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        foreach (var node in workflow.ParameterNodes)
        {
            if (!LaserPmtConfiguration.TryParseExplicitValues(
                    node.ParameterName, node.ValuesText, out var parsed, out var error))
            {
                errors.Add(new LaserPmtCompilationError(null, node.ParameterName, error));
                continue;
            }
            var normalized = parsed.Select(LaserPmtConfiguration.FormatParameterValue).ToArray();
            if (!normalized.SequenceEqual(node.Ports.Select(port => port.Value), StringComparer.Ordinal))
                errors.Add(new LaserPmtCompilationError(
                    null,
                    node.ParameterName,
                    $"参数节点 {node.Id} 的值列表与端口不一致。"));
        }

        var overrides = new Dictionary<(string TargetId, string ParameterName), object>();
        foreach (var connection in workflow.Connections)
        {
            var sourceNode = nodes[connection.SourceNodeId];
            var port = ports[connection.SourcePortId].Port;
            if (!LaserPmtConfiguration.TryParseExplicitValues(
                    sourceNode.ParameterName, port.Value, out var values, out var error) ||
                values.Count != 1)
            {
                errors.Add(new LaserPmtCompilationError(
                    connection.TargetId,
                    sourceNode.ParameterName,
                    error.Length == 0 ? $"端口 {port.Id} 必须包含一个参数值。" : error));
                continue;
            }
            overrides[(connection.TargetId, sourceNode.ParameterName)] = values[0];
        }

        var compiled = new List<LaserPmtCompiledTarget>(workflow.Targets.Count);
        foreach (var target in workflow.Targets
                     .OrderBy(target => target is LaserPmtTimestampTarget ? 1 : 0)
                     .ThenBy(target => target is LaserPmtTarget pmt ? pmt.Number : ((LaserPmtTimestampTarget)target).CreationOrder))
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (overrides.TryGetValue((target.Id, definition.Name), out var overrideValue))
                {
                    parameters.Add(definition.Name, overrideValue);
                    continue;
                }
                if (workflow.BaseNode.RemovedParameters.Contains(definition.Name) ||
                    !workflow.BaseNode.Parameters.TryGetValue(definition.Name, out var baseValue))
                {
                    errors.Add(new LaserPmtCompilationError(
                        target.Id,
                        definition.Name,
                        $"目标 {target.Id} 缺少参数 {definition.DisplayName}。"));
                    continue;
                }
                if (!LaserPmtConfiguration.TryParseExplicitValues(
                        definition.Name, baseValue, out var values, out var error) ||
                    values.Count != 1)
                {
                    errors.Add(new LaserPmtCompilationError(target.Id, definition.Name, error));
                    continue;
                }
                parameters.Add(definition.Name, values[0]);
            }
            compiled.Add(CreateCompiledTarget(
                target,
                workflow.Numbering,
                new ReadOnlyDictionary<string, object>(parameters)));
        }
        return new LaserPmtCompilationResult(compiled, errors);
    }

    private static LaserPmtCompiledTarget CreateCompiledTarget(
        LaserPmtWorkflowTarget target,
        LaserPmtWorkflowNumbering numbering,
        IReadOnlyDictionary<string, object> parameters) => target switch
    {
        LaserPmtTarget pmt => new LaserPmtCompiledTarget(
            pmt.Id,
            LaserPmtCompiledTargetKind.Pmt,
            $"{numbering.Prefix}{pmt.Number.ToString($"D{numbering.Padding}", System.Globalization.CultureInfo.InvariantCulture)}",
            pmt.Number,
            null,
            null,
            pmt.Bounds,
            parameters),
        LaserPmtTimestampTarget timestamp => new LaserPmtCompiledTarget(
            timestamp.Id,
            LaserPmtCompiledTargetKind.Timestamp,
            $"timestamp-{timestamp.CreationOrder}",
            null,
            timestamp.CreationOrder,
            timestamp.Text,
            timestamp.Bounds,
            parameters),
        _ => throw new ArgumentException($"不支持的目标类型：{target.GetType().Name}", nameof(target))
    };

    private static string CreateNonEmptyId(Func<string> createPortId)
    {
        var id = createPortId();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("端口 ID 生成器返回了空值。");
        return id;
    }
}
