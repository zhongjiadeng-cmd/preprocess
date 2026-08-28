using Avalonia;

namespace GrayscaleLayersMac;

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

        var across = (RasterTopRight - RasterTopLeft) / pixelSize.Width;
        var down = (RasterBottomLeft - RasterTopLeft) / pixelSize.Height;
        var expectedBottomRight = RasterTopLeft +
            across * pixelSize.Width + down * pixelSize.Height;
        const double tolerance = 1e-7;
        if (Math.Abs(expectedBottomRight.X - RasterBottomRight.X) > tolerance ||
            Math.Abs(expectedBottomRight.Y - RasterBottomRight.Y) > tolerance)
        {
            throw new InvalidOperationException("平面纹理投影必须形成平行四边形。");
        }

        return new Matrix(
            across.X, across.Y, down.X, down.Y,
            RasterTopLeft.X, RasterTopLeft.Y);
    }
}
