using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class LaserPmtWorkflowCanvas : Control
{
    private const double BaseNodeWidth = 58;
    private const double ParameterNodeWidth = 58;
    private LaserPmtWorkflow? _workflow;
    private LaserPmtCanvasViewport _viewport = new(1, 0, 0);
    private Point? _panStart;
    private LaserPmtCanvasViewport _viewportAtPanStart;

    public LaserPmtWorkflow? Workflow => _workflow;
    public LaserPmtCanvasViewport Viewport => _viewport;
    public event EventHandler? ViewChanged;

    public LaserPmtWorkflowCanvas()
    {
        MinHeight = 360;
        ClipToBounds = true;
        Focusable = true;
    }

    public void Load(LaserPmtWorkflow workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _viewport = workflow.Viewport;
        InvalidateVisual();
    }

    public void Clear()
    {
        _workflow = null;
        _viewport = new LaserPmtCanvasViewport(1, 0, 0);
        InvalidateVisual();
    }

    public void FitWorkpiece()
    {
        if (_workflow is null)
            return;
        _viewport = LaserPmtWorkflowViewMath.FitBounds(
            ToRect(_workflow.Workpiece), Bounds.Size, 36);
        NotifyViewChanged();
    }

    public void FitAll()
    {
        if (_workflow is null)
            return;
        var bounds = ToRect(_workflow.Workpiece).Union(BaseNodeRect(_workflow.BaseNode));
        foreach (var node in _workflow.ParameterNodes)
            bounds = bounds.Union(ParameterNodeRect(node));
        _viewport = LaserPmtWorkflowViewMath.FitBounds(bounds, Bounds.Size, 36);
        NotifyViewChanged();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(UiTheme.SunkenBrush, new Rect(Bounds.Size));
        if (_workflow is null)
        {
            DrawText(context, "创建或导入 PMT 工作流后将在这里显示节点画布", Bounds.Center, 13,
                UiTheme.TextSecondaryBrush, centered: true);
            return;
        }

        var compilation = LaserPmtWorkflowCompiler.Compile(_workflow);
        var invalidTargets = compilation.Errors
            .Where(error => error.TargetId is not null)
            .Select(error => error.TargetId!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var geometryError in LaserPmtWorkflowEditor.ValidateGeometry(_workflow))
        {
            invalidTargets.Add(geometryError.TargetId);
            if (geometryError.OtherTargetId is not null)
                invalidTargets.Add(geometryError.OtherTargetId);
        }

        DrawWorkpiece(context);
        DrawBaseBus(context);
        DrawConnections(context);
        foreach (var target in _workflow.Targets)
            DrawTarget(context, target, invalidTargets.Contains(target.Id));
        DrawBaseNode(context, _workflow.BaseNode);
        foreach (var node in _workflow.ParameterNodes)
            DrawParameterNode(context, node);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_workflow is null)
            return;
        var factor = e.Delta.Y > 0 ? 1.18 : 1 / 1.18;
        _viewport = LaserPmtWorkflowViewMath.ZoomAt(
            _viewport,
            e.GetPosition(this),
            Bounds.Size,
            _viewport.Zoom * factor);
        NotifyViewChanged();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (_workflow is null ||
            (!point.Properties.IsMiddleButtonPressed && !point.Properties.IsLeftButtonPressed))
            return;
        _panStart = e.GetPosition(this);
        _viewportAtPanStart = _viewport;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panStart is not { } start)
            return;
        var delta = e.GetPosition(this) - start;
        _viewport = _viewportAtPanStart with
        {
            PanX = _viewportAtPanStart.PanX + delta.X,
            PanY = _viewportAtPanStart.PanY + delta.Y
        };
        NotifyViewChanged();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panStart is null)
            return;
        _panStart = null;
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Arrow);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panStart = null;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void DrawWorkpiece(DrawingContext context)
    {
        var rect = ScreenRect(ToRect(_workflow!.Workpiece));
        context.DrawRectangle(UiTheme.CardBrush, new Pen(UiTheme.BorderStrongBrush, 1.5), rect);
        DrawText(context, "工件 · (0, 0)", new Point(rect.Left, rect.Top - 16), 11,
            UiTheme.TextSecondaryBrush);
    }

    private void DrawTarget(
        DrawingContext context,
        LaserPmtWorkflowTarget target,
        bool invalid)
    {
        var rect = ScreenRect(ToRect(target.Bounds));
        var pen = new Pen(invalid ? UiTheme.DangerBrush : UiTheme.BorderMediumBrush, invalid ? 2 : 1);
        var fill = invalid ? UiTheme.GhostPressedBrush : UiTheme.GhostBrush;
        context.DrawRectangle(fill, pen, rect);
        var label = target switch
        {
            LaserPmtTarget pmt =>
                $"{_workflow!.Numbering.Prefix}{pmt.Number.ToString($"D{_workflow.Numbering.Padding}", CultureInfo.InvariantCulture)}",
            LaserPmtTimestampTarget timestamp => timestamp.Text,
            _ => target.Id
        };
        if (target is LaserPmtTimestampTarget && rect.Height >= 8)
        {
            var spacing = Math.Max(3, _workflow!.HatchSpacing * _viewport.Zoom);
            for (var y = rect.Top + spacing / 2; y < rect.Bottom; y += spacing)
                context.DrawLine(new Pen(UiTheme.BorderSubtleBrush, 1),
                    new Point(rect.Left + 2, y), new Point(rect.Right - 2, y));
        }
        if (rect.Width >= 18 && rect.Height >= 12)
            DrawText(context, label, rect.Center, 10.5,
                invalid ? UiTheme.DangerTextBrush : UiTheme.TextPrimaryBrush, centered: true);
    }

    private void DrawBaseNode(DrawingContext context, LaserPmtBaseParameterNode node)
    {
        var rect = ScreenRect(BaseNodeRect(node));
        context.DrawRectangle(UiTheme.CardBrush, new Pen(UiTheme.AccentBrush, 1.5), rect, 6, 6);
        DrawText(context, "基础参数", new Point(rect.Left + 8, rect.Top + 6), 11.5,
            UiTheme.TextPrimaryBrush);
        DrawText(context,
            $"启用 {node.Parameters.Count - node.RemovedParameters.Count} · 移除 {node.RemovedParameters.Count}",
            new Point(rect.Left + 8, rect.Top + 23), 9.5, UiTheme.TextSecondaryBrush);
    }

    private void DrawParameterNode(DrawingContext context, LaserPmtSingleParameterNode node)
    {
        var worldRect = ParameterNodeRect(node);
        var rect = ScreenRect(worldRect);
        context.DrawRectangle(UiTheme.CardBrush, new Pen(UiTheme.BorderStrongBrush, 1.2), rect, 6, 6);
        var definition = LaserPmtConfiguration.Parameters.FirstOrDefault(item => item.Name == node.ParameterName);
        DrawText(context, definition?.DisplayName ?? node.ParameterName,
            new Point(rect.Left + 7, rect.Top + 5), 10.5, UiTheme.TextPrimaryBrush);
        DrawText(context, node.ValuesText, new Point(rect.Left + 7, rect.Top + 20), 9,
            UiTheme.TextSecondaryBrush);
        for (var index = 0; index < node.Ports.Count; index++)
        {
            var port = ScreenPoint(ParameterPortPoint(node, index));
            context.DrawEllipse(UiTheme.AccentBrush, new Pen(UiTheme.AccentTextBrush, 1), port, 4, 4);
            DrawText(context, (index + 1).ToString(CultureInfo.InvariantCulture),
                new Point(port.X - 10, port.Y - 6), 9, UiTheme.TextPrimaryBrush, centered: true);
        }
    }

    private void DrawBaseBus(DrawingContext context)
    {
        var node = ScreenRect(BaseNodeRect(_workflow!.BaseNode));
        var workpiece = ScreenRect(ToRect(_workflow.Workpiece));
        var start = new Point(node.Right, node.Center.Y);
        var end = new Point(workpiece.Left, workpiece.Center.Y);
        context.DrawLine(new Pen(UiTheme.AccentBrush, 1.5), start, end);
        DrawText(context, "基础", new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2 - 12),
            9, UiTheme.AccentBrush, centered: true);
    }

    private void DrawConnections(DrawingContext context)
    {
        var nodes = _workflow!.ParameterNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var targets = _workflow.Targets.ToDictionary(target => target.Id, StringComparer.Ordinal);
        foreach (var connection in _workflow.Connections)
        {
            if (!nodes.TryGetValue(connection.SourceNodeId, out var node) ||
                !targets.TryGetValue(connection.TargetId, out var target))
                continue;
            var portIndex = node.Ports.ToList().FindIndex(port => port.Id == connection.SourcePortId);
            if (portIndex < 0)
                continue;
            var start = ScreenPoint(ParameterPortPoint(node, portIndex));
            var targetRect = ScreenRect(ToRect(target.Bounds));
            var end = new Point(targetRect.Left, targetRect.Center.Y);
            var offset = Math.Max(24, Math.Abs(end.X - start.X) * 0.42);
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(start, false);
                path.CubicBezierTo(
                    new Point(start.X + offset, start.Y),
                    new Point(end.X - offset, end.Y),
                    end);
            }
            context.DrawGeometry(null, new Pen(UiTheme.AccentBrush, 1.4), geometry);
            DrawText(context, (portIndex + 1).ToString(CultureInfo.InvariantCulture),
                new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2), 9,
                UiTheme.AccentTextBrush, centered: true, background: UiTheme.AccentBrush);
        }
    }

    private static Rect BaseNodeRect(LaserPmtBaseParameterNode node) =>
        new(node.Position.X, node.Position.Y, BaseNodeWidth, 18);

    private static Rect ParameterNodeRect(LaserPmtSingleParameterNode node) =>
        new(node.Position.X, node.Position.Y, ParameterNodeWidth, Math.Max(22, 17 + node.Ports.Count * 7));

    private static Point ParameterPortPoint(LaserPmtSingleParameterNode node, int index)
    {
        var rect = ParameterNodeRect(node);
        return new Point(rect.Right, rect.Top + 18 + index * 7);
    }

    private Point ScreenPoint(Point world) =>
        LaserPmtWorkflowViewMath.WorldToScreen(world, _viewport, Bounds.Size);

    private Rect ScreenRect(Rect world) =>
        LaserPmtWorkflowViewMath.WorldRectToScreen(world, _viewport, Bounds.Size);

    private static Rect ToRect(LaserPmtWorkflowBounds bounds) =>
        new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

    private static void DrawText(
        DrawingContext context,
        string text,
        Point point,
        double size,
        IBrush brush,
        bool centered = false,
        IBrush? background = null)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            UiTheme.UiTypeface,
            size,
            brush);
        var origin = centered
            ? point - new Vector(formatted.Width / 2, formatted.Height / 2)
            : point;
        if (background is not null)
            context.DrawRectangle(background, null,
                new Rect(origin - new Vector(3, 1),
                    new Size(formatted.Width + 6, formatted.Height + 2)), 3, 3);
        context.DrawText(formatted, origin);
    }

    private void NotifyViewChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
