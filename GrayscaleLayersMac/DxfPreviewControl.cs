using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GrayscaleLayersMac;

public sealed class DxfPreviewControl : Control, IDisposable
{
    internal sealed record Segment(
        double X1,
        double Y1,
        double Z1,
        double X2,
        double Y2,
        double Z2,
        int BlockIndex,
        bool IsBorder);
    private sealed record RowGroup(
        int StartIndex,
        int Count,
        int BlockIndex,
        double Y,
        bool IsBorder);

    private const int MaximumDisplayedSegments = 250_000;

    /// <summary>平移允许越过内容边界的余量，免得缩小状态下画布被拖到完全看不见。</summary>
    private const double PanSlack = 80;

    private GrayscalePreviewWheelMode _wheelMode = GrayscalePreviewWheelMode.Auto;
    private List<Segment> _segments = [];
    private List<RowGroup> _rowGroups = [];
    private Rect _modelBounds = new(-50, -50, 100, 100);
    private double _minZ;
    private double _maxZ;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragStart;
    private Vector _panAtDragStart;
    private double _yawAtDragStart;
    private double _tiltAtDragStart;
    private double _yaw = -35 * Math.PI / 180;
    private double _tilt = 55 * Math.PI / 180;
    private bool _isOrbiting;
    private readonly DxfOverlayState _overlay;
    private Bitmap? _textureBitmap;
    private Rect _textureBounds;
    private Rect _textureFrameBounds;
    private static readonly Color[] LayerColors =
    [
        Color.FromRgb(66, 165, 245),
        Color.FromRgb(239, 83, 80),
        Color.FromRgb(102, 187, 106),
        Color.FromRgb(255, 202, 40),
        Color.FromRgb(171, 71, 188),
        Color.FromRgb(38, 198, 218),
        Color.FromRgb(255, 112, 67),
        Color.FromRgb(124, 179, 66),
        Color.FromRgb(92, 107, 192),
        Color.FromRgb(255, 167, 38),
        Color.FromRgb(38, 166, 154),
        Color.FromRgb(236, 64, 122)
    ];

    public string Summary { get; private set; } = "尚未生成或加载 DXF";
    public int LineCount { get; private set; }
    public bool HasTexture => _textureBitmap is not null;
    public string TextureStatus => !HasTexture
        ? "此 DXF 没有配对纹理"
        : "已加载配准纹理";
    public bool ShowTexture
    {
        get => _overlay.ShowTexture;
        set
        {
            _overlay.ShowTexture = value;
            InvalidateVisual();
        }
    }
    public bool ShowLines
    {
        get => _overlay.ShowLines;
        set
        {
            _overlay.ShowLines = value;
            InvalidateVisual();
        }
    }
    public double TextureOpacity
    {
        get => _overlay.TextureOpacity;
        set
        {
            _overlay.TextureOpacity = value;
            InvalidateVisual();
        }
    }
    public bool ShowDirectionArrows
    {
        get => _overlay.ShowDirectionArrows;
        set
        {
            if (_overlay.ShowDirectionArrows == value)
                return;
            _overlay.ShowDirectionArrows = value;
            InvalidateVisual();
        }
    }
    public bool IsTopView =>
        Math.Abs(_yaw) < 1e-7 && Math.Abs(_tilt) < 1e-7;

    /// <summary>画布上是否有东西可看（DXF 线段或配准纹理）。</summary>
    public bool HasContent => _segments.Count > 0 || _textureBitmap is not null;

    /// <summary>
    /// 当前缩放倍率。基准 1.0 = 适应窗口，而不是纹理画布那种「1 图像像素 : 1 屏幕像素」——
    /// DXF 是矢量模型，没有像素可言，用适应窗口当基准才有可比性。
    /// </summary>
    public double Zoom => _zoom;

    /// <summary>当前平移偏移（画布坐标系）。与 <see cref="Zoom"/> 一起构成完整的视图状态。</summary>
    public Vector PanOffset => _pan;

