using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class PmtPreviewControl : Control
{
    private const double Padding = 28;
    private LaserPmtLayout? _layout;
    private int _selectedIndex = -1;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragStart;
    private Vector _panAtDragStart;
    private bool _dragged;

    public LaserPmtLayout? Layout => _layout;
    public LaserPmtJobLayout? SelectedJob =>
        _layout is not null && _selectedIndex >= 0 && _selectedIndex < _layout.Jobs.Count
            ? _layout.Jobs[_selectedIndex]
            : null;
    public double Zoom => _zoom;
    public Vector PanOffset => _pan;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ViewChanged;

    public PmtPreviewControl()
    {
        MinHeight = 360;
        ClipToBounds = true;
        Focusable = true;
    }

    public void Load(LaserPmtLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _selectedIndex = layout.Jobs.Count > 0 ? 0 : -1;
        FitToView();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _layout = null;
        _selectedIndex = -1;
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FitToView()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), _zoom * 1.25);
    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), _zoom / 1.25);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(UiTheme.SunkenBrush, new Rect(Bounds.Size));
        if (_layout is null)
        {
            DrawCenteredText(context, "生成 LaserPMT 后将在这里显示工件布局", 14, UiTheme.TextSecondaryBrush);
            return;
        }

        var workpiece = ToScreen(new Rect(0, 0, _layout.WorkpieceWidth, _layout.WorkpieceHeight));
        context.DrawRectangle(
            UiTheme.CardBrush,
            new Pen(UiTheme.BorderStrongBrush, 1.5),
            workpiece);
        foreach (var job in _layout.Jobs)
        {
            var rect = ToScreen(new Rect(job.Left, job.Top, job.Width, job.Height));
            var selected = job.Index == _selectedIndex;
            context.DrawRectangle(
                selected ? UiTheme.SelectionBrush : UiTheme.GhostBrush,
                new Pen(selected ? UiTheme.AccentBrush : UiTheme.BorderMediumBrush, selected ? 2 : 1),
                rect);
            if (rect.Width >= 24 && rect.Height >= 18)
            {
                var label = new FormattedText(
                    job.Identifier,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    UiTheme.UiTypeface,
                    Math.Clamp(Math.Min(rect.Width / Math.Max(job.Identifier.Length, 2), rect.Height * 0.28), 9, 16),
                    selected ? UiTheme.TextPrimaryBrush : UiTheme.TextSecondaryBrush);
                context.DrawText(
                    label,
                    rect.Center - new Vector(label.Width / 2, label.Height / 2));
            }
        }

        var origin = new FormattedText(
            "工件起点 (0, 0)",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            UiTheme.UiTypeface,
            11,
            UiTheme.TextSecondaryBrush);
        context.DrawText(origin, new Point(workpiece.Left, Math.Max(4, workpiece.Top - origin.Height - 4)));
        var spacing = new FormattedText(
            $"横向间隔 {_layout.HorizontalGap:0.###} mm  ·  纵向间隔 {_layout.VerticalGap:0.###} mm",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            UiTheme.UiTypeface,
            11,
            UiTheme.TextSecondaryBrush);
        context.DrawText(
            spacing,
            new Point(workpiece.Left, Math.Min(Bounds.Height - spacing.Height - 4, workpiece.Bottom + 5)));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_layout is null)
            return;
        ZoomAt(e.GetPosition(this), GrayscalePreviewViewMath.WheelZoom(_zoom, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed)
            return;
        _dragStart = e.GetPosition(this);
        _panAtDragStart = _pan;
        _dragged = false;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start)
            return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) > 3 || Math.Abs(delta.Y) > 3)
            _dragged = true;
        if (_dragged)
        {
            _pan = _panAtDragStart + delta;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart is null)
            return;
        var position = e.GetPosition(this);
        if (!_dragged)
            SelectAt(position);
        _dragStart = null;
        Cursor = new Cursor(StandardCursorType.Arrow);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragStart = null;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void SelectAt(Point screen)
    {
        if (_layout is null)
            return;
        var model = FromScreen(screen);
        var selected = _layout.Jobs.LastOrDefault(job =>
            new Rect(job.Left, job.Top, job.Width, job.Height).Contains(model));
        var index = selected?.Index ?? -1;
        if (_selectedIndex == index)
            return;
        _selectedIndex = index;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ZoomAt(Point anchor, double requestedZoom)
    {
        var zoom = GrayscalePreviewViewMath.ClampZoom(requestedZoom);
        var factor = zoom / _zoom;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        _pan = anchor - center - (anchor - center - _pan) * factor;
        _zoom = zoom;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private double BaseScale => _layout is null
        ? 1
        : Math.Min(
            Math.Max(Bounds.Width - Padding * 2, 1) / _layout.WorkpieceWidth,
            Math.Max(Bounds.Height - Padding * 2, 1) / _layout.WorkpieceHeight);

    private Point ModelOrigin => _layout is null
        ? new Point(Bounds.Width / 2, Bounds.Height / 2)
        : new Point(
            (Bounds.Width - _layout.WorkpieceWidth * BaseScale * _zoom) / 2 + _pan.X,
            (Bounds.Height - _layout.WorkpieceHeight * BaseScale * _zoom) / 2 + _pan.Y);

    private Point ToScreen(Point model) => ModelOrigin + new Vector(
        model.X * BaseScale * _zoom,
        model.Y * BaseScale * _zoom);

    private Point FromScreen(Point screen)
    {
        var scale = BaseScale * _zoom;
        return new Point((screen.X - ModelOrigin.X) / scale, (screen.Y - ModelOrigin.Y) / scale);
    }

    private Rect ToScreen(Rect model)
    {
        var topLeft = ToScreen(model.TopLeft);
        return new Rect(
            topLeft,
            new Size(model.Width * BaseScale * _zoom, model.Height * BaseScale * _zoom));
    }

    private void DrawCenteredText(DrawingContext context, string text, double size, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            UiTheme.UiTypeface,
            size,
            brush);
        context.DrawText(formatted, Bounds.Center - new Vector(formatted.Width / 2, formatted.Height / 2));
    }
}
