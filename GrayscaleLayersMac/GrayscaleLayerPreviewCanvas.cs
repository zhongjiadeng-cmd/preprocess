using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GrayscaleLayersMac;

/// <summary>
/// 灰度分层预览画布。
///
/// 这里的缩放与平移由控件自己维护，而不是交给 ScrollViewer：
/// ScrollViewer 会先吃掉滚轮事件做原生滚动，再冒泡到外部处理器，
/// 导致“滚一次既滚又缩”。自绘之后滚轮意图、光标锚点、拖拽手感都可精确控制。
/// </summary>
public sealed class GrayscaleLayerPreviewCanvas : Control, IDisposable
{
    private const double DragThreshold = 3;
    private const double PixelGridZoom = 8;
    private const int MaxGridLinesPerAxis = 400;

    private static readonly Cursor GrabCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private Bitmap? _bitmap;
    private GrayscalePreviewView _view = GrayscalePreviewView.Identity;
    private GrayscalePreviewWheelMode _wheelMode = GrayscalePreviewWheelMode.Auto;
    private bool _isPanning;
    private bool _movedPastThreshold;
    private Point _panPointerStart;
    private GrayscalePreviewView _panViewStart;
    private bool _spaceHeld;
    private bool _disposed;
    /// <summary>
    /// 当外部已经负责传入 <see cref="Bitmap"/> 的生命周期时不释放，避免双释放。
    /// </summary>
    private bool _ownsBitmap = true;

    public event EventHandler? ViewChanged;

    public GrayscaleLayerPreviewCanvas()
    {
        MinHeight = 320;
        ClipToBounds = true;
        Focusable = true;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Cursor = ArrowCursor;
    }

    public GrayscalePreviewView View => _view;

    public double Zoom => _view.Zoom;

    public bool IsPanning => _isPanning;

    public bool HasImage => _bitmap is not null;

    public bool CanPanHorizontally => HasImage &&
        GrayscalePreviewViewMath.CanScroll(ContentSize.Width, Bounds.Width);

    public bool CanPanVertically => HasImage &&
        GrayscalePreviewViewMath.CanScroll(ContentSize.Height, Bounds.Height);

    public bool CanPan => CanPanHorizontally || CanPanVertically;

    public GrayscalePreviewWheelMode WheelMode
    {
        get => _wheelMode;
        set => _wheelMode = value;
    }

    private Size ImageSize => _bitmap is null
        ? default
        : new Size(_bitmap.PixelSize.Width, _bitmap.PixelSize.Height);

    private Size ViewportSize => Bounds.Size;

    private Size ContentSize => GrayscalePreviewViewMath.ContentSize(ImageSize, _view.Zoom);

    private Point ViewportCenter => new(Bounds.Width / 2, Bounds.Height / 2);

    /// <summary>
    /// 换图。<paramref name="keepView"/> 为真且新图与旧图同尺寸时保留当前缩放与位置
    /// （逐层对照时这是关键：否则每切一层视口都会跳回原点）。
    /// </summary>
    /// <param name="ownsBitmap">
    /// 为 false 时画布不释放传入的 Bitmap，由调用方（例如
    /// <see cref="TexturePreviewSurface"/>，背后是 <c>TexturePreviewController</c>）
    /// 自己负责生命周期；分层页可保持默认值 true。
    /// </param>
    public void SetImage(Bitmap? bitmap, bool keepView = false, bool ownsBitmap = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ownsBitmap = ownsBitmap;
        var previous = _bitmap;
        var sameGeometry = previous is not null &&
            bitmap is not null &&
            previous.PixelSize == bitmap.PixelSize;

        if (!ReferenceEquals(previous, bitmap))
            _bitmap = bitmap;

        _view = bitmap is null
            ? GrayscalePreviewView.Identity
            : keepView && sameGeometry
                ? GrayscalePreviewViewMath.Clamp(_view, ImageSize, ViewportSize)
                // 新图（或尺寸发生变化）回到 100% 并居中，避免沿用上一张图的偏移。
                : GrayscalePreviewViewMath.CenterContent(
                    GrayscalePreviewView.Identity,
                    ImageSize,
                    ViewportSize);

        if (_ownsBitmap && previous is not null && !ReferenceEquals(previous, bitmap))
            Dispatcher.UIThread.Post(previous.Dispose, DispatcherPriority.Background);

        InvalidateVisual();
        RaiseViewChanged();
    }

