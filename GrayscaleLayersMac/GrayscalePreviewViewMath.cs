using System;
using Avalonia;

namespace GrayscaleLayersMac;

/// <summary>
/// 预览画布上滚轮的语义。这里只描述“策略”，具体判定交给
/// <see cref="GrayscalePreviewViewMath.ResolveWheelAction"/>。
/// </summary>
public enum GrayscalePreviewWheelMode
{
    /// <summary>优先滚动；当目标方向已经滚不动时自动改为缩放（推荐）。</summary>
    Auto,

    /// <summary>滚轮始终滚动画布。</summary>
    Scroll,

    /// <summary>滚轮始终缩放画布。</summary>
    Zoom
}

/// <summary>一次滚轮事件最终要执行的操作。</summary>
public enum GrayscalePreviewWheelAction
{
    Scroll,
    Zoom
}

/// <summary>
/// 预览画布的视图状态：缩放倍率 + 内容坐标系下的滚动偏移。
/// 内容坐标系以“缩放后的像素”为单位，偏移可取范围为 [0, 内容尺寸 − 视口尺寸]。
/// 当内容不大于视口时偏移恒为 0，由绘制阶段负责居中。
/// </summary>
public readonly record struct GrayscalePreviewView(double Zoom, double OffsetX, double OffsetY)
{
    public static GrayscalePreviewView Identity { get; } = new(1, 0, 0);
}

/// <summary>
/// 灰度分层预览的缩放 / 平移纯数学。刻意不触碰任何 Avalonia 控件，
/// 便于单元测试直接覆盖“滚轮是滚还是缩”“缩放是否锚定光标”等判定。
/// </summary>
public static class GrayscalePreviewViewMath
{
    public const double MinZoom = 0.02;
    public const double MaxZoom = 64;

    /// <summary>按钮与键盘的缩放步长。</summary>
    public const double ZoomButtonStep = 1.25;

    /// <summary>滚轮一个刻度滚动的像素数。</summary>
    public const double WheelScrollStep = 48;

    private const double WheelZoomBase = 1.12;

    /// <summary>单次滚轮事件允许的最大缩放指数，避免触控板惯性把倍率打飞。</summary>
    private const double MaxWheelExponent = 4;

    /// <summary>判定“可滚动”的容差；小于它的差距不应产生滚动条。</summary>
    private const double ScrollEpsilon = 0.5;

    public static double ClampZoom(double zoom)
        => double.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : 1;

    public static Size ContentSize(Size image, double zoom)
    {
        if (image.Width <= 0 || image.Height <= 0 || !double.IsFinite(zoom))
            return default;
        return new Size(
            Math.Max(image.Width * zoom, 0),
            Math.Max(image.Height * zoom, 0));
    }

    public static bool CanScroll(double contentSize, double viewportSize)
        => contentSize > viewportSize + ScrollEpsilon;

    public static double ClampOffset(double offset, double contentSize, double viewportSize)
    {
        if (!CanScroll(contentSize, viewportSize) || !double.IsFinite(offset))
            return 0;
        return Math.Clamp(offset, 0, contentSize - viewportSize);
    }

    /// <summary>把屏幕坐标换算成内容坐标（未居中时即“相对内容左上角的距离”）。</summary>
    public static double ContentFromScreen(
        double screen,
        double contentSize,
        double viewportSize,
        double offset)
        => CanScroll(contentSize, viewportSize)
            ? screen + offset
            : screen - (viewportSize - contentSize) / 2;

    /// <summary><see cref="ContentFromScreen"/> 的逆运算。</summary>
    public static double ScreenFromContent(
        double content,
        double contentSize,
        double viewportSize,
        double offset)
        => CanScroll(contentSize, viewportSize)
            ? content - offset
            : content + (viewportSize - contentSize) / 2;

