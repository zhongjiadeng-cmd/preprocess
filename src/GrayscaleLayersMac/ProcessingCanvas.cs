using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace GrayscaleLayersMac;

public sealed class ProcessingCanvas : Control
{
    public ProcessingSession Session { get; }
    public ProcessingExecutor? Executor { get; set; }
    public Func<string, string[]?>? SelectedLayers { get; set; }
    public event Action<string>? Error;
    public event Action<string, Point>? FileDropped;
    public event Action<string>? Shortcut;
    private readonly Dictionary<string, Bitmap> _images = new();
    private Point _start;
    private Point _last;
    private string? _dragNode;
    private string? _dragInstance;
    private string? _wireSource;
    private string[]? _wireLayers;
    private bool _panning;
    private string? _resizeNode;
    private Vector _delta;
    public const double NodeWidth = 280;
    public ProcessingCanvas(ProcessingSession session)
    {
        Session = session;
        ClipToBounds = true; Focusable = true; MinHeight = 300;
        session.Changed += InvalidateVisual;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = DragDropEffects.Copy; e.Handled = true; });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
#pragma warning disable CS0618
            var files = e.Data.GetFiles();
#pragma warning restore CS0618
            if (files is not null)
                foreach (var file in files)
                    if (file.TryGetLocalPath() is { } path) FileDropped?.Invoke(path, World(e.GetPosition(this)));
            e.Handled = true;
        });
    }
    public Point World(Point point) => new((point.X - Session.Document.PanX) / Session.Document.Zoom,
        (point.Y - Session.Document.PanY) / Session.Document.Zoom);
    public Point VisibleCenter => World(Bounds.Center);
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiTheme.SchemeChanged += ThemeChanged;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiTheme.SchemeChanged -= ThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }
    private void ThemeChanged(object? sender, EventArgs e) => InvalidateVisual();
    public static double NodeHeight(ProcessingNode node) => node.CanvasHeight ?? (node.Kind == ProcessingNodeKind.Pmt ? 330 : 235);
    private Rect NodeRect(ProcessingNode node) => new(node.X + (Session.Selection.Contains(node.Id) && _dragNode is not null && _dragInstance is null ? _delta.X : 0),
        node.Y + (Session.Selection.Contains(node.Id) && _dragNode is not null && _dragInstance is null ? _delta.Y : 0),
        Math.Max(240, node.CanvasWidth + (_resizeNode == node.Id ? _delta.X : 0)),
        Math.Max(200, NodeHeight(node) + (_resizeNode == node.Id ? _delta.Y : 0)));
    public static Rect WorkpieceRect(ProcessingNode node)
    {
        var scale = Math.Min((node.CanvasWidth - 32) / node.WorkpieceWidth, (NodeHeight(node) - 140) / node.WorkpieceHeight);
        return new(node.X + (node.CanvasWidth - node.WorkpieceWidth * scale) / 2, node.Y + 90, node.WorkpieceWidth * scale, node.WorkpieceHeight * scale);
    }
    private Point Port(ProcessingNode node, bool input, bool subset = false)
    {
        var rect = NodeRect(node);
        return new(input ? rect.Left : rect.Right, rect.Top + (subset ? 73 : 49));
    }
    private static bool LayerOutput(ProcessingNode node) => node.Kind is ProcessingNodeKind.Grayscale or ProcessingNodeKind.Hatch;
    public void FitAll()
    {
        if (Session.Document.Nodes.Length == 0) { Session.View(1, 40, 60); InvalidateVisual(); return; }
        var nodes = Session.Document.Nodes;
        var left = nodes.Min(n => n.X); var top = nodes.Min(n => n.Y);
        var width = nodes.Max(n => n.X + n.CanvasWidth) - left;
        var height = nodes.Max(n => n.Y + NodeHeight(n)) - top;
        var zoom = Math.Clamp(Math.Min((Bounds.Width - 80) / width, (Bounds.Height - 80) / height), .1, 1.3);
        Session.View(zoom, (Bounds.Width - width * zoom) / 2 - left * zoom, (Bounds.Height - height * zoom) / 2 - top * zoom);
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(UiTheme.SunkenBrush, Bounds.WithX(0).WithY(0));
        var doc = Session.Document;
        var grid = 32 * doc.Zoom;
        if (grid > 10)
        {
            using var opacity = context.PushOpacity(.5);
            for (var x = doc.PanX % grid; x < Bounds.Width; x += grid)
                for (var y = doc.PanY % grid; y < Bounds.Height; y += grid)
                    context.DrawEllipse(UiTheme.TextFaintBrush, null, new Point(x, y), .65, .65);
        }
        Text(context, "PROCESS GRAPH", new(18, 12), 10, UiTheme.TextFaintBrush);
        Text(context, $"{doc.Nodes.Length:00} 节点  /  {doc.Connections.Length:00} 连接", new(18, 28), 11, UiTheme.TextSecondaryBrush);
        var hud = new Rect(14, Bounds.Height - 34, 184, 24);
        context.DrawRectangle(UiTheme.PanelBrush, new Pen(UiTheme.BorderSubtleBrush), hud, 3, 3);
        Text(context, $"{doc.Zoom * 100:0}%   ·   滚轮缩放 / F 适应", hud.TopLeft + new Vector(9, 4), 10, UiTheme.TextSecondaryBrush, 172);
        using var transform = context.PushTransform(Matrix.CreateScale(doc.Zoom, doc.Zoom) * Matrix.CreateTranslation(doc.PanX, doc.PanY));
        foreach (var edge in doc.Connections)
        {
            var start = Port(doc.Node(edge.SourceId), false, edge.LayerIds is not null);
            var end = Port(doc.Node(edge.TargetId), true);
            var routeY = WireRoute(edge);
            DrawWire(context, start, end, Session.Selection.Contains(edge.Id) ? UiTheme.AccentBrush : UiTheme.TextFaintBrush, routeY);
            var label = WireLabel(start, end, routeY);
            context.DrawRectangle(UiTheme.PanelBrush, new Pen(UiTheme.BorderSubtleBrush), label, 3, 3);
            Text(context, edge.LayerIds is null ? "全部层" : $"{edge.LayerIds.Length} 层", label.TopLeft + new Vector(8, 3), 10, UiTheme.TextSecondaryBrush, 46);
        }
        if (_wireSource is { } wire) DrawWire(context, Port(doc.Node(wire), false, _wireLayers is not null), _last, UiTheme.AccentBrush);
        foreach (var node in doc.Nodes)
        {
            var rect = NodeRect(node);
            var selected = Session.Selection.Contains(node.Id);
            using (context.PushOpacity(.14)) context.DrawRectangle(Brushes.Black, null, rect.Translate(new Vector(0, 3)), 5, 5);
            context.DrawRectangle(UiTheme.CardBrush, new Pen(selected ? UiTheme.FocusRingBrush : UiTheme.BorderMediumBrush, selected ? 1.8 : 1), rect, 5, 5);
            using (context.PushClip(new RoundedRect(rect, 5)))
                context.FillRectangle(UiTheme.BarBrush, new Rect(rect.X, rect.Y, rect.Width, 35));
            var badge = new Rect(rect.X + 10, rect.Y + 8, 36, 20);
            context.DrawRectangle(selected ? UiTheme.SelectionBrush : UiTheme.GhostBrush, new Pen(UiTheme.BorderSubtleBrush), badge, 3, 3);
            Text(context, NodeCode(node.Kind), badge.TopLeft + new Vector(5, 4), 9, selected ? UiTheme.InfoTextBrush : UiTheme.TextSecondaryBrush, 29);
            Text(context, node.Name, rect.TopLeft + new Vector(54, 10), 13, UiTheme.TextPrimaryBrush, rect.Width - 72);
            context.DrawLine(new Pen(UiTheme.BorderSubtleBrush, 1), new(rect.Left, rect.Top + 35), new(rect.Right, rect.Top + 35));
            var grip = Session.Selection.Contains(node.Id) ? UiTheme.AccentBrush : UiTheme.TextFaintBrush;
            for (var offset = 5; offset <= 11; offset += 6)
                context.DrawLine(new Pen(grip, 1.5), new(rect.Right - 5 - offset, rect.Bottom - 5), new(rect.Right - 5, rect.Bottom - 5 - offset));
            var status = Executor?.Status(doc, node.Id) ?? "未运行";
            var statusBrush = status switch { "失败" => UiTheme.DangerTextBrush, "已完成" => UiTheme.SuccessTextBrush, "运行中" => UiTheme.InfoTextBrush, _ => UiTheme.TextFaintBrush };
            context.DrawLine(new Pen(UiTheme.BorderSubtleBrush), new(rect.Left + 1, rect.Bottom - 29), new(rect.Right - 1, rect.Bottom - 29));
            context.DrawEllipse(statusBrush, null, new(rect.Left + 14, rect.Bottom - 14), 2.5, 2.5);
            Text(context, status, new(rect.Left + 23, rect.Bottom - 21), 10, statusBrush);
            if (node.Kind != ProcessingNodeKind.Texture)
            {
                var compatible = _wireSource is { } source && ProcessingDocument.CanConnect(doc.Node(source).Kind, node.Kind) && !doc.Connections.Any(c => c.TargetId == node.Id);
                context.DrawEllipse(compatible ? UiTheme.SelectionBrush : UiTheme.CardBrush, new Pen(compatible ? UiTheme.FocusRingBrush : UiTheme.TextFaintBrush, 1.5), Port(node, true), compatible ? 8 : 5, compatible ? 8 : 5);
                Text(context, "输入", new(rect.Left + 12, rect.Top + 42), 10, UiTheme.TextSecondaryBrush);
            }
            if (node.Kind != ProcessingNodeKind.Pmt)
            {
                context.DrawEllipse(UiTheme.InfoTextBrush, new Pen(UiTheme.CardBrush, 1.5), Port(node, false), 5, 5);
                Text(context, LayerOutput(node) ? "全部层" : "输出", new(rect.Right - 49, rect.Top + 42), 10, UiTheme.TextSecondaryBrush);
                if (LayerOutput(node))
                {
                    context.DrawEllipse(UiTheme.CardBrush, new Pen(UiTheme.InfoTextBrush, 1.5), Port(node, false, true), 5, 5);
                    Text(context, "选中层", new(rect.Right - 49, rect.Top + 66), 10, UiTheme.TextSecondaryBrush);
                }
            }
            if (node.Kind == ProcessingNodeKind.Pmt) DrawWorkpiece(context, node, rect);
            else
            {
                var result = Executor?.States.GetValueOrDefault(node.Id)?.Result;
                var preview = result?.Layers.FirstOrDefault()?.PreviewPath;
                if (preview is not null && File.Exists(preview))
                {
                    try
                    {
                        if (!_images.TryGetValue(preview, out var bitmap))
                        {
                            if (_images.Count >= 32) { foreach (var item in _images.Values) item.Dispose(); _images.Clear(); }
                            using var stream = File.OpenRead(preview);
                            bitmap = Bitmap.DecodeToWidth(stream, 500); _images[preview] = bitmap;
                        }
                        var area = new Rect(rect.Left + 12, rect.Top + 91, rect.Width - 24, rect.Height - 131);
                        context.DrawRectangle(UiTheme.SunkenBrush, new Pen(UiTheme.BorderSubtleBrush), area, 2, 2);
                        var scale = Math.Min((area.Width - 8) / bitmap.Size.Width, (area.Height - 8) / bitmap.Size.Height);
                        context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(area.Center.X - bitmap.Size.Width * scale / 2, area.Center.Y - bitmap.Size.Height * scale / 2, bitmap.Size.Width * scale, bitmap.Size.Height * scale));
                    }
                    catch (Exception e) when (e is IOException or ArgumentException) { }
                }
                else
                {
                    DrawParameters(context, node, rect);
                }
                if (result?.Layers.Length > 0)
                    Text(context, $"{result.Layers.Length} 层", new(rect.Right - 60, rect.Bottom - 21), 10, UiTheme.TextSecondaryBrush);
            }
        }
        if (doc.Nodes.Length == 0)
        {
            var center = World(Bounds.Center);
            Text(context, "构建加工流程", center - new Vector(100 / doc.Zoom, 55 / doc.Zoom), 22 / doc.Zoom, UiTheme.TextPrimaryBrush, 320 / doc.Zoom);
            Text(context, "拖入 TIFF 纹理，或从工具栏导入文件", center - new Vector(145 / doc.Zoom, 13 / doc.Zoom), 13 / doc.Zoom, UiTheme.TextSecondaryBrush, 420 / doc.Zoom);
            Text(context, "纹理  →  灰度分层  →  DXF Hatch  →  Machine  →  PMT", center - new Vector(210 / doc.Zoom, -23 / doc.Zoom), 11 / doc.Zoom, UiTheme.TextFaintBrush, 460 / doc.Zoom);
        }
    }
    private static string NodeCode(ProcessingNodeKind kind) => kind switch
    { ProcessingNodeKind.Texture => "TIFF", ProcessingNodeKind.Grayscale => "GRAY", ProcessingNodeKind.Hatch => "DXF", ProcessingNodeKind.Machine => "CNC", _ => "PMT" };
    private static void DrawParameters(DrawingContext context, ProcessingNode node, Rect rect)
    {
        (string Label, string Value)[] rows = node.Kind switch
        {
            ProcessingNodeKind.Texture => [("源文件", Path.GetFileName(node.Setting("path", "未导入"))), ("备用分辨率", node.Setting("dpi") + " DPI")],
            ProcessingNodeKind.Grayscale => [("分层数量", node.Setting("layers") + " 层"), ("灰阶范围", node.Setting("min-level") + " – " + node.Setting("max-level")), ("分层模式", "累计阈值")],
            ProcessingNodeKind.Hatch => [("加工尺寸", node.Setting("width") + " × " + node.Setting("height") + " mm"), ("线间距", node.Setting("spacing") + " mm"), ("分块数量", node.Setting("blocks") + " 块")],
            _ when node.Setting("path").Length > 0 => [("来源", "已导入 Machine"), ("参数", "继承加工文件")],
            _ => [("激光功率", node.Setting("power")), ("扫描速度", node.Setting("scan-speed")), ("层间进给", node.Setting("layer-step-um") + " μm")]
        };
        var spacing = Math.Min(29, (rect.Height - 126) / rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var y = rect.Top + 95 + index * spacing;
            Text(context, rows[index].Label, new(rect.Left + 14, y), 11, UiTheme.TextFaintBrush, 85);
            Text(context, rows[index].Value, new(rect.Left + 106, y), 11, UiTheme.TextPrimaryBrush, rect.Width - 120);
        }
    }
    private void DrawWorkpiece(DrawingContext context, ProcessingNode node, Rect rect)
    {
        var workpiece = WorkpieceRect(node with { X = rect.X, Y = rect.Y, CanvasWidth = rect.Width, CanvasHeight = rect.Height });
        var scale = workpiece.Width / node.WorkpieceWidth;
        context.DrawRectangle(UiTheme.SunkenBrush, new Pen(UiTheme.BorderStrongBrush, 1), workpiece);
        using (context.PushOpacity(.4))
        {
            var pen = new Pen(UiTheme.BorderSubtleBrush);
            for (var step = 1; step < 4; step++)
            {
                context.DrawLine(pen, new(workpiece.Left + workpiece.Width * step / 4, workpiece.Top), new(workpiece.Left + workpiece.Width * step / 4, workpiece.Bottom));
                context.DrawLine(pen, new(workpiece.Left, workpiece.Top + workpiece.Height * step / 4), new(workpiece.Right, workpiece.Top + workpiece.Height * step / 4));
            }
        }
        foreach (var item in node.Instances)
        {
            var dx = _dragInstance == item.Id ? _delta.X / scale : 0;
            var dy = _dragInstance == item.Id ? _delta.Y / scale : 0;
            var bounds = new Rect(workpiece.Left + (item.X + dx) * scale, workpiece.Top + (item.Y + dy) * scale, item.Width * scale, item.Height * scale);
            var bad = item.X + dx < 0 || item.Y + dy < 0 || item.X + dx + item.Width > node.WorkpieceWidth || item.Y + dy + item.Height > node.WorkpieceHeight ||
                node.Instances.Any(other => other.Id != item.Id && item.X + dx < other.X + other.Width && item.X + dx + item.Width > other.X && item.Y + dy < other.Y + other.Height && item.Y + dy + item.Height > other.Y);
            var brush = bad ? UiTheme.DangerTextBrush : UiTheme.AccentBrush;
            context.DrawRectangle(UiTheme.SelectionBrush, new Pen(brush, Session.SelectedInstanceId == item.Id ? 2 : 1), bounds, 1, 1);
            if (bounds.Width > 14 && bounds.Height > 14) Text(context, item.Number.ToString(), bounds.TopLeft + new Vector(3, 2), 10, brush);
        }
        Text(context, $"{node.WorkpieceWidth:0.##} × {node.WorkpieceHeight:0.##} mm · {node.Instances.Length} PMT", new(rect.Left + 16, rect.Bottom - 49), 11, UiTheme.TextSecondaryBrush);
    }
    private static void Text(DrawingContext context, string text, Point origin, double size, IBrush brush, double maxWidth = 260)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(UiTheme.UiFont), size, brush) { MaxTextWidth = maxWidth, MaxTextHeight = 70, Trimming = TextTrimming.CharacterEllipsis };
        context.DrawText(formatted, origin);
    }
    private double? WireRoute(ProcessingConnection edge)
    {
        var source = NodeRect(Session.Document.Node(edge.SourceId));
        var target = NodeRect(Session.Document.Node(edge.TargetId));
        if (target.Left > source.Right + 30) return null;
        if (target.Top > source.Bottom + 24) return (source.Bottom + target.Top) / 2;
        if (source.Top > target.Bottom + 24) return (target.Bottom + source.Top) / 2;
        return Math.Min(source.Top, target.Top) - 28 - Array.IndexOf(Session.Document.Connections, edge) * 8;
    }
    private static Rect WireLabel(Point start, Point end, double? routeY) =>
        new((start.X + end.X) / 2 - 10, routeY is { } y ? y - 10 : (start.Y + end.Y) / 2 - 22, 58, 20);
    private static void DrawWire(DrawingContext context, Point start, Point end, IBrush brush, double? routeY = null)
    {
        var distance = Math.Max(70, Math.Abs(end.X - start.X) * .45);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            if (routeY is { } y)
            {
                path.CubicBezierTo(new(start.X + 32, start.Y), new(start.X + 32, y), new(start.X + 16, y));
                path.LineTo(new(end.X - 16, y));
                path.CubicBezierTo(new(end.X - 32, y), new(end.X - 32, end.Y), end);
            }
            else path.CubicBezierTo(start + new Vector(distance, 0), end - new Vector(distance, 0), end);
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 1.5), geometry);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus();
        var p = World(e.GetPosition(this)); _start = _last = p; _delta = default;
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed)
        { _panning = true; _start = e.GetPosition(this); e.Pointer.Capture(this); e.Handled = true; return; }
        if (!properties.IsLeftButtonPressed) return;
        foreach (var node in Session.Document.Nodes.Reverse())
        {
            if (ResizeGrip(node).Contains(p))
            { Session.Select(node.Id); _resizeNode = node.Id; e.Pointer.Capture(this); e.Handled = true; return; }
            foreach (var subset in new[] { false, true })
                if (node.Kind != ProcessingNodeKind.Pmt && (!subset || LayerOutput(node)) && Distance(p, Port(node, false, subset)) < 12)
                {
                    _wireLayers = subset ? SelectedLayers?.Invoke(node.Id) : null;
                    if (subset && (_wireLayers is null || _wireLayers.Length == 0)) { Error?.Invoke("请先在右侧勾选需要传递的层。"); return; }
                    _wireSource = node.Id; e.Pointer.Capture(this); e.Handled = true; return;
                }
            if (!NodeRect(node).Contains(p)) continue;
            string? instanceId = null;
            if (node.Kind == ProcessingNodeKind.Pmt)
            {
                var work = WorkpieceRect(node); var scale = work.Width / node.WorkpieceWidth;
                instanceId = node.Instances.LastOrDefault(i => new Rect(work.Left + i.X * scale, work.Top + i.Y * scale, i.Width * scale, i.Height * scale).Contains(p))?.Id;
            }
            if (!Session.Selection.Contains(node.Id) || instanceId is not null || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                Session.Select(node.Id, e.KeyModifiers.HasFlag(KeyModifiers.Shift), instanceId);
            else if (Session.SelectedInstanceId is not null) Session.Select(node.Id);
            if (Session.Selection.Contains(node.Id)) { _dragNode = node.Id; _dragInstance = instanceId; e.Pointer.Capture(this); }
            e.Handled = true; return;
        }
        // The connection label is also a predictable hit target.
        foreach (var edge in Session.Document.Connections)
        {
            var a = Port(Session.Document.Node(edge.SourceId), false, edge.LayerIds is not null); var b = Port(Session.Document.Node(edge.TargetId), true);
            if (WireLabel(a, b, WireRoute(edge)).Inflate(5).Contains(p))
            { Session.Select(edge.Id); e.Handled = true; return; }
        }
        Session.Select(null); _panning = true; _start = e.GetPosition(this); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); _last = World(e.GetPosition(this));
        if (_panning)
        {
            var screen = e.GetPosition(this); var delta = screen - _start; _start = screen;
            Session.View(Session.Document.Zoom, Session.Document.PanX + delta.X, Session.Document.PanY + delta.Y);
        }
        else if (_dragNode is not null || _resizeNode is not null) _delta = _last - _start;
        Cursor = new Cursor(_resizeNode is not null || Session.Document.Nodes.Any(n => ResizeGrip(n).Contains(_last))
            ? StandardCursorType.BottomRightCorner : _panning || _dragNode is not null ? StandardCursorType.SizeAll : StandardCursorType.Arrow);
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        try
        {
            if (_resizeNode is { } resized && _delta.Length > .01)
            {
                var node = Session.Document.Node(resized); var rect = NodeRect(node);
                Session.UpdateNode(node with { CanvasWidth = rect.Width, CanvasHeight = rect.Height });
            }
            if (_wireSource is { } source)
            {
                var p = World(e.GetPosition(this));
                var target = Session.Document.Nodes.LastOrDefault(n => n.Kind != ProcessingNodeKind.Texture && Distance(p, Port(n, true)) < 18);
                if (target is not null) Session.Connect(source, target.Id, _wireLayers);
            }
            if (_dragNode is { } id && _delta.Length > .01)
            {
                var delta = _delta;
                if (_dragInstance is { } instance)
                {
                    var node = Session.Document.Node(id); var scale = WorkpieceRect(node).Width / node.WorkpieceWidth;
                    Session.UpdateNode(node with { Instances = node.Instances.Select(i => i.Id == instance ? i with { X = i.X + delta.X / scale, Y = i.Y + delta.Y / scale } : i).ToArray() });
                }
                else Session.Edit(d => d with { Nodes = d.Nodes.Select(n => Session.Selection.Contains(n.Id) ? n with { X = n.X + delta.X, Y = n.Y + delta.Y } : n).ToArray() });
            }
        }
        catch (Exception error) { Error?.Invoke(error.Message); }
        ResetGesture(); e.Pointer.Capture(null); e.Handled = true;
    }
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private Rect ResizeGrip(ProcessingNode node)
    {
        var rect = NodeRect(node); var size = Math.Min(24, 14 / Session.Document.Zoom);
        return new Rect(rect.Right - size, rect.Bottom - size, size, size);
    }
    private void ResetGesture() { _panning = false; _dragNode = _dragInstance = _wireSource = _resizeNode = null; _delta = default; Cursor = new Cursor(StandardCursorType.Arrow); InvalidateVisual(); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); ResetGesture(); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var position = e.GetPosition(this); var world = World(position);
        var zoom = Math.Clamp(Session.Document.Zoom * Math.Pow(1.12, e.Delta.Y), .1, 4);
        Session.View(zoom, position.X - world.X * zoom, position.Y - world.Y * zoom);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { ResetGesture(); e.Handled = true; return; }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None) { FitAll(); e.Handled = true; return; }
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        string? action = command ? e.Key switch
        {
            Key.C => "copy", Key.V => "paste", Key.Z => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "redo" : "undo", Key.Y => "redo", Key.A => "all", _ => null
        } : e.Key is Key.Delete or Key.Back ? "delete" : null;
        if (action is not null) { Shortcut?.Invoke(action); e.Handled = true; }
    }
    public void ClearImages() { foreach (var bitmap in _images.Values) bitmap.Dispose(); _images.Clear(); InvalidateVisual(); }
}
