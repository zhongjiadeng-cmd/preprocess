using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrayscaleLayersMac;

public enum ProcessingNodeKind { Texture, Grayscale, Hatch, Machine, Pmt }

public sealed record ProcessingPmtInstance
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int Number { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 10;
    public double Height { get; init; } = 10;
    public Dictionary<string, string> Overrides { get; init; } = new();
}

public sealed record ProcessingNode
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ProcessingNodeKind Kind { get; init; }
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double CanvasWidth { get; init; } = 280;
    public double? CanvasHeight { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new();
    public ProcessingPmtInstance[] Instances { get; init; } = [];
    public double WorkpieceWidth { get; init; } = 100;
    public double WorkpieceHeight { get; init; } = 100;
    public string Setting(string key, string fallback = "") => Settings.GetValueOrDefault(key, fallback);
    public static ProcessingNode Create(ProcessingNodeKind kind, double x, double y) => new()
    {
        Kind = kind, X = x, Y = y,
        Name = kind switch
        {
            ProcessingNodeKind.Texture => "TIFF 纹理",
            ProcessingNodeKind.Grayscale => "灰度分层",
            ProcessingNodeKind.Hatch => "DXF Hatch",
            ProcessingNodeKind.Machine => "Machine",
            _ => "单个 PMT"
        },
        Settings = kind switch
        {
            ProcessingNodeKind.Texture => new() { ["dpi"] = "300" },
            ProcessingNodeKind.Grayscale => new() { ["layers"] = "10", ["min-level"] = "0", ["max-level"] = "255", ["below-is-white"] = "false" },
            ProcessingNodeKind.Hatch => new()
            {
                ["width"] = "10", ["height"] = "10", ["spacing"] = "0.02", ["angle-step"] = "0",
                ["blocks"] = "9", ["boundary-blur"] = "0.3", ["boundary-correlation"] = "1", ["seed"] = "12345",
                ["min-area-percent"] = "5", ["max-area-percent"] = "18", ["bidirectional"] = "false",
                ["anchor"] = "center", ["tile-mode"] = "unit", ["threshold"] = "128", ["border"] = "false"
            },
            ProcessingNodeKind.Machine => new()
            {
                ["layer-step-um"] = "3", ["block-center-positioning"] = "true",
                ["power"] = "38", ["frequency"] = "350", ["pulse-width-idx"] = "3", ["scan-speed"] = "2100",
                ["jump-vel"] = "6000", ["jump-delay"] = "50", ["scan-ahead"] = "true", ["acc-scale"] = "50",
                ["corner-scale"] = "100", ["end-scale"] = "100", ["sky-writing"] = "true", ["time-lag"] = "100",
                ["laser-on-shift"] = "18", ["delaseroff"] = "32", ["delaseron"] = "0"
            },
            _ => new() { ["spacing"] = "0.02" }
        }
    };
}

// null means all current layers; an explicit list is a stable subset in source order.
public sealed record ProcessingConnection(string Id, string SourceId, string TargetId, string[]? LayerIds);
public sealed record ProcessingLayer(string Id, string Name, string Path, int Order, string? PreviewPath = null);

public sealed record ProcessingDocument
{
    public int Version { get; init; } = 1;
    public ProcessingNode[] Nodes { get; init; } = [];
    public ProcessingConnection[] Connections { get; init; } = [];
    public int NextPmtNumber { get; init; } = 1;
    public double Zoom { get; init; } = 1;
    public double PanX { get; init; } = 40;
    public double PanY { get; init; } = 60;
    public string OutputDirectory { get; init; } = "";

    public ProcessingNode Node(string id) => Nodes.FirstOrDefault(n => n.Id == id)
        ?? throw new InvalidOperationException("找不到节点。");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static ProcessingDocument FromJson(string json)
    {
        var document = JsonSerializer.Deserialize<ProcessingDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("项目文件为空。");
        document.Validate();
        return document;
    }

