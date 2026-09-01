using System.Collections.ObjectModel;

namespace GrayscaleLayersMac;

public readonly record struct LaserPmtWorkflowPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public readonly record struct LaserPmtWorkflowBounds(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool IsFinite =>
        double.IsFinite(Left) &&
        double.IsFinite(Top) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height);
    public bool HasPositiveSize => Width > 0 && Height > 0;
}

public readonly record struct LaserPmtCanvasViewport(double Zoom, double PanX, double PanY)
{
    public bool IsValid =>
        double.IsFinite(Zoom) && Zoom > 0 &&
        double.IsFinite(PanX) && double.IsFinite(PanY);
}

public sealed record LaserPmtBaseParameterNode(
    string Id,
    LaserPmtWorkflowPoint Position,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlySet<string> RemovedParameters);

public sealed record LaserPmtParameterPort(string Id, string Value);

public sealed record LaserPmtSingleParameterNode(
    string Id,
    LaserPmtWorkflowPoint Position,
    string ParameterName,
    string ValuesText,
    IReadOnlyList<LaserPmtParameterPort> Ports);

public abstract record LaserPmtWorkflowTarget(
    string Id,
    LaserPmtWorkflowBounds Bounds);

public sealed record LaserPmtTarget(
    string Id,
    int Number,
    LaserPmtWorkflowBounds Bounds,
    bool WasManuallyMoved) : LaserPmtWorkflowTarget(Id, Bounds);

public sealed record LaserPmtTimestampTarget(
    string Id,
    long CreationOrder,
    string Text,
    LaserPmtWorkflowBounds Bounds) : LaserPmtWorkflowTarget(Id, Bounds);

public sealed record LaserPmtConnection(
    string Id,
    string SourceNodeId,
    string SourcePortId,
    string TargetId);

public sealed class LaserPmtWorkflow
{
    public string BaseMachineIdentity { get; }
    public LaserPmtWorkflowBounds Workpiece { get; }
    public double HatchSpacing { get; }
    public LaserPmtCanvasViewport Viewport { get; }
    public LaserPmtBaseParameterNode BaseNode { get; }
    public IReadOnlyList<LaserPmtSingleParameterNode> ParameterNodes { get; }
    public IReadOnlyList<LaserPmtWorkflowTarget> Targets { get; }
    public IReadOnlyList<LaserPmtConnection> Connections { get; }
    public int PmtColumns { get; }
    public int NextPmtNumber { get; }
    public long NextCreationOrder { get; }

    public LaserPmtWorkflow(
        string baseMachineIdentity,
        LaserPmtWorkflowBounds workpiece,
        double hatchSpacing,
        LaserPmtCanvasViewport viewport,
        LaserPmtBaseParameterNode baseNode,
        IReadOnlyList<LaserPmtSingleParameterNode> parameterNodes,
        IReadOnlyList<LaserPmtWorkflowTarget> targets,
        IReadOnlyList<LaserPmtConnection> connections,
        int pmtColumns,
        int nextPmtNumber,
        long nextCreationOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseMachineIdentity);
        ArgumentNullException.ThrowIfNull(baseNode);
        ArgumentNullException.ThrowIfNull(parameterNodes);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(connections);
        if (!workpiece.IsFinite || !workpiece.HasPositiveSize)
            throw new ArgumentException("工件边界必须是正的有限数值。", nameof(workpiece));
        if (!double.IsFinite(hatchSpacing) || hatchSpacing <= 0)
            throw new ArgumentOutOfRangeException(nameof(hatchSpacing), "Hatch spacing 必须大于零。");
        if (!viewport.IsValid)
            throw new ArgumentException("画布视口无效。", nameof(viewport));
        if (pmtColumns <= 0)
            throw new ArgumentOutOfRangeException(nameof(pmtColumns));

