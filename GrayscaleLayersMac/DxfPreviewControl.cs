using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GrayscaleLayersMac;

public sealed class DxfPreviewControl : Control, IDisposable
{
    private sealed record Segment(
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
    private readonly DxfOverlayState _overlay = new();
    private Bitmap? _textureBitmap;
    private Rect _textureBounds;
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
    public bool HasTexture => _textureBitmap is not null;
    public string TextureStatus => !HasTexture
        ? "此 DXF 没有配对纹理"
        : _overlay.IsTopView
            ? "已加载配准纹理"
            : "纹理对齐仅在顶视图显示";
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
    public DxfPreviewControl()
    {
        MinHeight = 360;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
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
        InvalidateVisual();
    }

    public void FitToView()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

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
    {
        if (!double.IsFinite(widthMm) || widthMm <= 0 ||
            !double.IsFinite(heightMm) || heightMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthMm));

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

            var previous = _textureBitmap;
            _textureBitmap = candidate;
            candidate = null;
            _textureBounds = new Rect(-widthMm / 2, -heightMm / 2, widthMm, heightMm);
            _modelBounds = _textureBounds;
            _overlay.SetTextureAvailable(true);
            previous?.Dispose();
            FitToView();
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
        _overlay.SetTextureAvailable(false);
        InvalidateVisual();
    }

    public void Dispose() => ClearTexture();