    public void Validate()
    {
        if (Version != 1) throw new InvalidDataException("不支持的处理图版本。");
        if (Nodes is null || Connections is null || Nodes.Length > 5000 || !double.IsFinite(Zoom) || Zoom < .1 || Zoom > 4 ||
            !double.IsFinite(PanX) || !double.IsFinite(PanY)) throw new InvalidDataException("画布数据无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<int>();
        foreach (var node in Nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id) || !Enum.IsDefined(node.Kind) ||
                !double.IsFinite(node.X) || !double.IsFinite(node.Y) || node.Settings is null || node.Instances is null ||
                !Positive(node.WorkpieceWidth) || !Positive(node.WorkpieceHeight) ||
                !double.IsFinite(node.CanvasWidth) || node.CanvasWidth < 240 ||
                (node.CanvasHeight is { } height && (!double.IsFinite(height) || height < 200)))
                throw new InvalidDataException("节点数据无效或 ID 重复。");
            if (node.Instances.Length > LaserPmtConfiguration.MaximumJobs || (node.Kind != ProcessingNodeKind.Pmt && node.Instances.Length != 0))
                throw new InvalidDataException("PMT 数量或所属节点无效。");
            foreach (var pmt in node.Instances)
                if (pmt is null || !ids.Add(pmt.Id) || !numbers.Add(pmt.Number) || pmt.Number < 1 ||
                    !double.IsFinite(pmt.X) || !double.IsFinite(pmt.Y) || !Positive(pmt.Width) || !Positive(pmt.Height) || pmt.Overrides is null)
                    throw new InvalidDataException("PMT 数据或编号无效。");
        }
        if (NextPmtNumber < 1 || numbers.Any(n => n >= NextPmtNumber)) throw new InvalidDataException("下一 PMT 编号无效。");
        var targets = new HashSet<string>();
        foreach (var edge in Connections)
        {
            if (edge is null || !ids.Add(edge.Id) || !targets.Add(edge.TargetId)) throw new InvalidDataException("连线重复，每个处理节点只能接收一个层集合。");
            var source = Node(edge.SourceId);
            var target = Node(edge.TargetId);
            if (!CanConnect(source.Kind, target.Kind) || source.Id == target.Id) throw new InvalidDataException("端口类型不匹配。");
            if (edge.LayerIds is not null && (edge.LayerIds.Length == 0 || edge.LayerIds.Distinct().Count() != edge.LayerIds.Length ||
                edge.LayerIds.Any(string.IsNullOrWhiteSpace) || source.Kind is ProcessingNodeKind.Machine or ProcessingNodeKind.Pmt))
                throw new InvalidDataException("层选择无效。");
        }
        foreach (var node in Nodes) ExecutionOrder(node.Id);
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    public static bool CanConnect(ProcessingNodeKind source, ProcessingNodeKind target) => (source, target) switch
    {
        (ProcessingNodeKind.Texture, ProcessingNodeKind.Grayscale or ProcessingNodeKind.Hatch) => true,
        (ProcessingNodeKind.Grayscale, ProcessingNodeKind.Hatch) => true,
        (ProcessingNodeKind.Hatch, ProcessingNodeKind.Machine) => true,
        (ProcessingNodeKind.Machine, ProcessingNodeKind.Pmt) => true,
        _ => false
    };

    public IReadOnlyList<ProcessingNode> ExecutionOrder(string id)
    {
        var result = new List<ProcessingNode>();
        var active = new HashSet<string>();
        void Visit(string current)
        {
            if (!active.Add(current)) throw new InvalidDataException("处理图不能包含循环。");
            var node = Node(current);
            var edge = Connections.SingleOrDefault(c => c.TargetId == current);
            if (edge is not null) Visit(edge.SourceId);
            result.Add(node);
        }
        Visit(id);
        return result;
    }

    public static ProcessingLayer[] SelectLayers(ProcessingLayer[] layers, string[]? selection)
    {
        if (selection is null) return layers.OrderBy(l => l.Order).ToArray();
        if (selection.Length == 0 || selection.Any(id => !layers.Any(l => l.Id == id)))
            throw new InvalidOperationException("连接选中的层已不存在，请重新选择；不会自动替换层。");
        return layers.Where(l => selection.Contains(l.Id)).OrderBy(l => l.Order).ToArray();
    }
}