    /// <summary>滚轮语义；与纹理画布共用 <see cref="GrayscalePreviewWheelMode"/>。</summary>
    public GrayscalePreviewWheelMode WheelMode
    {
        get => _wheelMode;
        set => _wheelMode = value;
    }

    /// <summary>缩放或平移发生变化后触发，宿主据此刷新缩放读数。</summary>
    public event EventHandler? ViewChanged;

    /// <summary>内容投影到屏幕后的尺寸，用于判定「还有没有地方可滚」。</summary>
    private Size ContentSize
    {
        get
        {
            var projected = ProjectedModelSize();
            var scale = CalculateScale();
            return new Size(projected.Width * scale, projected.Height * scale);
        }
    }

    public bool CanPanHorizontally =>
        HasContent && ContentSize.Width > Bounds.Width + 0.5;

    public bool CanPanVertically =>
        HasContent && ContentSize.Height > Bounds.Height + 0.5;

    /// <summary>画布自身坐标系下的中心点。
    /// <see cref="Control.Bounds"/> 是相对父控件的，绘制与指针坐标都在本控件坐标系里，
    /// 因此中心一律取 (Width/2, Height/2)，避免宿主带内边距时整幅图被偏移。</summary>
    private Point LocalCenter => new(Bounds.Width / 2, Bounds.Height / 2);

    public DxfPreviewControl(bool startInTopView = false)
    {
        _overlay = new DxfOverlayState(startInTopView);
        MinHeight = 360;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        ClipToBounds = true;
        if (startInTopView)
        {
            _yaw = 0;
            _tilt = 0;
        }
    }

