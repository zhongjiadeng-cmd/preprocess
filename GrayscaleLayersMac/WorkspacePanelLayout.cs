using Avalonia.Controls;

namespace GrayscaleLayersMac;

/// <summary>将宽度下限放在可调整的列上，使拖动与窗口缩放使用同一组边界。</summary>
internal static class WorkspacePanelLayout
{
    public const double SplitterWidth = 5;

    public static ColumnDefinitions Columns(
        double previewMinimum = 320,
        double inspectorMinimum = 300,
        double previewRatio = .75)
    {
        return new ColumnDefinitions
        {
            new() { Width = new GridLength(previewRatio, GridUnitType.Star), MinWidth = previewMinimum },
            new(new GridLength(SplitterWidth)),
            new() { Width = new GridLength(1 - previewRatio, GridUnitType.Star), MinWidth = inspectorMinimum }
        };
    }
}
