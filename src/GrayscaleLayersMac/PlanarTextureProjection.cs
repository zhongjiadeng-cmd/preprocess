using Avalonia;

namespace GrayscaleLayersMac;

internal readonly record struct PlanarTextureDrawPlan(
    ProjectedTextureQuad TextureQuad,
    ProjectedTextureQuad FrameQuad,
    Matrix ImageToScreenTransform);

internal readonly record struct PlanarOverlayProjection(
    Point ModelCenter,
    double ModelZCenter,
    double YawRadians,
    double TiltRadians,
    double Scale,
    Point ScreenCenter)
{
    public Vector Project(double x, double y, double z)
    {
        var dx = x - ModelCenter.X;
        var dy = y - ModelCenter.Y;
        var dz = z - ModelZCenter;
        var horizontal = Math.Cos(YawRadians) * dx - Math.Sin(YawRadians) * dy;
        var away = Math.Sin(YawRadians) * dx + Math.Cos(YawRadians) * dy;
        var vertical = away * Math.Cos(TiltRadians) + dz * Math.Sin(TiltRadians);
        return new Vector(horizontal, vertical);
    }

    public Point ToScreen(double x, double y, double z)
    {
        var projected = Project(x, y, z);
        return new Point(
            ScreenCenter.X + projected.X * Scale,
            ScreenCenter.Y - projected.Y * Scale);
    }

    public ProjectedTextureQuad ProjectRasterBounds(Rect bounds)
    {
        var corners = ProjectedTextureQuad.ModelCorners(bounds);
        return new ProjectedTextureQuad(
            ToScreen(corners.RasterTopLeft.X, corners.RasterTopLeft.Y, 0),
            ToScreen(corners.RasterTopRight.X, corners.RasterTopRight.Y, 0),
            ToScreen(corners.RasterBottomRight.X, corners.RasterBottomRight.Y, 0),
            ToScreen(corners.RasterBottomLeft.X, corners.RasterBottomLeft.Y, 0));
    }

    public bool TryCreateTextureDrawPlan(
        Rect textureBounds,
        Rect frameBounds,
        Size pixelSize,
        out PlanarTextureDrawPlan plan)
    {
        plan = default;
        var textureQuad = ProjectRasterBounds(textureBounds);
        var frameQuad = ProjectRasterBounds(frameBounds);
        if (!textureQuad.IsFinite || !frameQuad.IsFinite)
            return false;

        if (!textureQuad.TryCreateImageToScreenTransform(
                pixelSize,
                out var imageToScreenTransform))
            return false;

        plan = new PlanarTextureDrawPlan(
            textureQuad,
            frameQuad,
            imageToScreenTransform);
        return true;
    }
}

internal readonly record struct ProjectedTextureQuad(
    Point RasterTopLeft,
    Point RasterTopRight,
    Point RasterBottomRight,
    Point RasterBottomLeft)
{
    public static ProjectedTextureQuad ModelCorners(Rect bounds) => new(
        new Point(bounds.Left, bounds.Bottom),
        new Point(bounds.Right, bounds.Bottom),
        new Point(bounds.Right, bounds.Top),
        new Point(bounds.Left, bounds.Top));

    public bool IsFinite =>
        double.IsFinite(RasterTopLeft.X) && double.IsFinite(RasterTopLeft.Y) &&
        double.IsFinite(RasterTopRight.X) && double.IsFinite(RasterTopRight.Y) &&
        double.IsFinite(RasterBottomRight.X) && double.IsFinite(RasterBottomRight.Y) &&
        double.IsFinite(RasterBottomLeft.X) && double.IsFinite(RasterBottomLeft.Y);

    public Matrix CreateImageToScreenTransform(Size pixelSize)
    {
        if (!double.IsFinite(pixelSize.Width) || pixelSize.Width <= 0 ||
            !double.IsFinite(pixelSize.Height) || pixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));

        if (!IsFinite)
            throw new ArgumentOutOfRangeException(nameof(ProjectedTextureQuad));

        if (!TryCreateImageToScreenTransform(pixelSize, out var transform))
            throw new InvalidOperationException("平面纹理投影必须形成平行四边形。");

        return transform;
    }

    public bool TryCreateImageToScreenTransform(Size pixelSize, out Matrix transform)
    {
        transform = default;
        if (!double.IsFinite(pixelSize.Width) || pixelSize.Width <= 0 ||
            !double.IsFinite(pixelSize.Height) || pixelSize.Height <= 0 ||
            !IsFinite)
            return false;

        var across = (RasterTopRight - RasterTopLeft) / pixelSize.Width;
        var down = (RasterBottomLeft - RasterTopLeft) / pixelSize.Height;
        if (!IsFiniteVector(across) || !IsFiniteVector(down))
            return false;

        var expectedBottomRight = RasterTopLeft +
            across * pixelSize.Width + down * pixelSize.Height;
        if (!IsFinitePoint(expectedBottomRight))
            return false;

        const double absoluteTolerance = 1e-7;
        const double relativeTolerance = 1e-12;
        var tolerance = Math.Max(
            absoluteTolerance,
            relativeTolerance * MaximumCoordinateMagnitude(expectedBottomRight));
        var errorX = Math.Abs(expectedBottomRight.X - RasterBottomRight.X);
        var errorY = Math.Abs(expectedBottomRight.Y - RasterBottomRight.Y);
        if (!double.IsFinite(errorX) || !double.IsFinite(errorY) ||
            errorX > tolerance || errorY > tolerance)
            return false;

        transform = new Matrix(
            across.X, across.Y, down.X, down.Y,
            RasterTopLeft.X, RasterTopLeft.Y);
        return true;
    }

    private double MaximumCoordinateMagnitude(Point expectedBottomRight) => Math.Max(
        1,
        Math.Max(
            Math.Max(MaximumMagnitude(RasterTopLeft), MaximumMagnitude(RasterTopRight)),
            Math.Max(
                Math.Max(
                    MaximumMagnitude(RasterBottomRight),
                    MaximumMagnitude(RasterBottomLeft)),
                MaximumMagnitude(expectedBottomRight))));

    private static double MaximumMagnitude(Point point) =>
        Math.Max(Math.Abs(point.X), Math.Abs(point.Y));

    private static bool IsFinitePoint(Point point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static bool IsFiniteVector(Vector vector) =>
        double.IsFinite(vector.X) && double.IsFinite(vector.Y);
}