    public void ZoomIn() => ZoomBy(GrayscalePreviewViewMath.ZoomButtonStep);

    public void ZoomOut() => ZoomBy(1 / GrayscalePreviewViewMath.ZoomButtonStep);

    public void ZoomBy(double factor) => ZoomAt(ViewportCenter, _view.Zoom * factor);

    public void ZoomAt(Point anchor, double zoom)
        => ApplyView(GrayscalePreviewViewMath.ZoomAt(_view, ImageSize, ViewportSize, anchor, zoom));

    public void Fit() => ApplyView(GrayscalePreviewViewMath.Fit(ImageSize, ViewportSize));

    /// <summary>100% 并居中。</summary>
    public void ActualSize()
        => ApplyView(GrayscalePreviewViewMath.CenterContent(
            GrayscalePreviewView.Identity,
            ImageSize,
            ViewportSize));

    /// <summary>双击时在“适应窗口”与“100%”之间切换。</summary>
    public void ToggleFitOrActual()
    {
        if (!HasImage)
            return;
        var fitZoom = GrayscalePreviewViewMath.FitZoom(ImageSize, ViewportSize);
        if (_view.Zoom > fitZoom + 1e-6)
            Fit();
        else
            ActualSize();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var bitmap = _bitmap;
        _bitmap = null;
        if (_ownsBitmap && bitmap is not null)
            bitmap.Dispose();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var viewport = new Rect(Bounds.Size);
        context.FillRectangle(UiTheme.SunkenBrush, viewport);

        if (_bitmap is null)
        {
            DrawHint(context, viewport, "导入纹理图后在这里预览");
            return;
        }

        var content = ContentSize;
        var origin = ContentOrigin;
        var options = new RenderOptions
        {
            // 放大到 100% 以上时不再插值，直接看硬边像素，便于核对阈值分界。
            BitmapInterpolationMode = _view.Zoom >= 1
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.MediumQuality
        };
        using (context.PushRenderOptions(options))
        {
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), new Rect(origin, content));
        }

        if (_view.Zoom >= PixelGridZoom)
            DrawPixelGrid(context, viewport, origin, content);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyView(_view);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        UpdateCursor();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        UpdateCursor();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_bitmap is null)
            return;

        var modifiers = e.KeyModifiers;
        var zoomModifier =
            modifiers.HasFlag(KeyModifiers.Control) ||
            modifiers.HasFlag(KeyModifiers.Meta);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);

        var action = GrayscalePreviewViewMath.ResolveWheelAction(
            _wheelMode,
            zoomModifier,
            shift,
            CanPanVertically,
            CanPanHorizontally);

        if (action == GrayscalePreviewWheelAction.Zoom)
        {
            ZoomAt(e.GetPosition(this), GrayscalePreviewViewMath.WheelZoom(_view.Zoom, e.Delta.Y));
        }
        else
        {
            // Shift 把竖向滚轮转成横向滚动；其余情况沿用原生方向。
            var deltaX = shift ? e.Delta.Y : e.Delta.X;
            var deltaY = shift ? 0 : e.Delta.Y;
            ApplyView(GrayscalePreviewViewMath.PanBy(
                _view,
                ImageSize,
                ViewportSize,
                new Vector(
                    -deltaX * GrayscalePreviewViewMath.WheelScrollStep,
                    -deltaY * GrayscalePreviewViewMath.WheelScrollStep)));
        }

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
            return;

        if (point.Properties.IsLeftButtonPressed && e.ClickCount >= 2)
        {
            ToggleFitOrActual();
            e.Handled = true;
            return;
        }

        // 中键随时可拖；左键在画布确实可滚动（或按住空格）时才拖。
        var wantsPan = point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsLeftButtonPressed && (_spaceHeld || CanPan));
        if (!wantsPan)
            return;

        Focus();
        _isPanning = true;
        _movedPastThreshold = false;
        _panPointerStart = e.GetPosition(this);
        _panViewStart = _view;
        e.Pointer.Capture(this);
        UpdateCursor();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isPanning)
            return;

        var delta = e.GetPosition(this) - _panPointerStart;
        if (!_movedPastThreshold &&
            Math.Abs(delta.X) < DragThreshold &&
            Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _movedPastThreshold = true;
        ApplyView(GrayscalePreviewViewMath.PanBy(_panViewStart, ImageSize, ViewportSize, delta));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isPanning)
            return;
        EndPan();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPan();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space)
        {
            if (!_spaceHeld)
            {
                _spaceHeld = true;
                UpdateCursor();
            }

            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomIn();
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomOut();
                e.Handled = true;
                break;
            case Key.D0:
                Fit();
                e.Handled = true;
                break;
            case Key.D1:
                ActualSize();
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key != Key.Space)
            return;
        _spaceHeld = false;
        UpdateCursor();
        e.Handled = true;
    }

    private Point ContentOrigin
    {
        get
        {
            var content = ContentSize;
            return new Point(
                GrayscalePreviewViewMath.ScreenFromContent(0, content.Width, Bounds.Width, _view.OffsetX),
                GrayscalePreviewViewMath.ScreenFromContent(0, content.Height, Bounds.Height, _view.OffsetY));
        }
    }

    private void ApplyView(GrayscalePreviewView view)
    {
        var clamped = GrayscalePreviewViewMath.Clamp(view, ImageSize, ViewportSize);
        if (clamped == _view)
            return;
        _view = clamped;
        InvalidateVisual();
        UpdateCursor();
        RaiseViewChanged();
    }

    private void EndPan()
    {
        if (!_isPanning)
            return;
        _isPanning = false;
        _movedPastThreshold = false;
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        Cursor = _isPanning || _spaceHeld || CanPan ? GrabCursor : ArrowCursor;
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    private void DrawPixelGrid(
        DrawingContext context,
        Rect viewport,
        Point origin,
        Size content)
    {
        var step = _view.Zoom;
        if (step <= 0)
            return;
        if (content.Width / step > MaxGridLinesPerAxis ||
            content.Height / step > MaxGridLinesPerAxis)
        {
            return;
        }

        var pen = new Pen(UiTheme.BorderSubtleBrush, 1);
        for (var x = origin.X; x <= origin.X + content.Width + 0.5; x += step)
        {
            if (x < viewport.Left || x > viewport.Right)
                continue;
            context.DrawLine(pen, new Point(x, Math.Max(origin.Y, viewport.Top)), new Point(x, Math.Min(origin.Y + content.Height, viewport.Bottom)));
        }

        for (var y = origin.Y; y <= origin.Y + content.Height + 0.5; y += step)
        {
            if (y < viewport.Top || y > viewport.Bottom)
                continue;
            context.DrawLine(pen, new Point(Math.Max(origin.X, viewport.Left), y), new Point(Math.Min(origin.X + content.Width, viewport.Right), y));
        }
    }

    private static void DrawHint(DrawingContext context, Rect viewport, string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            13,
            UiTheme.TextFaintBrush);
        context.DrawText(
            formatted,
            new Point(
                Math.Max(viewport.Left + (viewport.Width - formatted.Width) / 2, 0),
                Math.Max(viewport.Top + (viewport.Height - formatted.Height) / 2, 0)));
    }
}
