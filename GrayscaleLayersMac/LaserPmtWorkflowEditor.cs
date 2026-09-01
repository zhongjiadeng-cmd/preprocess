namespace GrayscaleLayersMac;

public enum LaserPmtGeometryErrorCode
{
    OutOfBounds,
    Overlap
}

public sealed record LaserPmtGeometryError(
    LaserPmtGeometryErrorCode Code,
    string TargetId,
    string? OtherTargetId,
    string Message);

public sealed record LaserPmtParameterNodeUpdate(
    LaserPmtWorkflow Workflow,
    IReadOnlyList<string> RemovedConnectionIds);

public static class LaserPmtWorkflowEditor
{
    public static LaserPmtWorkflow AddTimestamp(
        LaserPmtWorkflow workflow,
        string targetId,
        string text,
        LaserPmtWorkflowBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var target = new LaserPmtTimestampTarget(
            targetId,
            workflow.NextCreationOrder,
            text,
            bounds);
        return Rebuild(
            workflow,
            targets: workflow.Targets.Append<LaserPmtWorkflowTarget>(target).ToArray(),
            nextCreationOrder: checked(workflow.NextCreationOrder + 1));
    }

    public static LaserPmtWorkflow DeleteTarget(LaserPmtWorkflow workflow, string targetId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (!workflow.Targets.Any(target => target.Id == targetId))
            throw new ArgumentException($"找不到加工目标：{targetId}", nameof(targetId));
        return Rebuild(
            workflow,
            targets: workflow.Targets.Where(target => target.Id != targetId).ToArray(),
            connections: workflow.Connections
                .Where(connection => connection.TargetId != targetId)
                .ToArray());
    }

    public static LaserPmtWorkflow MoveTimestamp(
        LaserPmtWorkflow workflow,
        string targetId,
        double left,
        double top) => UpdateTimestampBounds(
            workflow,
            targetId,
            bounds => bounds with { Left = left, Top = top });

    public static LaserPmtWorkflow ResizeTimestamp(
        LaserPmtWorkflow workflow,
        string targetId,
        double width,
        double height) => UpdateTimestampBounds(
            workflow,
            targetId,
            bounds => bounds with { Width = width, Height = height });