    public void Clear()
    {
        ClearTexture();
        _segments = [];
        _rowGroups = [];
        _modelBounds = new Rect(-50, -50, 100, 100);
        _minZ = 0;
        _maxZ = 0;
        _zoom = 1;
        _pan = default;
        Summary = "正在等待生成 DXF…";
        LineCount = 0;
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>缩放到适应窗口并回到居中位置。</summary>
    public void FitToView()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>
    /// 把缩放恢复成 100%（即适应窗口的基准倍率），但保留当前平移位置：
    /// 逐层对照时常常只想退回基准倍率、不想丢掉正在看的位置。
    /// </summary>
    public void ActualSize()
    {
        if (Math.Abs(_zoom - 1) < 1e-9)
            return;
        _zoom = 1;
        ClampPan();
        InvalidateVisual();
        RaiseViewChanged();
    }

    public void ZoomIn() => ZoomBy(GrayscalePreviewViewMath.ZoomButtonStep);

    public void ZoomOut() => ZoomBy(1 / GrayscalePreviewViewMath.ZoomButtonStep);

    public void ZoomBy(double factor) => ZoomAt(LocalCenter, _zoom * factor);

    /// <summary>以屏幕上某个锚点为中心缩放，锚点下的模型点保持不动。</summary>
    public void ZoomAt(Point anchor, double zoom)
    {
        if (Math.Abs(_zoom - zoom) < 1e-12)
            return;
        var oldZoom = _zoom;
        _zoom = GrayscalePreviewViewMath.ClampZoom(zoom);
        var relative = anchor - LocalCenter - _pan;
        _pan = anchor - LocalCenter - relative * (_zoom / oldZoom);
        ClampPan();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>把平移限制在「内容不会被拖出视野」的范围内，留出一点余量。</summary>
    private void ClampPan()
    {
        if (!HasContent)
        {
            _pan = default;
            return;
        }

        var content = ContentSize;
        var limitX = Math.Max(0, (content.Width - Bounds.Width) / 2) + PanSlack;
        var limitY = Math.Max(0, (content.Height - Bounds.Height) / 2) + PanSlack;
        _pan = new Vector(
            Math.Clamp(_pan.X, -limitX, limitX),
            Math.Clamp(_pan.Y, -limitY, limitY));
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    public void SetIsometricView()
    {
        _yaw = -35 * Math.PI / 180;
        _tilt = 55 * Math.PI / 180;
        _overlay.IsTopView = false;
        FitToView();
    }

    public void SetTopView()
    {
        _yaw = 0;
        _tilt = 0;
        _overlay.IsTopView = true;
        FitToView();
    }

    public void LoadTexture(string path, double widthMm, double heightMm)
        => LoadTexture(
            path,
            new DxfTextureRegistration(
                widthMm, heightMm, widthMm, heightMm, 1, 1));

    public void LoadTexture(string path, DxfTextureRegistration registration)
        => LoadTexture(path, registration, keepView: false);

    /// <param name="keepView">
    /// 为真时保留当前缩放 / 平移 / 视角。换层时由宿主根据「切层保持视图」传入，
    /// 这样逐层对照不会每次都跳回适应窗口。
    /// </param>
    public void LoadTexture(
        string path,
        DxfTextureRegistration registration,
        bool keepView)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists || file.Length <= 0 ||
            (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("配准纹理必须是非空普通文件。");

        Bitmap? candidate = null;
        try
        {
            candidate = new Bitmap(path);
            if (candidate.PixelSize.Width <= 0 || candidate.PixelSize.Height <= 0)
                throw new InvalidDataException("配准纹理像素尺寸无效。");
            if (candidate.PixelSize.Width != registration.PixelColumns ||
                candidate.PixelSize.Height != registration.PixelRows)
                throw new InvalidDataException("配准纹理像素尺寸与 Hatch 采样信息不一致。");

            var previous = _textureBitmap;
            _textureBitmap = candidate;
            candidate = null;
            _textureFrameBounds = new Rect(
                -registration.FrameWidthMm / 2,
                -registration.FrameHeightMm / 2,
                registration.FrameWidthMm,
                registration.FrameHeightMm);
            _textureBounds = new Rect(
                registration.RasterLeftMm,
                registration.RasterBottomMm,
                registration.RasterRightMm - registration.RasterLeftMm,
                registration.RasterTopMm - registration.RasterBottomMm);
            _modelBounds = _textureFrameBounds;
            _overlay.SetTextureAvailable(true);
            previous?.Dispose();
            if (keepView)
            {
                ClampPan();
                InvalidateVisual();
                RaiseViewChanged();
            }
            else
            {
                FitToView();
            }
        }
        finally
        {
            candidate?.Dispose();
        }
    }

    public void ClearTexture()
    {
        _textureBitmap?.Dispose();
        _textureBitmap = null;
        _textureBounds = default;
        _textureFrameBounds = default;
        _overlay.SetTextureAvailable(false);
        InvalidateVisual();
    }

    public void Dispose() => ClearTexture();

    public void LoadFile(string path) => LoadFile(path, keepView: false);

    /// <param name="keepView">
    /// 为真时保留当前缩放 / 平移 / 视角，供「切层保持视图」逐层对照使用。
    /// </param>
    public void LoadFile(string path, bool keepView)
    {
        var metadata = DxfBlockMetadata.LoadForDxf(path);
        var firstPass = ScanFile(path, 0, metadata: null);
        var count = firstPass.Count;
        LineCount = count;
        var bounds = firstPass.Bounds;
        metadata?.ValidateLineCount(count);
        var stride = Math.Max(1, (int)Math.Ceiling(count / (double)MaximumDisplayedSegments));
        var secondPass = ScanFile(path, stride, metadata);
        var segments = secondPass.Segments;
        _segments = segments;
        _rowGroups = BuildRowGroups(segments);
        _modelBounds = HasTexture ? _textureFrameBounds : bounds;
        _minZ = secondPass.MinZ;
        _maxZ = secondPass.MaxZ;
        if (keepView)
        {
            ClampPan();
            InvalidateVisual();
            RaiseViewChanged();
        }
        else
        {
            FitToView();
        }
        var blockSummary = metadata is null
            ? string.Empty
            : $" · 加工块 {metadata.Blocks.Count} 个";
        Summary = stride == 1
            ? $"{Path.GetFileName(path)} · {count:N0} 条 LINE{blockSummary}"
            : $"{Path.GetFileName(path)} · {count:N0} 条 LINE{blockSummary} · 抽样显示 {segments.Count:N0} 条";
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(UiTheme.SunkenBrush, new Rect(Bounds.Size));
        if (_segments.Count == 0 && _textureBitmap is null)
        {
            var text = new FormattedText(
                "生成 DXF 后将在这里显示实际文件",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                UiTheme.UiTypeface,
                14,
                UiTheme.TextSecondaryBrush);
            context.DrawText(text, Bounds.Center - new Vector(text.Width / 2, text.Height / 2));
            return;
        }

        var projection = CreateProjection(CalculateScale(), LocalCenter + _pan);
        var viewport = new Rect(Bounds.Size);
        DrawGrid(context, projection);

        if (_overlay.ShouldDrawTexture)
            DrawTextureOverlay(context, projection);
        if (_overlay.ShowLines)
            DrawDxfSegments(context, projection, viewport);
    }

    private void DrawTextureOverlay(
        DrawingContext context,
        PlanarOverlayProjection projection)
    {
        if (_textureBitmap is null)
            return;

        if (!projection.TryCreateTextureDrawPlan(
                _textureBounds,
                _textureFrameBounds,
                _textureBitmap.Size,
                out var plan))
            return;
        var clipGeometry = CreateClipGeometry(plan.FrameQuad);
        using (context.PushGeometryClip(clipGeometry))
        using (context.PushOpacity(_overlay.TextureOpacity))
        using (context.PushTransform(plan.ImageToScreenTransform))
        {
            context.DrawImage(
                _textureBitmap,
                new Rect(_textureBitmap.Size),
                new Rect(_textureBitmap.Size));
        }
    }

    private static StreamGeometry CreateClipGeometry(ProjectedTextureQuad quad)
    {
        var geometry = new StreamGeometry();
        using var drawing = geometry.Open();
        drawing.BeginFigure(quad.RasterTopLeft, isFilled: true);
        drawing.LineTo(quad.RasterTopRight);
        drawing.LineTo(quad.RasterBottomRight);
        drawing.LineTo(quad.RasterBottomLeft);
        drawing.EndFigure(isClosed: true);
        return geometry;
    }

    private void DrawDxfSegments(
        DrawingContext context,
        PlanarOverlayProjection projection,
        Rect viewport)
    {
        if (_segments.Count == 0)
            return;

        var pens = _segments
            .Where(segment => !segment.IsBorder)
            .Select(segment => segment.BlockIndex)
            .Distinct()
            .ToDictionary(
                blockIndex => blockIndex,
                blockIndex => new Pen(
                    new SolidColorBrush(LayerColors[blockIndex % LayerColors.Length]),
                    0.9));
        var borderPen = new Pen(UiTheme.TextSecondaryBrush, 1.2);
        var arrowPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 196, 92)), 1);
        var lastRenderedScreenY = new Dictionary<int, double>();
        const double minimumRowSpacingPixels = 1.15;
        foreach (var group in _rowGroups)
        {
            if (!group.IsBorder && IsTopView)
            {
                var screenY = projection.ScreenCenter.Y -
                    (group.Y - _modelBounds.Center.Y) * projection.Scale;
                if (screenY < -2 || screenY > Bounds.Height + 2)
                    continue;
                if (lastRenderedScreenY.TryGetValue(group.BlockIndex, out var previousScreenY) &&
                    Math.Abs(screenY - previousScreenY) < minimumRowSpacingPixels)
                    continue;
                lastRenderedScreenY[group.BlockIndex] = screenY;
            }

            var endIndex = group.StartIndex + group.Count;
            for (var index = group.StartIndex; index < endIndex; index++)
            {
                var segment = _segments[index];
                var start = projection.ToScreen(segment.X1, segment.Y1, segment.Z1);
                var end = projection.ToScreen(segment.X2, segment.Y2, segment.Z2);
                var clippedStart = start;
                var clippedEnd = end;
                if (TryClipLine(ref clippedStart, ref clippedEnd, viewport))
                {
                    var pen = segment.IsBorder
                        ? borderPen
                        : pens[segment.BlockIndex];
                    context.DrawLine(pen, clippedStart, clippedEnd);
                    if (_overlay.ShouldDrawDirectionArrows && !segment.IsBorder)
                        DrawEndpointDirectionArrows(
                            context,
                            arrowPen,
                            start,
                            end,
                            viewport);
                }
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!HasContent)
            return;

        var modifiers = e.KeyModifiers;
        var zoomModifier =
            modifiers.HasFlag(KeyModifiers.Control) ||
            modifiers.HasFlag(KeyModifiers.Meta);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        // 与纹理画布共用同一套判定：⌘/Ctrl 恒为缩放，其余按滚轮模式决定，
        // Auto 模式下目标方向已经滚不动了才退化为缩放，画面永远不会「滚轮没反应」。
        var action = GrayscalePreviewViewMath.ResolveWheelAction(
            _wheelMode,
            zoomModifier,
            shift,
            CanPanVertically,
            CanPanHorizontally);

        if (action == GrayscalePreviewWheelAction.Zoom)
        {
            ZoomAt(
                e.GetPosition(this),
                GrayscalePreviewViewMath.WheelZoom(_zoom, e.Delta.Y));
        }
        else
        {
            // Shift 把竖向滚轮转成横向滚动；其余情况沿用原生方向。
            var deltaX = shift ? e.Delta.Y : e.Delta.X;
            var deltaY = shift ? 0 : e.Delta.Y;
            _pan += new Vector(
                -deltaX * GrayscalePreviewViewMath.WheelScrollStep,
                -deltaY * GrayscalePreviewViewMath.WheelScrollStep);
            ClampPan();
            InvalidateVisual();
            RaiseViewChanged();
        }

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        var isLeftButton = properties.IsLeftButtonPressed;
        var isMiddleButton = properties.IsMiddleButtonPressed;
        if (!isLeftButton && !isMiddleButton)
            return;
        if (isMiddleButton && e.ClickCount >= 2)
        {
            FitToView();
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        _panAtDragStart = _pan;
        _yawAtDragStart = _yaw;
        _tiltAtDragStart = _tilt;
        _isOrbiting = isLeftButton || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        Cursor = new Cursor(
            _isOrbiting ? StandardCursorType.Hand : StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start)
            return;
        var delta = e.GetPosition(this) - start;
        if (_isOrbiting)
        {
            _yaw = _yawAtDragStart + delta.X * 0.01;
            _tilt = NormalizeAngle(_tiltAtDragStart + delta.Y * 0.01);
            _overlay.IsTopView = IsTopView;
        }
        else
        {
            _pan = _panAtDragStart + delta;
            ClampPan();
        }
        e.Handled = true;
        InvalidateVisual();
        RaiseViewChanged();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart is null)
            return;
        _dragStart = null;
        _isOrbiting = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragStart = null;
        _isOrbiting = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private double CalculateScale()
    {
        var projected = ProjectedModelSize();
        return Math.Min(
            Math.Max(Bounds.Width - 36, 1) / Math.Max(projected.Width, 1e-9),
            Math.Max(Bounds.Height - 36, 1) / Math.Max(projected.Height, 1e-9)) * _zoom;
    }

    private Size ProjectedModelSize()
    {
        var projection = CreateProjection(1, default);
        var corners = new List<Vector>();
        foreach (var x in new[] { _modelBounds.Left, _modelBounds.Right })
        foreach (var y in new[] { _modelBounds.Top, _modelBounds.Bottom })
        foreach (var z in new[] { _minZ, _maxZ })
            corners.Add(projection.Project(x, y, z));
        return new Size(
            corners.Max(point => point.X) - corners.Min(point => point.X),
            corners.Max(point => point.Y) - corners.Min(point => point.Y));
    }

    private PlanarOverlayProjection CreateProjection(double scale, Point screenCenter) => new(
        _modelBounds.Center,
        (_minZ + _maxZ) / 2,
        _yaw,
        _tilt,
        scale,
        screenCenter);

    private static double NormalizeAngle(double angle)
    {
        while (angle > Math.PI)
            angle -= Math.PI * 2;
        while (angle < -Math.PI)
            angle += Math.PI * 2;
        return angle;
    }

    private static void DrawEndpointDirectionArrows(
        DrawingContext context,
        Pen pen,
        Point start,
        Point end,
        Rect viewport)
    {
        var vector = new Vector(end.X - start.X, end.Y - start.Y);
        var length = vector.Length;
        if (length < 5)
            return;
        var direction = vector / length;
        var normal = new Vector(-direction.Y, direction.X);
        var arrowLength = Math.Clamp(length * 0.18, 3, 6);
        var halfWidth = Math.Clamp(arrowLength * 0.42, 1.25, 2.5);

        // 起点箭头略微落在线段内部，终点箭头的尖端落在 DXF 终点；
        // 两者均沿 start -> end 指向。
        var startTip = start + direction * arrowLength;
        DrawClippedLine(context, pen, startTip, start + normal * halfWidth, viewport);
        DrawClippedLine(context, pen, startTip, start - normal * halfWidth, viewport);

        var endBase = end - direction * arrowLength;
        DrawClippedLine(context, pen, end, endBase + normal * halfWidth, viewport);
        DrawClippedLine(context, pen, end, endBase - normal * halfWidth, viewport);
    }

    private void DrawGrid(DrawingContext context, PlanarOverlayProjection projection)
    {
        DrawThreeDimensionalGrid(context, projection);
    }

    private void DrawThreeDimensionalGrid(
        DrawingContext context,
        PlanarOverlayProjection projection)
    {
        var pen = new Pen(UiTheme.BorderSubtleBrush, 1);
        var viewport = new Rect(Bounds.Size);
        const int divisions = 10;
        for (var index = 0; index <= divisions; index++)
        {
            var x = _modelBounds.Left + _modelBounds.Width * index / divisions;
            var y = _modelBounds.Top + _modelBounds.Height * index / divisions;
            DrawClippedLine(
                context,
                pen,
                projection.ToScreen(x, _modelBounds.Top, _minZ),
                projection.ToScreen(x, _modelBounds.Bottom, _minZ),
                viewport);
            DrawClippedLine(
                context,
                pen,
                projection.ToScreen(_modelBounds.Left, y, _minZ),
                projection.ToScreen(_modelBounds.Right, y, _minZ),
                viewport);
        }
    }

    private static void DrawClippedLine(
        DrawingContext context,
        Pen pen,
        Point start,
        Point end,
        Rect viewport)
    {
        if (TryClipLine(ref start, ref end, viewport))
            context.DrawLine(pen, start, end);
    }

    // Liang–Barsky 裁剪确保送入绘图后端的坐标始终位于预览视口内。
    // 这也规避了极端缩放或近乎侧视时 Skia 对超大坐标裁剪不稳定的问题。
    private static bool TryClipLine(ref Point start, ref Point end, Rect viewport)
    {
        if (!double.IsFinite(start.X) ||
            !double.IsFinite(start.Y) ||
            !double.IsFinite(end.X) ||
            !double.IsFinite(end.Y))
            return false;

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var t0 = 0.0;
        var t1 = 1.0;

        static bool Clip(double p, double q, ref double lower, ref double upper)
        {
            if (Math.Abs(p) < 1e-12)
                return q >= 0;
            var ratio = q / p;
            if (p < 0)
            {
                if (ratio > upper)
                    return false;
                lower = Math.Max(lower, ratio);
            }
            else
            {
                if (ratio < lower)
                    return false;
                upper = Math.Min(upper, ratio);
            }
            return true;
        }

        if (!Clip(-dx, start.X - viewport.Left, ref t0, ref t1) ||
            !Clip(dx, viewport.Right - start.X, ref t0, ref t1) ||
            !Clip(-dy, start.Y - viewport.Top, ref t0, ref t1) ||
            !Clip(dy, viewport.Bottom - start.Y, ref t0, ref t1))
            return false;

        var original = start;
        start = new Point(original.X + t0 * dx, original.Y + t0 * dy);
        end = new Point(original.X + t1 * dx, original.Y + t1 * dy);
        return true;
    }

    private static List<RowGroup> BuildRowGroups(List<Segment> segments)
    {
        var groups = new List<RowGroup>();
        for (var start = 0; start < segments.Count;)
        {
            var first = segments[start];
            var end = start + 1;
            while (end < segments.Count)
            {
                var candidate = segments[end];
                if (candidate.IsBorder != first.IsBorder ||
                    candidate.BlockIndex != first.BlockIndex ||
                    Math.Abs(candidate.Y1 - first.Y1) > 1e-7)
                    break;
                end++;
            }
            groups.Add(new RowGroup(
                start,
                end - start,
                first.BlockIndex,
                first.Y1,
                first.IsBorder));
            start = end;
        }
        return groups;
    }

    internal static (
        int Count,
        Rect Bounds,
        List<Segment> Segments,
        double MinZ,
        double MaxZ) ScanFile(
        string path,
        int collectEvery,
        DxfBlockMetadata? metadata)
    {
        var segments = new List<Segment>();
        var count = 0;
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var minZ = double.PositiveInfinity;
        var maxZ = double.NegativeInfinity;
        var inLine = false;
        double? x1 = null, y1 = null, z1 = null, x2 = null, y2 = null, z2 = null;

        void CompleteEntity()
        {
            if (!inLine || x1 is null || y1 is null || x2 is null || y2 is null)
                return;
            var entityIndex = count;
            var classification = metadata?.ClassifyLine(entityIndex)
                ?? new DxfLineClassification(0, false);
            var segment = new Segment(
                x1.Value,
                y1.Value,
                z1 ?? 0,
                x2.Value,
                y2.Value,
                z2 ?? 0,
                classification.BlockIndex,
                classification.IsBorder);
            minX = Math.Min(minX, Math.Min(segment.X1, segment.X2));
            minY = Math.Min(minY, Math.Min(segment.Y1, segment.Y2));
            maxX = Math.Max(maxX, Math.Max(segment.X1, segment.X2));
            maxY = Math.Max(maxY, Math.Max(segment.Y1, segment.Y2));
            minZ = Math.Min(minZ, Math.Min(segment.Z1, segment.Z2));
            maxZ = Math.Max(maxZ, Math.Max(segment.Z1, segment.Z2));
            if (collectEvery > 0 && count % collectEvery == 0)
                segments.Add(segment);
            count++;
        }

        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } codeLine && reader.ReadLine() is { } valueLine)
        {
            if (!int.TryParse(codeLine.Trim(), out var code))
                continue;
            var value = valueLine.Trim();
            if (code == 0)
            {
                CompleteEntity();
                inLine = value.Equals("LINE", StringComparison.OrdinalIgnoreCase);
                x1 = y1 = z1 = x2 = y2 = z2 = null;
                continue;
            }
            if (!inLine ||
                !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                continue;
            switch (code)
            {
                case 10: x1 = number; break;
                case 20: y1 = number; break;
                case 30: z1 = number; break;
                case 11: x2 = number; break;
                case 21: y2 = number; break;
                case 31: z2 = number; break;
            }
        }
        CompleteEntity();
        if (count == 0)
            throw new InvalidDataException("DXF 中没有可预览的 LINE 实体。");
        return (
            count,
            new Rect(minX, minY, Math.Max(maxX - minX, 1e-6), Math.Max(maxY - minY, 1e-6)),
            segments,
            double.IsFinite(minZ) ? minZ : 0,
            double.IsFinite(maxZ) ? maxZ : 0);
    }
}