        BaseMachineIdentity = baseMachineIdentity;
        Workpiece = workpiece;
        HatchSpacing = hatchSpacing;
        Viewport = viewport;
        BaseNode = CopyBaseNode(baseNode);
        ParameterNodes = parameterNodes.Select(CopyParameterNode).ToArray();
        Targets = targets.ToArray();
        Connections = connections.ToArray();
        PmtColumns = pmtColumns;
        NextPmtNumber = nextPmtNumber;
        NextCreationOrder = nextCreationOrder;
        Validate();
    }

    private void Validate()
    {
        var definitions = LaserPmtConfiguration.Parameters
            .Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal);
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        AddUniqueId(allIds, BaseNode.Id, "基础参数节点");
        if (!BaseNode.Position.IsFinite)
            throw new ArgumentException("基础参数节点位置无效。", nameof(BaseNode));
        foreach (var pair in BaseNode.Parameters)
        {
            if (!definitions.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                throw new ArgumentException($"基础参数无效：{pair.Key}", nameof(BaseNode));
        }
        foreach (var name in BaseNode.RemovedParameters)
            if (!definitions.Contains(name) || !BaseNode.Parameters.ContainsKey(name))
                throw new ArgumentException($"被移除的基础参数无效：{name}", nameof(BaseNode));

        var nodesById = new Dictionary<string, LaserPmtSingleParameterNode>(StringComparer.Ordinal);
        var portsById = new Dictionary<string, LaserPmtSingleParameterNode>(StringComparer.Ordinal);
        foreach (var node in ParameterNodes)
        {
            AddUniqueId(allIds, node.Id, "单参数节点");
            if (!node.Position.IsFinite || !definitions.Contains(node.ParameterName))
                throw new ArgumentException($"单参数节点无效：{node.Id}", nameof(ParameterNodes));
            nodesById.Add(node.Id, node);
            foreach (var port in node.Ports)
            {
                AddUniqueId(allIds, port.Id, "参数端口");
                if (string.IsNullOrWhiteSpace(port.Value))
                    throw new ArgumentException($"参数端口值为空：{port.Id}", nameof(ParameterNodes));
                portsById.Add(port.Id, node);
            }
        }

        var targetsById = new Dictionary<string, LaserPmtWorkflowTarget>(StringComparer.Ordinal);
        var pmtNumbers = new HashSet<int>();
        var creationOrders = new HashSet<long>();
        var maximumPmtNumber = 0;
        long maximumCreationOrder = 0;
        foreach (var target in Targets)
        {
            AddUniqueId(allIds, target.Id, "加工目标");
            if (!target.Bounds.IsFinite || !target.Bounds.HasPositiveSize)
                throw new ArgumentException($"加工目标边界无效：{target.Id}", nameof(Targets));
            targetsById.Add(target.Id, target);
            switch (target)
            {
                case LaserPmtTarget pmt:
                    if (pmt.Number <= 0 || !pmtNumbers.Add(pmt.Number))
                        throw new ArgumentException($"PMT 编号无效或重复：{pmt.Number}", nameof(Targets));
                    maximumPmtNumber = Math.Max(maximumPmtNumber, pmt.Number);
                    break;
                case LaserPmtTimestampTarget timestamp:
                    if (timestamp.CreationOrder <= 0 || !creationOrders.Add(timestamp.CreationOrder))
                        throw new ArgumentException($"时间戳创建序号无效或重复：{timestamp.CreationOrder}", nameof(Targets));
                    if (!IsEightAsciiDigits(timestamp.Text))
                        throw new ArgumentException($"时间戳必须是 8 位数字：{timestamp.Id}", nameof(Targets));
                    maximumCreationOrder = Math.Max(maximumCreationOrder, timestamp.CreationOrder);
                    break;
                default:
                    throw new ArgumentException($"不支持的加工目标：{target.GetType().Name}", nameof(Targets));
            }
        }
        if (NextPmtNumber <= maximumPmtNumber || NextPmtNumber <= 0)
            throw new ArgumentException("下一 PMT 编号必须大于所有历史编号。", nameof(NextPmtNumber));
        if (NextCreationOrder <= maximumCreationOrder || NextCreationOrder <= 0)
            throw new ArgumentException("下一创建序号必须大于所有时间戳序号。", nameof(NextCreationOrder));

        var targetParameterInputs = new HashSet<(string TargetId, string ParameterName)>();
        foreach (var connection in Connections)
        {
            AddUniqueId(allIds, connection.Id, "参数连线");
            if (!nodesById.TryGetValue(connection.SourceNodeId, out var sourceNode) ||
                !portsById.TryGetValue(connection.SourcePortId, out var portOwner) ||
                !ReferenceEquals(sourceNode, portOwner))
                throw new ArgumentException($"连线引用不存在的源端口：{connection.Id}", nameof(Connections));
            if (!targetsById.ContainsKey(connection.TargetId))
                throw new ArgumentException($"连线引用不存在的目标：{connection.Id}", nameof(Connections));
            if (!targetParameterInputs.Add((connection.TargetId, sourceNode.ParameterName)))
                throw new ArgumentException(
                    $"目标 {connection.TargetId} 的参数 {sourceNode.ParameterName} 存在重复输入。",
                    nameof(Connections));
        }
    }

    private static LaserPmtBaseParameterNode CopyBaseNode(LaserPmtBaseParameterNode node) => node with
    {
        Parameters = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(node.Parameters, StringComparer.Ordinal)),
        RemovedParameters = new HashSet<string>(node.RemovedParameters, StringComparer.Ordinal)
    };

    private static LaserPmtSingleParameterNode CopyParameterNode(LaserPmtSingleParameterNode node) => node with
    {
        Ports = node.Ports.ToArray()
    };

    private static void AddUniqueId(HashSet<string> ids, string id, string label)
    {
        if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            throw new ArgumentException($"{label} ID 为空或重复：{id}");
    }

    private static bool IsEightAsciiDigits(string text) =>
        text is { Length: 8 } && text.All(character => character is >= '0' and <= '9');
}
