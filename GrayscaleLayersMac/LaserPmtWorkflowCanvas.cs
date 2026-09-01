using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class LaserPmtWorkflowCanvas : Control
{
    private enum DragKind
    {
        None,
        Pan,
        Target,
        ParameterNode,
        BaseNode,
        TimestampResize,
        Connection
    }

    private const double BaseNodeWidth = 58;
    private const double ParameterNodeWidth = 58;
    private LaserPmtWorkflow? _workflow;
    private LaserPmtCanvasViewport _viewport = new(1, 0, 0);
    private Point? _panStart;
    private LaserPmtCanvasViewport _viewportAtPanStart;
    private DragKind _dragKind;
    private string? _dragId;
    private string? _connectionNodeId;
    private string? _connectionPortId;
    private Point _dragWorldStart;
    private Point _connectionPointer;
    private LaserPmtWorkflow? _workflowAtDragStart;
    private string? _selectedId;
    private bool _isWorkpieceSelected;
    private bool _showNodes;
    private double? _verticalAlignmentGuide;
    private double? _horizontalAlignmentGuide;

    public LaserPmtWorkflow? Workflow => _workflow;
    public LaserPmtCanvasViewport Viewport => _viewport;
    public string? SelectedId => _selectedId;
    public bool IsWorkpieceSelected => _isWorkpieceSelected;
    public bool HasEditableSelection =>
        _isWorkpieceSelected ||
        _selectedId is not null && _workflow is not null &&
        (_workflow.BaseNodes.Any(node => node.Id == _selectedId) ||
         _workflow.ParameterNodes.Any(node => node.Id == _selectedId) ||
         _workflow.Targets.Any(target => target.Id == _selectedId) ||
         _workflow.Connections.Any(connection => connection.Id == _selectedId));
    public bool ShowNodes
    {
        get => _showNodes;
        set
        {
            if (_showNodes == value)
                return;
            _showNodes = value;
            FitAll();
            InvalidateVisual();
        }
    }
    public event EventHandler? ViewChanged;
    public event EventHandler? WorkflowChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? WorkpieceEditRequested;
    public event EventHandler<string>? EditRejected;

    public LaserPmtWorkflowCanvas()
    {
        MinHeight = 360;
        ClipToBounds = true;
        Focusable = true;
    }

    public void Load(LaserPmtWorkflow workflow, bool preserveSelection = false)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _viewport = workflow.Viewport;
        if (!preserveSelection)
        {
            _selectedId = null;
            _isWorkpieceSelected = false;
        }
        else if (!ContainsId(workflow, _selectedId))
            _selectedId = null;
        InvalidateVisual();
    }

    public void UpdateWorkflow(LaserPmtWorkflow workflow, bool preserveSelection = true)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        if (!preserveSelection ||
            !_isWorkpieceSelected && _selectedId is not null && !ContainsId(workflow, _selectedId))
            Select(null);
        NotifyWorkflowChanged();
    }

    public void ZoomBy(double factor)
    {
        if (_workflow is null || !double.IsFinite(factor) || factor <= 0)
            return;
        _viewport = LaserPmtWorkflowViewMath.ZoomAt(
            _viewport, Bounds.Center, Bounds.Size, _viewport.Zoom * factor);
        NotifyViewChanged();
    }

    public void DeleteSelection()
    {
        if (_workflow is null || _selectedId is null)
            return;
        DeleteSelectedCore();
    }

    public void Clear()
    {
        _workflow = null;
        _viewport = new LaserPmtCanvasViewport(1, 0, 0);
        _selectedId = null;
        _isWorkpieceSelected = false;
        ClearAlignmentGuides();
        InvalidateVisual();
    }

    public Rect? GetSelectionScreenBounds()
    {
        if (_workflow is null)
            return null;
        if (_isWorkpieceSelected)
            return ScreenRect(ToRect(_workflow.Workpiece));
        if (_selectedId is null)
            return null;
        var target = _workflow.Targets.FirstOrDefault(item => item.Id == _selectedId);
        if (target is not null)
            return ScreenRect(ToRect(target.Bounds));
        var baseNode = _workflow.BaseNodes.FirstOrDefault(item => item.Id == _selectedId);
        if (baseNode is not null)
            return ScreenRect(BaseNodeRect(baseNode));
        var parameterNode = _workflow.ParameterNodes.FirstOrDefault(item => item.Id == _selectedId);
        if (parameterNode is not null)
            return ScreenRect(ParameterNodeRect(parameterNode));
        return null;
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
        var bounds = ToRect(_workflow.Workpiece);
        if (_showNodes)
        {
            foreach (var node in _workflow.BaseNodes)
                bounds = bounds.Union(BaseNodeRect(node));
            foreach (var node in _workflow.ParameterNodes)
                bounds = bounds.Union(ParameterNodeRect(node));
        }
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
        if (_showNodes)
        {
            DrawBaseBus(context);
            DrawConnections(context);
        }
        foreach (var target in _workflow.Targets)
            DrawTarget(context, target, invalidTargets.Contains(target.Id));
        DrawAlignmentGuides(context);
        if (_showNodes)
        {
            foreach (var node in _workflow.BaseNodes)
                DrawBaseNode(context, node);
            foreach (var node in _workflow.ParameterNodes)
                DrawParameterNode(context, node);
        }
        if (_showNodes && _dragKind == DragKind.Connection &&
            _connectionNodeId is not null && _connectionPortId is not null)
        {
            var node = _workflow.ParameterNodes.Single(item => item.Id == _connectionNodeId);
            var index = node.Ports.ToList().FindIndex(port => port.Id == _connectionPortId);
            if (index >= 0)
                context.DrawLine(new Pen(UiTheme.AccentBrush, 1.4),
                    ScreenPoint(ParameterPortPoint(node, index)), _connectionPointer);
        }
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
        var screen = e.GetPosition(this);
        _workflowAtDragStart = _workflow;
        _dragWorldStart = LaserPmtWorkflowViewMath.ScreenToWorld(screen, _viewport, Bounds.Size);
        if (_showNodes && point.Properties.IsLeftButtonPressed && TryHitPort(screen, out var nodeId, out var portId))
        {
            _dragKind = DragKind.Connection;
            _connectionNodeId = nodeId;
            _connectionPortId = portId;
            _connectionPointer = screen;
        }
        else if (point.Properties.IsLeftButtonPressed && TryHitTimestampHandle(screen, out var resizeId))
        {
            Select(resizeId);
            _dragKind = DragKind.TimestampResize;
            _dragId = resizeId;
        }
        else if (point.Properties.IsLeftButtonPressed && TryHitTarget(screen, out var targetId))
        {
            Select(targetId);
            _dragKind = DragKind.Target;
            _dragId = targetId;
        }
        else if (point.Properties.IsLeftButtonPressed && IsOnWorkpieceBorder(screen))
        {
            SelectWorkpiece();
            _dragKind = DragKind.None;
            WorkpieceEditRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (_showNodes && point.Properties.IsLeftButtonPressed && TryHitParameterNode(screen, out var parameterNodeId))
        {
            Select(parameterNodeId);
            _dragKind = DragKind.ParameterNode;
            _dragId = parameterNodeId;
        }
        else if (_showNodes && point.Properties.IsLeftButtonPressed &&
                 _workflow.BaseNodes.Any(node => ScreenRect(BaseNodeRect(node)).Contains(screen)))
        {
            var baseNode = _workflow.BaseNodes.First(node => ScreenRect(BaseNodeRect(node)).Contains(screen));
            Select(baseNode.Id);
            _dragKind = DragKind.BaseNode;
            _dragId = baseNode.Id;
        }
        else if (_showNodes && point.Properties.IsLeftButtonPressed && TryHitConnection(screen, out var connectionId))
        {
            Select(connectionId);
            _dragKind = DragKind.None;
        }
        else
        {
            Select(null);
            _dragKind = DragKind.Pan;
            _panStart = screen;
            _viewportAtPanStart = _viewport;
        }
        e.Pointer.Capture(this);
        Cursor = new Cursor(_dragKind == DragKind.Connection
            ? StandardCursorType.Cross
            : StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_workflow is null || _workflowAtDragStart is null || _dragKind == DragKind.None)
            return;
        var screen = e.GetPosition(this);
        if (_dragKind == DragKind.Connection)
        {
            _connectionPointer = screen;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_dragKind == DragKind.Pan && _panStart is { } panStart)
        {
            var panDelta = screen - panStart;
            _viewport = _viewportAtPanStart with
            {
                PanX = _viewportAtPanStart.PanX + panDelta.X,
                PanY = _viewportAtPanStart.PanY + panDelta.Y
            };
            NotifyViewChanged();
            e.Handled = true;
            return;
        }
        var world = LaserPmtWorkflowViewMath.ScreenToWorld(screen, _viewport, Bounds.Size);
        var delta = world - _dragWorldStart;
        try
        {
            if (_dragKind == DragKind.Target &&
                _workflowAtDragStart.Targets.OfType<LaserPmtTarget>()
                    .FirstOrDefault(item => item.Id == _dragId) is { } draggedPmt &&
                !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                var candidate = draggedPmt.Bounds with
                {
                    Left = draggedPmt.Bounds.Left + delta.X,
                    Top = draggedPmt.Bounds.Top + delta.Y
                };
                var snapped = LaserPmtAlignmentSnap.Apply(
                    candidate,
                    _workflowAtDragStart.Targets.OfType<LaserPmtTarget>()
                        .Where(item => item.Id != draggedPmt.Id)
                        .Select(item => item.Bounds)
                        .ToArray(),
                    _workflowAtDragStart.Workpiece,
                    6 / _viewport.Zoom);
                _verticalAlignmentGuide = snapped.VerticalGuide;
                _horizontalAlignmentGuide = snapped.HorizontalGuide;
                _workflow = LaserPmtWorkflowEditor.MovePmt(
                    _workflowAtDragStart,
                    draggedPmt.Id,
                    snapped.Bounds.Left,
                    snapped.Bounds.Top);
            }
            else
            {
                ClearAlignmentGuides();
                _workflow = _dragKind switch
                {
                    DragKind.Target => MoveTarget(_workflowAtDragStart, _dragId!, delta),
                    DragKind.TimestampResize => ResizeTimestamp(_workflowAtDragStart, _dragId!, delta),
                    DragKind.ParameterNode => MoveParameterNode(_workflowAtDragStart, _dragId!, delta),
                    DragKind.BaseNode => LaserPmtWorkflowEditor.MoveBaseNode(
                        _workflowAtDragStart,
                        _dragId!,
                        new LaserPmtWorkflowPoint(
                            _workflowAtDragStart.BaseNodes.Single(node => node.Id == _dragId).Position.X + delta.X,
                            _workflowAtDragStart.BaseNodes.Single(node => node.Id == _dragId).Position.Y + delta.Y)),
                    _ => _workflow
                };
            }
            NotifyWorkflowChanged();
        }
        catch (ArgumentException exception)
        {
            EditRejected?.Invoke(this, exception.Message);
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragKind == DragKind.None && _panStart is null)
        {
            e.Pointer.Capture(null);
            Cursor = new Cursor(StandardCursorType.Arrow);
            return;
        }
        var screen = e.GetPosition(this);
        if (_dragKind == DragKind.Connection && _workflow is not null &&
            TryHitTarget(screen, out var targetId))
        {
            try
            {
                _workflow = LaserPmtWorkflowEditor.AddConnection(
                    _workflow,
                    new LaserPmtConnection(
                        $"connection-{Guid.NewGuid():N}",
                        _connectionNodeId!,
                        _connectionPortId!,
                        targetId));
                NotifyWorkflowChanged();
            }
            catch (ArgumentException exception)
            {
                EditRejected?.Invoke(this, exception.Message);
            }
        }
        _dragKind = DragKind.None;
        _dragId = null;
        _connectionNodeId = null;
        _connectionPortId = null;
        _workflowAtDragStart = null;
        _panStart = null;
        ClearAlignmentGuides();
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Arrow);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panStart = null;
        _dragKind = DragKind.None;
        _workflowAtDragStart = null;
        ClearAlignmentGuides();
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_workflow is null || _selectedId is null)
            return;
        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedCore();
            e.Handled = true;
            return;
        }
        var direction = e.Key switch
        {
            Key.Left => PmtNavigationDirection.Left,
            Key.Right => PmtNavigationDirection.Right,
            Key.Up => PmtNavigationDirection.Up,
            Key.Down => PmtNavigationDirection.Down,
            _ => (PmtNavigationDirection?)null
        };
        if (direction is null || _workflow.Targets.OfType<LaserPmtTarget>()
                .FirstOrDefault(target => target.Id == _selectedId) is not { } selected)
            return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1d : 0.1d;
            var dx = direction is PmtNavigationDirection.Left ? -step :
                direction is PmtNavigationDirection.Right ? step : 0;
            var dy = direction is PmtNavigationDirection.Up ? -step :
                direction is PmtNavigationDirection.Down ? step : 0;
            try
            {
                _workflow = LaserPmtWorkflowEditor.MovePmt(
                    _workflow, selected.Id, selected.Bounds.Left + dx, selected.Bounds.Top + dy);
                NotifyWorkflowChanged();
            }
            catch (ArgumentException error)
            {
                EditRejected?.Invoke(this, error.Message);
            }
        }
        else
            SelectNearestPmt(selected, direction.Value);
        e.Handled = true;
    }

    private void SelectNearestPmt(LaserPmtTarget selected, PmtNavigationDirection direction)
    {
        var centerX = selected.Bounds.Left + selected.Bounds.Width / 2;
        var centerY = selected.Bounds.Top + selected.Bounds.Height / 2;
        var next = _workflow!.Targets.OfType<LaserPmtTarget>()
            .Where(target => target.Id != selected.Id)
            .Select(target => new
            {
                Target = target,
                Dx = target.Bounds.Left + target.Bounds.Width / 2 - centerX,
                Dy = target.Bounds.Top + target.Bounds.Height / 2 - centerY
            })
            .Where(item => direction switch
            {
                PmtNavigationDirection.Left => item.Dx < 0,
                PmtNavigationDirection.Right => item.Dx > 0,
                PmtNavigationDirection.Up => item.Dy < 0,
                _ => item.Dy > 0
            })
            .OrderBy(item => direction is PmtNavigationDirection.Left or PmtNavigationDirection.Right
                ? Math.Abs(item.Dy) : Math.Abs(item.Dx))
            .ThenBy(item => Math.Sqrt(item.Dx * item.Dx + item.Dy * item.Dy))
            .Select(item => item.Target)
            .FirstOrDefault();
        if (next is not null)
            Select(next.Id);
    }

    private void DeleteSelectedCore()
    {
        if (_workflow is not { } workflow || _selectedId is not { } selectedId)
            return;
        try
        {
            if (workflow.Connections.Any(connection => connection.Id == selectedId))
                _workflow = LaserPmtWorkflowEditor.RemoveConnection(workflow, selectedId);
            else if (workflow.ParameterNodes.Any(node => node.Id == selectedId))
                _workflow = LaserPmtWorkflowEditor.DeleteParameterNode(workflow, selectedId);
            else if (workflow.Targets.Any(target => target.Id == selectedId))
                _workflow = LaserPmtWorkflowEditor.DeleteTarget(workflow, selectedId);
            else
                return;
            Select(null);
            NotifyWorkflowChanged();
        }
        catch (ArgumentException exception)
        {
            EditRejected?.Invoke(this, exception.Message);
        }
    }

    private static bool ContainsId(LaserPmtWorkflow workflow, string? id) =>
        id is not null &&
        (workflow.BaseNodes.Any(node => node.Id == id) ||
         workflow.ParameterNodes.Any(node => node.Id == id) ||
         workflow.Targets.Any(target => target.Id == id) ||
         workflow.Connections.Any(connection => connection.Id == id));

    private void DrawWorkpiece(DrawingContext context)
    {
        var rect = ScreenRect(ToRect(_workflow!.Workpiece));
        context.DrawRectangle(
            UiTheme.CardBrush,
            new Pen(_isWorkpieceSelected ? UiTheme.AccentBrush : UiTheme.BorderStrongBrush,
                _isWorkpieceSelected ? 2.5 : 1.5),
            rect);
        if (_isWorkpieceSelected)
        {
            foreach (var point in new[]
                     {
                         rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight
                     })
                context.FillRectangle(UiTheme.AccentBrush,
                    new Rect(point.X - 3, point.Y - 3, 6, 6));
        }
        DrawText(context, "工件 · (0, 0)", new Point(rect.Left, rect.Top - 16), 11,
            UiTheme.TextSecondaryBrush);
    }

    private void DrawAlignmentGuides(DrawingContext context)
    {
        if (_workflow is null ||
            _verticalAlignmentGuide is null && _horizontalAlignmentGuide is null)
            return;
        var workpiece = ScreenRect(ToRect(_workflow.Workpiece));
        if (_verticalAlignmentGuide is { } x)
        {
            var screenX = ScreenPoint(new Point(x, 0)).X;
            DrawDashedLine(context, new Point(screenX, workpiece.Top),
                new Point(screenX, workpiece.Bottom));
        }
        if (_horizontalAlignmentGuide is { } y)
        {
            var screenY = ScreenPoint(new Point(0, y)).Y;
            DrawDashedLine(context, new Point(workpiece.Left, screenY),
                new Point(workpiece.Right, screenY));
        }
    }

    private static void DrawDashedLine(DrawingContext context, Point start, Point end)
    {
        const double dash = 5;
        const double gap = 3;
        var vector = end - start;
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        if (length <= 0)
            return;
        var direction = vector / length;
        var pen = new Pen(UiTheme.AccentBrush, 1);
        for (var offset = 0d; offset < length; offset += dash + gap)
            context.DrawLine(pen, start + direction * offset,
                start + direction * Math.Min(length, offset + dash));
    }

    private void DrawTarget(
        DrawingContext context,
        LaserPmtWorkflowTarget target,
        bool invalid)
    {
        var rect = ScreenRect(ToRect(target.Bounds));
        var selected = target.Id == _selectedId;
        var pen = new Pen(
            invalid ? UiTheme.DangerBrush : selected ? UiTheme.AccentBrush : UiTheme.BorderMediumBrush,
            invalid || selected ? 2 : 1);
        var fill = invalid ? UiTheme.GhostPressedBrush : UiTheme.GhostBrush;
        context.DrawRectangle(fill, pen, rect);
        var source = _workflow!.Sources.FirstOrDefault(item => item.Id == target.SourceId);
        if (source is not null)
        {
            var sourceBrush = new SolidColorBrush(Color.FromUInt32(source.ColorArgb));
            context.FillRectangle(sourceBrush, new Rect(rect.Left, rect.Top, Math.Min(5, rect.Width), rect.Height));
            if (rect.Width >= 28 && rect.Height >= 18)
                DrawText(context, source.Mark, new Point(rect.Left + 9, rect.Top + 3), 8.5,
                    sourceBrush);
        }
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
            if (selected)
                context.FillRectangle(UiTheme.AccentBrush,
                    new Rect(rect.Right - 7, rect.Bottom - 7, 7, 7));
        }
        if (rect.Width >= 18 && rect.Height >= 12)
            DrawText(context, label, rect.Center, 10.5,
                invalid ? UiTheme.DangerTextBrush : UiTheme.TextPrimaryBrush, centered: true);
        if (target is LaserPmtTarget { IsSizeLocked: true } && rect.Width >= 22 && rect.Height >= 16)
            DrawSizeLock(context, new Point(rect.Right - 10, rect.Top + 8));
    }

    private static void DrawSizeLock(DrawingContext context, Point center)
    {
        var pen = new Pen(UiTheme.TextSecondaryBrush, 1.2);
        context.DrawRectangle(null, pen, new Rect(center.X - 4, center.Y, 8, 6), 1, 1);
        var shackle = new StreamGeometry();
        using (var path = shackle.Open())
        {
            path.BeginFigure(new Point(center.X - 2.5, center.Y), false);
            path.CubicBezierTo(
                new Point(center.X - 2.5, center.Y - 4),
                new Point(center.X + 2.5, center.Y - 4),
                new Point(center.X + 2.5, center.Y));
        }
        context.DrawGeometry(null, pen, shackle);
    }

    private void DrawBaseNode(DrawingContext context, LaserPmtBaseParameterNode node)
    {
        var rect = ScreenRect(BaseNodeRect(node));
        context.DrawRectangle(UiTheme.CardBrush,
            new Pen(UiTheme.AccentBrush, node.Id == _selectedId ? 2.5 : 1.5), rect, 6, 6);
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
        context.DrawRectangle(UiTheme.CardBrush,
            new Pen(node.Id == _selectedId ? UiTheme.AccentBrush : UiTheme.BorderStrongBrush,
                node.Id == _selectedId ? 2 : 1.2), rect, 6, 6);
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
        var workflow = _workflow!;
        var workpiece = ScreenRect(ToRect(workflow.Workpiece));
        foreach (var baseNode in workflow.BaseNodes)
        {
            var node = ScreenRect(BaseNodeRect(baseNode));
            var start = new Point(node.Right, node.Center.Y);
            var end = new Point(workpiece.Left, node.Center.Y);
            context.DrawLine(new Pen(UiTheme.AccentBrush, 1.5), start, end);
            DrawText(context, "基础", new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2 - 12),
                9, UiTheme.AccentBrush, centered: true);
        }
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
            context.DrawGeometry(null,
                new Pen(UiTheme.AccentBrush, connection.Id == _selectedId ? 2.8 : 1.4), geometry);
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

    private bool TryHitTarget(Point screen, out string targetId)
    {
        foreach (var target in _workflow!.Targets.Reverse())
        {
            if (!ScreenRect(ToRect(target.Bounds)).Contains(screen))
                continue;
            targetId = target.Id;
            return true;
        }
        targetId = string.Empty;
        return false;
    }

    private bool IsOnWorkpieceBorder(Point screen)
    {
        var rect = ScreenRect(ToRect(_workflow!.Workpiece));
        var outer = rect.Inflate(6);
        var inner = rect.Deflate(6);
        return outer.Contains(screen) && !inner.Contains(screen);
    }

    private bool TryHitTimestampHandle(Point screen, out string targetId)
    {
        foreach (var timestamp in _workflow!.Targets.OfType<LaserPmtTimestampTarget>().Reverse())
        {
            var rect = ScreenRect(ToRect(timestamp.Bounds));
            if (!new Rect(rect.Right - 10, rect.Bottom - 10, 12, 12).Contains(screen))
                continue;
            targetId = timestamp.Id;
            return true;
        }
        targetId = string.Empty;
        return false;
    }

    private bool TryHitParameterNode(Point screen, out string nodeId)
    {
        foreach (var node in _workflow!.ParameterNodes.Reverse())
        {
            if (!ScreenRect(ParameterNodeRect(node)).Contains(screen))
                continue;
            nodeId = node.Id;
            return true;
        }
        nodeId = string.Empty;
        return false;
    }

    private bool TryHitPort(Point screen, out string nodeId, out string portId)
    {
        foreach (var node in _workflow!.ParameterNodes.Reverse())
        {
            for (var index = node.Ports.Count - 1; index >= 0; index--)
            {
                var port = ScreenPoint(ParameterPortPoint(node, index));
                if (Distance(screen, port) > 9)
                    continue;
                nodeId = node.Id;
                portId = node.Ports[index].Id;
                return true;
            }
        }
        nodeId = string.Empty;
        portId = string.Empty;
        return false;
    }

    private bool TryHitConnection(Point screen, out string connectionId)
    {
        var nodes = _workflow!.ParameterNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var targets = _workflow.Targets.ToDictionary(target => target.Id, StringComparer.Ordinal);
        foreach (var connection in _workflow.Connections.Reverse())
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
            var control1 = new Point(start.X + offset, start.Y);
            var control2 = new Point(end.X - offset, end.Y);
            var previous = start;
            for (var sample = 1; sample <= 24; sample++)
            {
                var current = CubicPoint(start, control1, control2, end, sample / 24d);
                if (DistanceToSegment(screen, previous, current) <= 7)
                {
                    connectionId = connection.Id;
                    return true;
                }
                previous = current;
            }
        }
        connectionId = string.Empty;
        return false;
    }

    private static Point CubicPoint(Point start, Point control1, Point control2, Point end, double t)
    {
        var inverse = 1 - t;
        return new Point(
            inverse * inverse * inverse * start.X + 3 * inverse * inverse * t * control1.X +
            3 * inverse * t * t * control2.X + t * t * t * end.X,
            inverse * inverse * inverse * start.Y + 3 * inverse * inverse * t * control1.Y +
            3 * inverse * t * t * control2.Y + t * t * t * end.Y);
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var segment = end - start;
        var lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        if (lengthSquared <= double.Epsilon)
            return Distance(point, start);
        var fromStart = point - start;
        var t = Math.Clamp((fromStart.X * segment.X + fromStart.Y * segment.Y) / lengthSquared, 0, 1);
        return Distance(point, start + segment * t);
    }

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static LaserPmtWorkflow MoveTarget(
        LaserPmtWorkflow workflow,
        string targetId,
        Vector delta)
    {
        var target = workflow.Targets.Single(item => item.Id == targetId);
        return target switch
        {
            LaserPmtTarget pmt => LaserPmtWorkflowEditor.MovePmt(
                workflow, targetId, pmt.Bounds.Left + delta.X, pmt.Bounds.Top + delta.Y),
            LaserPmtTimestampTarget timestamp => LaserPmtWorkflowEditor.MoveTimestamp(
                workflow, targetId, timestamp.Bounds.Left + delta.X, timestamp.Bounds.Top + delta.Y),
            _ => workflow
        };
    }

    private static LaserPmtWorkflow ResizeTimestamp(
        LaserPmtWorkflow workflow,
        string targetId,
        Vector delta)
    {
        var timestamp = workflow.Targets.OfType<LaserPmtTimestampTarget>()
            .Single(item => item.Id == targetId);
        return LaserPmtWorkflowEditor.ResizeTimestamp(
            workflow,
            targetId,
            Math.Max(0.5, timestamp.Bounds.Width + delta.X),
            Math.Max(0.5, timestamp.Bounds.Height + delta.Y));
    }

    private static LaserPmtWorkflow MoveParameterNode(
        LaserPmtWorkflow workflow,
        string nodeId,
        Vector delta)
    {
        var node = workflow.ParameterNodes.Single(item => item.Id == nodeId);
        return LaserPmtWorkflowEditor.MoveParameterNode(
            workflow,
            nodeId,
            new LaserPmtWorkflowPoint(node.Position.X + delta.X, node.Position.Y + delta.Y));
    }

    private void Select(string? id)
    {
        if (_selectedId == id && !_isWorkpieceSelected)
            return;
        _selectedId = id;
        _isWorkpieceSelected = false;
        ClearAlignmentGuides();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectWorkpiece()
    {
        if (_isWorkpieceSelected)
            return;
        _selectedId = null;
        _isWorkpieceSelected = true;
        ClearAlignmentGuides();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearAlignmentGuides()
    {
        var hadGuides = _verticalAlignmentGuide is not null || _horizontalAlignmentGuide is not null;
        _verticalAlignmentGuide = null;
        _horizontalAlignmentGuide = null;
        if (hadGuides)
            InvalidateVisual();
    }

    private void NotifyWorkflowChanged()
    {
        InvalidateVisual();
        WorkflowChanged?.Invoke(this, EventArgs.Empty);
    }

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
        if (_workflow is not null)
            _workflow = LaserPmtWorkflowEditor.SetViewport(_workflow, _viewport);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