    public static double FitZoom(Size image, Size viewport)
    {
        if (image.Width <= 0 || image.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return 1;
        return ClampZoom(Math.Min(viewport.Width / image.Width, viewport.Height / image.Height));
    }

    /// <summary>滚轮增量换算成新倍率；触控板的连续增量同样适用。</summary>
    public static double WheelZoom(double zoom, double delta)
    {
        if (!double.IsFinite(delta) || delta == 0)
            return ClampZoom(zoom);
        var exponent = Math.Clamp(delta, -MaxWheelExponent, MaxWheelExponent);
        return ClampZoom(zoom * Math.Pow(WheelZoomBase, exponent));
    }

    /// <summary>
    /// 以 <paramref name="anchor"/>（视口坐标系）为锚点缩放：锚点下的那个像素在缩放前后
    /// 停留在屏幕上的同一位置。这是区别于“从左上角缩放”的关键手感。
    /// </summary>
    public static GrayscalePreviewView ZoomAt(
        GrayscalePreviewView view,
        Size image,
        Size viewport,
        Point anchor,
        double requestedZoom)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return new GrayscalePreviewView(ClampZoom(requestedZoom), 0, 0);

        var zoom = ClampZoom(requestedZoom);
        if (view.Zoom <= 0)
            return new GrayscalePreviewView(zoom, 0, 0);

        var previous = ContentSize(image, view.Zoom);
        var next = ContentSize(image, zoom);

        // 锚点先落到与倍率无关的图像像素坐标系，缩放后再映射回屏幕。
        var anchorPixelX = ContentFromScreen(anchor.X, previous.Width, viewport.Width, view.OffsetX) / view.Zoom;
        var anchorPixelY = ContentFromScreen(anchor.Y, previous.Height, viewport.Height, view.OffsetY) / view.Zoom;

        var offsetX = CanScroll(next.Width, viewport.Width)
            ? anchorPixelX * zoom - anchor.X
            : 0;
        var offsetY = CanScroll(next.Height, viewport.Height)
            ? anchorPixelY * zoom - anchor.Y
            : 0;

        return new GrayscalePreviewView(
            zoom,
            ClampOffset(offsetX, next.Width, viewport.Width),
            ClampOffset(offsetY, next.Height, viewport.Height));
    }

    /// <summary>
    /// 拖拽平移：指针向右移动时内容跟着向右走，因此偏移要减掉位移量。
    /// </summary>
    public static GrayscalePreviewView PanBy(
        GrayscalePreviewView view,
        Size image,
        Size viewport,
        Vector delta)
    {
        var content = ContentSize(image, view.Zoom);
        return new GrayscalePreviewView(
            view.Zoom,
            ClampOffset(view.OffsetX - delta.X, content.Width, viewport.Width),
            ClampOffset(view.OffsetY - delta.Y, content.Height, viewport.Height));
    }

    /// <summary>保持倍率，把内容挪到视口正中间。</summary>
    public static GrayscalePreviewView CenterContent(
        GrayscalePreviewView view,
        Size image,
        Size viewport)
    {
        var content = ContentSize(image, view.Zoom);
        return new GrayscalePreviewView(
            view.Zoom,
            CanScroll(content.Width, viewport.Width) ? (content.Width - viewport.Width) / 2 : 0,
            CanScroll(content.Height, viewport.Height) ? (content.Height - viewport.Height) / 2 : 0);
    }

    public static GrayscalePreviewView Fit(Size image, Size viewport)
        => CenterContent(new GrayscalePreviewView(FitZoom(image, viewport), 0, 0), image, viewport);

    /// <summary>把任意视图收敛到合法范围（倍率受限、偏移不出界）。</summary>
    public static GrayscalePreviewView Clamp(
        GrayscalePreviewView view,
        Size image,
        Size viewport)
    {
        var zoom = ClampZoom(view.Zoom);
        var content = ContentSize(image, zoom);
        return new GrayscalePreviewView(
            zoom,
            ClampOffset(view.OffsetX, content.Width, viewport.Width),
            ClampOffset(view.OffsetY, content.Height, viewport.Height));
    }

    public static (bool Horizontal, bool Vertical) ScrollableAxes(
        GrayscalePreviewView view,
        Size image,
        Size viewport)
    {
        var content = ContentSize(image, view.Zoom);
        return (
            CanScroll(content.Width, viewport.Width),
            CanScroll(content.Height, viewport.Height));
    }

    /// <summary>
    /// 判定一次滚轮事件该滚动还是缩放：
    /// ⌘/Ctrl 恒为缩放（macOS 触控板捏合也会带上该修饰键）；
    /// 其余情况按模式决定，Auto 模式下目标方向滚不动时才退化为缩放，
    /// 这样画布永远不会出现“滚轮没反应”的死角。
    /// </summary>
    public static GrayscalePreviewWheelAction ResolveWheelAction(
        GrayscalePreviewWheelMode mode,
        bool zoomModifier,
        bool shiftModifier,
        bool canScrollVertically,
        bool canScrollHorizontally)
    {
        if (zoomModifier)
            return GrayscalePreviewWheelAction.Zoom;
        if (mode == GrayscalePreviewWheelMode.Zoom)
            return GrayscalePreviewWheelAction.Zoom;
        if (mode == GrayscalePreviewWheelMode.Scroll)
            return GrayscalePreviewWheelAction.Scroll;

        return shiftModifier
            ? canScrollHorizontally
                ? GrayscalePreviewWheelAction.Scroll
                : GrayscalePreviewWheelAction.Zoom
            : canScrollVertically
                ? GrayscalePreviewWheelAction.Scroll
                : GrayscalePreviewWheelAction.Zoom;
    }
}