    public static LaserPmtWorkflow UpdateTimestampText(
        LaserPmtWorkflow workflow,
        string targetId,
        string text)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var found = false;
        var targets = workflow.Targets.Select(target =>
        {
            if (target.Id != targetId)
                return target;
            if (target is not LaserPmtTimestampTarget timestamp)
                throw new ArgumentException($"目标不是时间戳：{targetId}", nameof(targetId));
            found = true;
            return (LaserPmtWorkflowTarget)(timestamp with { Text = text });
        }).ToArray();
        if (!found)
            throw new ArgumentException($"找不到时间戳：{targetId}", nameof(targetId));
        return Rebuild(workflow, targets: targets);
    }

    public static LaserPmtWorkflow AddConnection(
        LaserPmtWorkflow workflow,
        LaserPmtConnection connection)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(connection);
        return Rebuild(
            workflow,
            connections: workflow.Connections.Append(connection).ToArray());
    }

    public static LaserPmtWorkflow RemoveConnection(
        LaserPmtWorkflow workflow,
        string connectionId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (!workflow.Connections.Any(connection => connection.Id == connectionId))
            throw new ArgumentException($"找不到参数连线：{connectionId}", nameof(connectionId));
        return Rebuild(
            workflow,
            connections: workflow.Connections
                .Where(connection => connection.Id != connectionId)
                .ToArray());
    }

    public static LaserPmtParameterNodeUpdate UpdateParameterNodeValues(
        LaserPmtWorkflow workflow,
        string nodeId,
        string valuesText,
        Func<string> createPortId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var index = workflow.ParameterNodes.ToList().FindIndex(node => node.Id == nodeId);
        if (index < 0)
            throw new ArgumentException($"找不到单参数节点：{nodeId}", nameof(nodeId));
        var reconciliation = LaserPmtWorkflowCompiler.ReconcilePorts(
            workflow.ParameterNodes[index], valuesText, createPortId);
        if (!reconciliation.Success)
            throw new ArgumentException(reconciliation.Error, nameof(valuesText));
        var nodes = workflow.ParameterNodes.ToArray();
        nodes[index] = reconciliation.Node!;
        var removedPorts = reconciliation.RemovedPortIds.ToHashSet(StringComparer.Ordinal);
        var removedConnections = workflow.Connections
            .Where(connection => removedPorts.Contains(connection.SourcePortId))
            .Select(connection => connection.Id)
            .ToArray();
        var removedConnectionSet = removedConnections.ToHashSet(StringComparer.Ordinal);
        return new LaserPmtParameterNodeUpdate(
            Rebuild(
                workflow,
                parameterNodes: nodes,
                connections: workflow.Connections
                    .Where(connection => !removedConnectionSet.Contains(connection.Id))
                    .ToArray()),
            removedConnections);
    }

    public static LaserPmtWorkflow SetBaseParameterEnabled(
        LaserPmtWorkflow workflow,
        string parameterName,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (!workflow.BaseNode.Parameters.ContainsKey(parameterName))
            throw new ArgumentException($"基础节点不包含参数：{parameterName}", nameof(parameterName));
        var removed = new HashSet<string>(workflow.BaseNode.RemovedParameters, StringComparer.Ordinal);
        if (enabled)
            removed.Remove(parameterName);
        else
            removed.Add(parameterName);
        return Rebuild(
            workflow,
            baseNode: workflow.BaseNode with { RemovedParameters = removed });
    }

    public static LaserPmtWorkflow AddParameterNode(
        LaserPmtWorkflow workflow,
        LaserPmtSingleParameterNode node)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(node);
        return Rebuild(
            workflow,
            parameterNodes: workflow.ParameterNodes.Append(node).ToArray());
    }

    public static LaserPmtWorkflow DeleteParameterNode(
        LaserPmtWorkflow workflow,
        string nodeId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (!workflow.ParameterNodes.Any(node => node.Id == nodeId))
            throw new ArgumentException($"找不到单参数节点：{nodeId}", nameof(nodeId));
        return Rebuild(
            workflow,
            parameterNodes: workflow.ParameterNodes.Where(node => node.Id != nodeId).ToArray(),
            connections: workflow.Connections
                .Where(connection => connection.SourceNodeId != nodeId)
                .ToArray());
    }

    public static LaserPmtWorkflow MoveParameterNode(
        LaserPmtWorkflow workflow,
        string nodeId,
        LaserPmtWorkflowPoint position)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var found = false;
        var nodes = workflow.ParameterNodes.Select(node =>
        {
            if (node.Id != nodeId)
                return node;
            found = true;
            return node with { Position = position };
        }).ToArray();
        if (!found)
            throw new ArgumentException($"找不到单参数节点：{nodeId}", nameof(nodeId));
        return Rebuild(workflow, parameterNodes: nodes);
    }

    public static LaserPmtWorkflow MoveBaseNode(
        LaserPmtWorkflow workflow,
        LaserPmtWorkflowPoint position) => Rebuild(
            workflow,
            baseNode: workflow.BaseNode with { Position = position });

    public static LaserPmtWorkflow DeletePmt(LaserPmtWorkflow workflow, string targetId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (workflow.Targets.FirstOrDefault(target => target.Id == targetId) is not LaserPmtTarget)
            throw new ArgumentException($"找不到 PMT：{targetId}", nameof(targetId));
        return Rebuild(
            workflow,
            targets: workflow.Targets.Where(target => target.Id != targetId).ToArray(),
            connections: workflow.Connections
                .Where(connection => connection.TargetId != targetId)
                .ToArray());
    }

    public static LaserPmtWorkflow SetPmtCount(
        LaserPmtWorkflow workflow,
        int count,
        double unitWidth,
        double unitHeight,
        Func<string> createTargetId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(createTargetId);
        if (count is < 0 or > LaserPmtConfiguration.MaximumJobs)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (!double.IsFinite(unitWidth) || !double.IsFinite(unitHeight) || unitWidth <= 0 || unitHeight <= 0)
            throw new ArgumentException("PMT 单元尺寸必须是正的有限数值。", nameof(unitWidth));

        var result = workflow;
        while (result.Targets.OfType<LaserPmtTarget>().Count() > count)
        {
            var highest = result.Targets.OfType<LaserPmtTarget>().MaxBy(target => target.Number)!;
            result = DeletePmt(result, highest.Id);
        }
        while (result.Targets.OfType<LaserPmtTarget>().Count() < count)
        {
            var id = createTargetId();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("PMT ID 生成器返回了空值。");
            var bounds = FindFirstAvailableBounds(result, count, unitWidth, unitHeight);
            var targets = result.Targets
                .Append<LaserPmtWorkflowTarget>(new LaserPmtTarget(
                    id,
                    result.NextPmtNumber,
                    bounds,
                    false))
                .ToArray();
            result = Rebuild(
                result,
                targets: targets,
                nextPmtNumber: checked(result.NextPmtNumber + result.Numbering.Increment));
        }
        return result;
    }

    public static LaserPmtWorkflow MovePmt(
        LaserPmtWorkflow workflow,
        string targetId,
        double left,
        double top)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (!double.IsFinite(left) || !double.IsFinite(top))
            throw new ArgumentException("PMT 位置必须是有限数值。");
        var found = false;
        var targets = workflow.Targets.Select(target =>
        {
            if (target.Id != targetId)
                return target;
            if (target is not LaserPmtTarget pmt)
                throw new ArgumentException($"目标不是 PMT：{targetId}", nameof(targetId));
            found = true;
            return (LaserPmtWorkflowTarget)(pmt with
            {
                Bounds = pmt.Bounds with { Left = left, Top = top },
                WasManuallyMoved = true
            });
        }).ToArray();
        if (!found)
            throw new ArgumentException($"找不到 PMT：{targetId}", nameof(targetId));
        return Rebuild(workflow, targets: targets);
    }

    public static LaserPmtWorkflow SetPmtColumns(LaserPmtWorkflow workflow, int columns)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns));
        return Rebuild(workflow, pmtColumns: columns);
    }

    public static LaserPmtWorkflow AutoArrangePmts(
        LaserPmtWorkflow workflow,
        double unitWidth,
        double unitHeight)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var pmts = workflow.Targets.OfType<LaserPmtTarget>()
            .OrderBy(target => target.Number)
            .ToArray();
        if (pmts.Length == 0)
            return workflow;
        var positions = CalculateAutomaticBounds(
            workflow.Workpiece,
            pmts.Length,
            workflow.PmtColumns,
            unitWidth,
            unitHeight);
        var byId = pmts.Select((pmt, index) => (pmt.Id, Bounds: positions[index]))
            .ToDictionary(item => item.Id, item => item.Bounds, StringComparer.Ordinal);
        var targets = workflow.Targets.Select(target => target is LaserPmtTarget pmt
            ? (LaserPmtWorkflowTarget)(pmt with
            {
                Bounds = byId[pmt.Id],
                WasManuallyMoved = false
            })
            : target).ToArray();
        return Rebuild(workflow, targets: targets);
    }

    public static IReadOnlyList<LaserPmtGeometryError> ValidateGeometry(
        LaserPmtWorkflow workflow,
        int coordinateDecimals = 3)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (coordinateDecimals is < 0 or > 12)
            throw new ArgumentOutOfRangeException(nameof(coordinateDecimals));
        var errors = new List<LaserPmtGeometryError>();
        foreach (var target in workflow.Targets)
        {
            if (!Contains(workflow.Workpiece, target.Bounds) ||
                !Contains(RoundBounds(workflow.Workpiece, coordinateDecimals),
                    RoundBounds(target.Bounds, coordinateDecimals)))
                errors.Add(new LaserPmtGeometryError(
                    LaserPmtGeometryErrorCode.OutOfBounds,
                    target.Id,
                    null,
                    $"目标 {target.Id} 超出工件边界。"));
        }
        for (var left = 0; left < workflow.Targets.Count; left++)
        {
            for (var right = left + 1; right < workflow.Targets.Count; right++)
            {
                var first = workflow.Targets[left];
                var second = workflow.Targets[right];
                if (!Overlaps(first.Bounds, second.Bounds) &&
                    !Overlaps(
                        RoundBounds(first.Bounds, coordinateDecimals),
                        RoundBounds(second.Bounds, coordinateDecimals)))
                    continue;
                errors.Add(new LaserPmtGeometryError(
                    LaserPmtGeometryErrorCode.Overlap,
                    first.Id,
                    second.Id,
                    $"目标 {first.Id} 与 {second.Id} 重叠。"));
            }
        }
        return errors;
    }

    public static IReadOnlyList<LaserPmtWorkflowBounds> CalculateAutomaticBounds(
        LaserPmtWorkflowBounds workpiece,
        int count,
        int configuredColumns,
        double unitWidth,
        double unitHeight)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (configuredColumns <= 0)
            throw new ArgumentOutOfRangeException(nameof(configuredColumns));
        var columns = Math.Min(configuredColumns, count);
        var rows = (count + columns - 1) / columns;
        var horizontalGap = (workpiece.Width - columns * unitWidth) / (columns + 1);
        var verticalGap = (workpiece.Height - rows * unitHeight) / (rows + 1);
        if (!double.IsFinite(horizontalGap) || !double.IsFinite(verticalGap) ||
            horizontalGap < 0 || verticalGap < 0)
            throw new ArgumentException("工件不足以容纳 PMT 自动布局。", nameof(workpiece));
        return Enumerable.Range(0, count).Select(index =>
        {
            var row = index / columns;
            var column = index % columns;
            return new LaserPmtWorkflowBounds(
                workpiece.Left + horizontalGap + column * (unitWidth + horizontalGap),
                workpiece.Top + verticalGap + row * (unitHeight + verticalGap),
                unitWidth,
                unitHeight);
        }).ToArray();
    }

    private static LaserPmtWorkflowBounds FindFirstAvailableBounds(
        LaserPmtWorkflow workflow,
        int desiredPmtCount,
        double unitWidth,
        double unitHeight)
    {
        var candidates = CalculateAutomaticBounds(
            workflow.Workpiece,
            Math.Max(desiredPmtCount, 1),
            workflow.PmtColumns,
            unitWidth,
            unitHeight);
        foreach (var candidate in candidates)
            if (workflow.Targets.All(target => !Overlaps(candidate, target.Bounds)))
                return candidate;
        throw new InvalidOperationException("工件中没有可用于新增 PMT 的自动布局位置。");
    }

    private static bool Contains(LaserPmtWorkflowBounds outer, LaserPmtWorkflowBounds inner) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top &&
        inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static bool Overlaps(LaserPmtWorkflowBounds first, LaserPmtWorkflowBounds second) =>
        Math.Max(first.Left, second.Left) < Math.Min(first.Right, second.Right) &&
        Math.Max(first.Top, second.Top) < Math.Min(first.Bottom, second.Bottom);

    private static LaserPmtWorkflowBounds RoundBounds(
        LaserPmtWorkflowBounds bounds,
        int decimals)
    {
        var left = Math.Round(bounds.Left, decimals, MidpointRounding.AwayFromZero);
        var top = Math.Round(bounds.Top, decimals, MidpointRounding.AwayFromZero);
        var right = Math.Round(bounds.Right, decimals, MidpointRounding.AwayFromZero);
        var bottom = Math.Round(bounds.Bottom, decimals, MidpointRounding.AwayFromZero);
        return new LaserPmtWorkflowBounds(left, top, right - left, bottom - top);
    }

    private static LaserPmtWorkflow Rebuild(
        LaserPmtWorkflow workflow,
        LaserPmtBaseParameterNode? baseNode = null,
        IReadOnlyList<LaserPmtSingleParameterNode>? parameterNodes = null,
        IReadOnlyList<LaserPmtWorkflowTarget>? targets = null,
        IReadOnlyList<LaserPmtConnection>? connections = null,
        int? pmtColumns = null,
        int? nextPmtNumber = null,
        long? nextCreationOrder = null) => new(
            workflow.BaseMachineIdentity,
            workflow.Workpiece,
            workflow.HatchSpacing,
            workflow.Viewport,
            baseNode ?? workflow.BaseNode,
            parameterNodes ?? workflow.ParameterNodes,
            targets ?? workflow.Targets,
            connections ?? workflow.Connections,
            pmtColumns ?? workflow.PmtColumns,
            nextPmtNumber ?? workflow.NextPmtNumber,
            nextCreationOrder ?? workflow.NextCreationOrder,
            workflow.Numbering);

    private static LaserPmtWorkflow UpdateTimestampBounds(
        LaserPmtWorkflow workflow,
        string targetId,
        Func<LaserPmtWorkflowBounds, LaserPmtWorkflowBounds> update)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(update);
        var found = false;
        var targets = workflow.Targets.Select(target =>
        {
            if (target.Id != targetId)
                return target;
            if (target is not LaserPmtTimestampTarget timestamp)
                throw new ArgumentException($"目标不是时间戳：{targetId}", nameof(targetId));
            found = true;
            return (LaserPmtWorkflowTarget)(timestamp with { Bounds = update(timestamp.Bounds) });
        }).ToArray();
        if (!found)
            throw new ArgumentException($"找不到时间戳：{targetId}", nameof(targetId));
        return Rebuild(workflow, targets: targets);
    }
}
