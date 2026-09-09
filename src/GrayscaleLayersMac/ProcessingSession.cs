namespace GrayscaleLayersMac;

/// <summary>All durable edits are transactional snapshots; previews and selection are not history.</summary>
public sealed class ProcessingSession
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string? _clipboard;
    private int _numberHighWater = 1;
    public ProcessingDocument Document { get; private set; } = new();
    public HashSet<string> Selection { get; } = new();
    public string? SelectedInstanceId { get; set; }
    public long Revision { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public void Edit(Func<ProcessingDocument, ProcessingDocument> edit)
    {
        var previous = Document.ToJson();
        var next = edit(ProcessingDocument.FromJson(previous));
        next = next with { NextPmtNumber = Math.Max(next.NextPmtNumber, _numberHighWater) };
        next.Validate();
        if (next.ToJson() == previous) return;
        _undo.Push(previous);
        _redo.Clear();
        Install(next);
    }
    private void Install(ProcessingDocument document)
    {
        Document = document;
        _numberHighWater = Math.Max(_numberHighWater, document.NextPmtNumber);
        Selection.IntersectWith(document.Nodes.Select(n => n.Id).Concat(document.Connections.Select(c => c.Id)));
        if (!document.Nodes.Any(n => n.Instances.Any(i => i.Id == SelectedInstanceId))) SelectedInstanceId = null;
        Revision++;
        Changed?.Invoke();
    }
    public void Open(ProcessingDocument document)
    {
        document.Validate();
        _undo.Clear(); _redo.Clear(); Selection.Clear(); SelectedInstanceId = null;
        _numberHighWater = document.NextPmtNumber;
        Install(ProcessingDocument.FromJson(document.ToJson()));
    }
    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (!from.TryPop(out var json)) return;
        to.Push(Document.ToJson());
        Install(ProcessingDocument.FromJson(json) with { NextPmtNumber = _numberHighWater });
    }
    public void Select(string? id, bool toggle = false, string? instanceId = null)
    {
        if (!toggle) Selection.Clear();
        if (id is not null && !Selection.Add(id) && toggle) Selection.Remove(id);
        SelectedInstanceId = instanceId;
        Changed?.Invoke();
    }
    public void View(double zoom, double x, double y)
    {
        Document = Document with { Zoom = zoom, PanX = x, PanY = y };
    }
    public void UpdateNode(ProcessingNode node) => Edit(d => d with
    {
        Nodes = d.Nodes.Select(n => n.Id == node.Id ? node : n).ToArray()
    });
    public void Connect(string source, string target, string[]? layers)
    {
        if (Document.Connections.Any(c => c.TargetId == target))
            throw new InvalidOperationException("输入已有连接，请先选中连线并删除。");
        Edit(d => d with { Connections = d.Connections.Append(new(Guid.NewGuid().ToString("N"), source, target, layers?.ToArray())).ToArray() });
    }
    public ProcessingNode Add(ProcessingNodeKind kind, double x, double y, string? path = null)
    {
        var node = ProcessingNode.Create(kind, x, y);
        if (path is not null) node.Settings["path"] = path;
        Edit(d =>
        {
            if (kind == ProcessingNodeKind.Pmt)
                node = node with { Instances = [new() { Number = d.NextPmtNumber, X = 5, Y = 5 }] };
            return d with { Nodes = d.Nodes.Append(node).ToArray(), NextPmtNumber = d.NextPmtNumber + (kind == ProcessingNodeKind.Pmt ? 1 : 0) };
        });
        Select(node.Id);
        return node;
    }
    public void Delete()
    {
        if (SelectedInstanceId is { } instance && Selection.Count == 1)
        {
            var node = Document.Node(Selection.Single());
            if (node.Instances.Length == 1) throw new InvalidOperationException("工件至少保留一个 PMT；如需删除工件，请选中标题栏。");
            UpdateNode(node with { Instances = node.Instances.Where(i => i.Id != instance).ToArray() });
            return;
        }
        Edit(d => d with
        {
            Nodes = d.Nodes.Where(n => !Selection.Contains(n.Id)).ToArray(),
            Connections = d.Connections.Where(c => !Selection.Contains(c.Id) && !Selection.Contains(c.SourceId) && !Selection.Contains(c.TargetId)).ToArray()
        });
    }
    public string? Copy()
    {
        var nodes = Document.Nodes.Where(n => Selection.Contains(n.Id)).ToArray();
        if (nodes.Length == 0) return null;
        if (SelectedInstanceId is { } id && nodes.Length == 1)
            nodes = [nodes[0] with { Name = "单个 PMT", Instances = nodes[0].Instances.Where(i => i.Id == id).ToArray() }];
        // PMTs retain references to machine sources; processing nodes only copy internal edges.
        var ids = nodes.Select(n => n.Id).ToHashSet();
        var edges = Document.Connections.Where(c => ids.Contains(c.TargetId) &&
            (ids.Contains(c.SourceId) || nodes.Any(n => n.Id == c.TargetId && n.Kind == ProcessingNodeKind.Pmt))).ToArray();
        _clipboard = System.Text.Json.JsonSerializer.Serialize(new ClipboardData("pmt-processing-clipboard-v1", nodes, edges), ProcessingDocument.JsonOptions);
        return _clipboard;
    }
    public void Paste(string? text = null)
    {
        text ??= _clipboard;
        if (string.IsNullOrWhiteSpace(text)) return;
        var data = System.Text.Json.JsonSerializer.Deserialize<ClipboardData>(text, ProcessingDocument.JsonOptions);
        if (data?.Format != "pmt-processing-clipboard-v1") throw new InvalidDataException("剪贴板不包含工作流节点。");
        var selected = new HashSet<string>();
        Edit(d =>
        {
            var map = data.Nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid().ToString("N"));
            var next = d.NextPmtNumber;
            var nodes = data.Nodes.Select(n => n with
            {
                Id = map[n.Id], X = n.X + 40, Y = n.Y + 40,
                Instances = n.Instances.Select(i => i with { Id = Guid.NewGuid().ToString("N"), Number = next++, Overrides = new(i.Overrides) }).ToArray()
            }).ToArray();
            selected.UnionWith(nodes.Select(n => n.Id));
            var edges = data.Connections.Where(c => map.ContainsKey(c.SourceId) || d.Nodes.Any(n => n.Id == c.SourceId && n.Kind == ProcessingNodeKind.Machine))
                .Select(c => c with { Id = Guid.NewGuid().ToString("N"), SourceId = map.GetValueOrDefault(c.SourceId, c.SourceId), TargetId = map[c.TargetId] }).ToArray();
            return d with { Nodes = d.Nodes.Concat(nodes).ToArray(), Connections = d.Connections.Concat(edges).ToArray(), NextPmtNumber = next };
        });
        Selection.Clear(); Selection.UnionWith(selected); SelectedInstanceId = null; Changed?.Invoke();
    }
    public sealed record ClipboardData(string Format, ProcessingNode[] Nodes, ProcessingConnection[] Connections);

    public ProcessingNode CreatePmt(string hatchId, string[]? layerIds, double width, double height)
    {
        var hatch = Document.Node(hatchId);
        if (hatch.Kind != ProcessingNodeKind.Hatch) throw new InvalidOperationException("请选中 Hatch 节点。");
        var machine = ProcessingNode.Create(ProcessingNodeKind.Machine, hatch.X + hatch.CanvasWidth + 50, hatch.Y);
        var pmt = ProcessingNode.Create(ProcessingNodeKind.Pmt, machine.X + machine.CanvasWidth + 50, hatch.Y) with
        {
            WorkpieceWidth = width + 10, WorkpieceHeight = height + 10,
            Instances = [new() { Number = Document.NextPmtNumber, X = 5, Y = 5, Width = width, Height = height }],
            Settings = new() { ["spacing"] = hatch.Setting("spacing", "0.02") }
        };
        Edit(d => d with
        {
            Nodes = d.Nodes.Concat([machine, pmt]).ToArray(), NextPmtNumber = d.NextPmtNumber + 1,
            Connections = d.Connections.Concat([
                new ProcessingConnection(Guid.NewGuid().ToString("N"), hatch.Id, machine.Id, layerIds?.ToArray()),
                new ProcessingConnection(Guid.NewGuid().ToString("N"), machine.Id, pmt.Id, null)]).ToArray()
        });
        Select(pmt.Id, instanceId: pmt.Instances[0].Id);
        return pmt;
    }
    public ProcessingNode Matrix(string nodeId, string instanceId, int rows, int columns, double gap, double width, double height)
    {
        var source = Document.Node(nodeId);
        var template = source.Instances.Single(i => i.Id == instanceId);
        if (rows < 1 || columns < 1 || (long)rows * columns > LaserPmtConfiguration.MaximumJobs || !double.IsFinite(gap) || gap < 0)
            throw new InvalidOperationException("矩阵行列、数量或间距无效（最多 1000 个 PMT）。");
        if (width < columns * template.Width + (columns - 1) * gap || height < rows * template.Height + (rows - 1) * gap)
            throw new InvalidOperationException("矩阵超出工件尺寸，请增大工件或减少行列与间距。");
        var startX = (width - columns * template.Width - (columns - 1) * gap) / 2;
        var startY = (height - rows * template.Height - (rows - 1) * gap) / 2;
        var next = Document.NextPmtNumber;
        var matrix = source with
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"矩阵工件 {rows} × {columns}", X = source.X, Y = source.Y + (source.CanvasHeight ?? 330) + 70,
            WorkpieceWidth = width, WorkpieceHeight = height,
            Instances = Enumerable.Range(0, rows * columns).Select(i => template with
            {
                Id = Guid.NewGuid().ToString("N"), Number = next++, X = startX + i % columns * (template.Width + gap),
                Y = startY + i / columns * (template.Height + gap), Overrides = new(template.Overrides)
            }).ToArray()
        };
        Edit(d => d with
        {
            Nodes = d.Nodes.Append(matrix).ToArray(), NextPmtNumber = next,
            Connections = d.Connections.Concat(d.Connections.Where(c => c.TargetId == source.Id)
                .Select(c => c with { Id = Guid.NewGuid().ToString("N"), TargetId = matrix.Id })).ToArray()
        });
        Select(matrix.Id);
        return matrix;
    }
}
