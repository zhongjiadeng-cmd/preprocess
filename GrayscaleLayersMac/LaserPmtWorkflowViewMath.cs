using Avalonia;

namespace GrayscaleLayersMac;

public static class LaserPmtWorkflowViewMath
{
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 24;

    public static double ClampZoom(double zoom) =>
        Math.Clamp(double.IsFinite(zoom) ? zoom : 1, MinimumZoom, MaximumZoom);

    public static Point WorldToScreen(
        Point world,
        LaserPmtCanvasViewport viewport,
        Size controlSize) => new(
            controlSize.Width / 2 + viewport.PanX + world.X * viewport.Zoom,
            controlSize.Height / 2 + viewport.PanY + world.Y * viewport.Zoom);

    public static Point ScreenToWorld(
        Point screen,
        LaserPmtCanvasViewport viewport,
        Size controlSize)
    {
        var zoom = ClampZoom(viewport.Zoom);
        return new Point(
            (screen.X - controlSize.Width / 2 - viewport.PanX) / zoom,
            (screen.Y - controlSize.Height / 2 - viewport.PanY) / zoom);
    }

    public static Rect WorldRectToScreen(
        Rect world,
        LaserPmtCanvasViewport viewport,
        Size controlSize)
    {
        var topLeft = WorldToScreen(world.TopLeft, viewport, controlSize);
        return new Rect(topLeft, new Size(world.Width * viewport.Zoom, world.Height * viewport.Zoom));
    }

    public static LaserPmtCanvasViewport ZoomAt(
        LaserPmtCanvasViewport viewport,
        Point screenAnchor,
        Size controlSize,
        double requestedZoom)
    {
        var worldAnchor = ScreenToWorld(screenAnchor, viewport, controlSize);
        var zoom = ClampZoom(requestedZoom);
        return new LaserPmtCanvasViewport(
            zoom,
            screenAnchor.X - controlSize.Width / 2 - worldAnchor.X * zoom,
            screenAnchor.Y - controlSize.Height / 2 - worldAnchor.Y * zoom);
    }

    public static LaserPmtCanvasViewport FitBounds(
        Rect worldBounds,
        Size controlSize,
        double padding)
    {
        if (worldBounds.Width <= 0 || worldBounds.Height <= 0 ||
            controlSize.Width <= padding * 2 || controlSize.Height <= padding * 2)
            return new LaserPmtCanvasViewport(1, 0, 0);
        var zoom = ClampZoom(Math.Min(
            (controlSize.Width - padding * 2) / worldBounds.Width,
            (controlSize.Height - padding * 2) / worldBounds.Height));
        return new LaserPmtCanvasViewport(
            zoom,
            -worldBounds.Center.X * zoom,
            -worldBounds.Center.Y * zoom);
    }
}
