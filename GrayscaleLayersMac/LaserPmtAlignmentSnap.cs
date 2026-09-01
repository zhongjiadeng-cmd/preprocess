namespace GrayscaleLayersMac;

public readonly record struct LaserPmtAlignmentSnapResult(
    LaserPmtWorkflowBounds Bounds,
    double? VerticalGuide,
    double? HorizontalGuide);

public static class LaserPmtAlignmentSnap
{
    public static LaserPmtAlignmentSnapResult Apply(
        LaserPmtWorkflowBounds moving,
        IReadOnlyList<LaserPmtWorkflowBounds> otherPmts,
        LaserPmtWorkflowBounds workpiece,
        double tolerance)
    {
        if (!moving.IsFinite || !moving.HasPositiveSize)
            throw new ArgumentException("移动中的 PMT 边界无效。", nameof(moving));
        if (!double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        var x = FindNearest(
            [moving.Left, moving.Left + moving.Width / 2, moving.Right],
            BuildReferences(otherPmts, workpiece, horizontal: true),
            tolerance);
        var y = FindNearest(
            [moving.Top, moving.Top + moving.Height / 2, moving.Bottom],
            BuildReferences(otherPmts, workpiece, horizontal: false),
            tolerance);
        return new LaserPmtAlignmentSnapResult(
            moving with
            {
                Left = moving.Left + (x?.Delta ?? 0),
                Top = moving.Top + (y?.Delta ?? 0)
            },
            x?.Guide,
            y?.Guide);
    }

    private static IReadOnlyList<double> BuildReferences(
        IReadOnlyList<LaserPmtWorkflowBounds> others,
        LaserPmtWorkflowBounds workpiece,
        bool horizontal)
    {
        var result = new List<double>(3 + others.Count * 3);
        AddAnchors(result, workpiece, horizontal);
        foreach (var bounds in others)
            AddAnchors(result, bounds, horizontal);
        return result;
    }

    private static void AddAnchors(List<double> result, LaserPmtWorkflowBounds bounds, bool horizontal)
    {
        if (horizontal)
        {
            result.Add(bounds.Left);
            result.Add(bounds.Left + bounds.Width / 2);
            result.Add(bounds.Right);
        }
        else
        {
            result.Add(bounds.Top);
            result.Add(bounds.Top + bounds.Height / 2);
            result.Add(bounds.Bottom);
        }
    }

    private static AxisSnap? FindNearest(
        IReadOnlyList<double> movingAnchors,
        IReadOnlyList<double> references,
        double tolerance)
    {
        AxisSnap? nearest = null;
        foreach (var moving in movingAnchors)
        foreach (var reference in references)
        {
            var delta = reference - moving;
            if (Math.Abs(delta) > tolerance ||
                nearest is not null && Math.Abs(delta) >= Math.Abs(nearest.Value.Delta))
                continue;
            nearest = new AxisSnap(delta, reference);
        }
        return nearest;
    }

    private readonly record struct AxisSnap(double Delta, double Guide);
}