    public void LoadFile(string path)
    {
        var firstPass = ScanFile(path, 0, false);
        var count = firstPass.Count;
        var bounds = firstPass.Bounds;
        var stride = Math.Max(1, (int)Math.Ceiling(count / (double)MaximumDisplayedSegments));
        var secondPass = ScanFile(path, stride, firstPass.HasVerticalLine);
        var segments = secondPass.Segments;
        _segments = segments;
        _rowGroups = BuildRowGroups(segments);
        _modelBounds = bounds;
        _minZ = secondPass.MinZ;
        _maxZ = secondPass.MaxZ;
        FitToView();
        var blockSummary = $" · 分析出 {secondPass.BlockCount} 个加工块";
        Summary = stride == 1
            ? $"{Path.GetFileName(path)} · {count:N0} 条 LINE{blockSummary} "
            : $"{Path.GetFileName(path)} · {count:N0} 条 LINE{blockSummary} · 抽样显示 {segments.Count:N0} 条";
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(15, 18, 23)), Bounds);
        if (_segments.Count == 0 && _textureBitmap is null)
        {
            var text = new FormattedText(
                "生成 DXF 后将在这里显示实际文件",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                14,
                new SolidColorBrush(Color.FromRgb(150, 156, 166)));
            context.DrawText(text, Bounds.Center - new Vector(text.Width / 2, text.Height / 2));
            return;
        }

        var scale = CalculateScale();
        var center = Bounds.Center + _pan;
        var viewport = new Rect(Bounds.Size);
        DrawGrid(context, scale, center);

        if (_overlay.ShouldDrawTexture)
            DrawTextureOverlay(context, scale, center);
        if (_overlay.ShowLines)
            DrawDxfSegments(context, scale, center, viewport);
    }

    private void DrawTextureOverlay(DrawingContext context, double scale, Point center)
    {
        if (_textureBitmap is null)
            return;

        // PNG image Y increases downwards while DXF model Y increases upwards.
        var topLeft = ToScreen(_textureBounds.Left, _textureBounds.Bottom, 0, scale, center);
        var bottomRight = ToScreen(_textureBounds.Right, _textureBounds.Top, 0, scale, center);
        var destination = new Rect(topLeft, bottomRight);
        using (context.PushOpacity(_overlay.TextureOpacity))
            context.DrawImage(_textureBitmap, new Rect(_textureBitmap.Size), destination);
    }

    private void DrawDxfSegments(
        DrawingContext context,
        double scale,
        Point center,
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
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(170, 176, 186)), 1.2);
        var arrowPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 196, 92)), 1);
        var lastRenderedScreenY = new Dictionary<int, double>();
        const double minimumRowSpacingPixels = 1.15;
        foreach (var group in _rowGroups)
        {
            if (!group.IsBorder && IsTopView)
            {
                var screenY = center.Y - (group.Y - _modelBounds.Center.Y) * scale;
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
                var start = ToScreen(segment.X1, segment.Y1, segment.Z1, scale, center);
                var end = ToScreen(segment.X2, segment.Y2, segment.Z2, scale, center);
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
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * Math.Pow(1.18, e.Delta.Y), 0.1, 100);
        var pointer = e.GetPosition(this);
        var relative = pointer - Bounds.Center - _pan;
        _pan = pointer - Bounds.Center - relative * (_zoom / oldZoom);
        e.Handled = true;
        InvalidateVisual();
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
        }
        e.Handled = true;
        InvalidateVisual();
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
        var corners = new List<Vector>();
        foreach (var x in new[] { _modelBounds.Left, _modelBounds.Right })
        foreach (var y in new[] { _modelBounds.Top, _modelBounds.Bottom })
        foreach (var z in new[] { _minZ, _maxZ })
            corners.Add(Project(x, y, z));
        return new Size(
            corners.Max(point => point.X) - corners.Min(point => point.X),
            corners.Max(point => point.Y) - corners.Min(point => point.Y));
    }

    private Vector Project(double x, double y, double z)
    {
        var dx = x - _modelBounds.Center.X;
        var dy = y - _modelBounds.Center.Y;
        var dz = z - (_minZ + _maxZ) / 2;
        var horizontal = Math.Cos(_yaw) * dx - Math.Sin(_yaw) * dy;
        var away = Math.Sin(_yaw) * dx + Math.Cos(_yaw) * dy;
        var vertical = away * Math.Cos(_tilt) + dz * Math.Sin(_tilt);
        return new Vector(horizontal, vertical);
    }

    private bool IsTopView =>
        Math.Abs(_yaw) < 1e-7 && Math.Abs(_tilt) < 1e-7;

    private static double NormalizeAngle(double angle)
    {
        while (angle > Math.PI)
            angle -= Math.PI * 2;
        while (angle < -Math.PI)
            angle += Math.PI * 2;
        return angle;
    }

    private Point ToScreen(double x, double y, double z, double scale, Point center)
    {
        var projected = Project(x, y, z);
        return new Point(center.X + projected.X * scale, center.Y - projected.Y * scale);
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

    private void DrawGrid(DrawingContext context, double scale, Point center)
    {
        DrawThreeDimensionalGrid(context, scale, center);
    }

    private void DrawThreeDimensionalGrid(DrawingContext context, double scale, Point center)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), 1);
        var viewport = new Rect(Bounds.Size);
        const int divisions = 10;
        for (var index = 0; index <= divisions; index++)
        {
            var x = _modelBounds.Left + _modelBounds.Width * index / divisions;
            var y = _modelBounds.Top + _modelBounds.Height * index / divisions;
            DrawClippedLine(
                context,
                pen,
                ToScreen(x, _modelBounds.Top, _minZ, scale, center),
                ToScreen(x, _modelBounds.Bottom, _minZ, scale, center),
                viewport);
            DrawClippedLine(
                context,
                pen,
                ToScreen(_modelBounds.Left, y, _minZ, scale, center),
                ToScreen(_modelBounds.Right, y, _minZ, scale, center),
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

    private static (
        int Count,
        Rect Bounds,
        List<Segment> Segments,
        int BlockCount,
        bool HasVerticalLine,
        double MinZ,
        double MaxZ) ScanFile(
        string path,
        int collectEvery,
        bool detectGeneratedBorder)
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
        var blockIndex = 0;
        double? previousHatchY = null;
        var hasVerticalLine = false;
        double? x1 = null, y1 = null, z1 = null, x2 = null, y2 = null, z2 = null;

        void CompleteEntity()
        {
            if (!inLine || x1 is null || y1 is null || x2 is null || y2 is null)
                return;
            var entityIndex = count;
            var vertical = Math.Abs(x1.Value - x2.Value) <= 1e-8 &&
                           Math.Abs(y1.Value - y2.Value) > 1e-8;
            hasVerticalLine |= vertical;
            var isBorder = detectGeneratedBorder && entityIndex < 4;
            if (!isBorder)
            {
                if (previousHatchY.HasValue && y1.Value > previousHatchY.Value + 1e-7)
                    blockIndex++;
                previousHatchY = y1.Value;
            }
            var segment = new Segment(
                x1.Value,
                y1.Value,
                z1 ?? 0,
                x2.Value,
                y2.Value,
                z2 ?? 0,
                blockIndex,
                isBorder);
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
            count > (detectGeneratedBorder ? 4 : 0) ? blockIndex + 1 : 0,
            hasVerticalLine,
            double.IsFinite(minZ) ? minZ : 0,
            double.IsFinite(maxZ) ? maxZ : 0);
    }
}
