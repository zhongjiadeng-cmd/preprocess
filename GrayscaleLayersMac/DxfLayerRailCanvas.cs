using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// DXF 预览的图层侧栏：一行一层，展开态显示「层号 + 层名」，收拢态只留层号。
///
/// 与纹理界面的 <see cref="GrayscaleLayerThumbnailCanvas"/> 是同一套视觉与交互（同样的
/// 行高节奏、选中描边、收拢动画、点击命中判定），差别只在没有缩略图可画——DXF 层没有
/// 位图预览，所以行高更小、只排文字。
/// </summary>
public sealed class DxfLayerRailCanvas : Control
{
    private const double RowHeight = 54;
    private const double CompactRowHeight = 36;
    private const double RowSpacing = 4;

    private IReadOnlyList<DxfLayerPreviewItem> _items = [];

    /// <summary>点击某一行时触发，参数是该层的索引。</summary>
    public event EventHandler<int>? LayerClicked;

    public int SelectedIndex { get; set; } = -1;
    public bool IsCompact { get; private set; }

    public void SetCompact(bool compact)
    {
        IsCompact = compact;
        Resize();
        InvalidateVisual();
    }

    /// <summary>按 y 坐标反算行号；落在列表之外返回 -1。</summary>
    public static int GetIndexAt(double y, int itemCount, bool compact = false)
    {
        if (y < 0)
            return -1;
        var pitch = compact ? CompactRowHeight : RowHeight;
        var index = (int)(y / pitch);
        return index >= 0 && index < itemCount ? index : -1;
    }

    public void SetItems(IReadOnlyList<DxfLayerPreviewItem> items)
    {
        _items = items ?? [];
        Resize();
        InvalidateVisual();
    }

    private void Resize()
    {
        Height = _items.Count * (IsCompact ? CompactRowHeight : RowHeight);
        InvalidateMeasure();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pitch = IsCompact ? CompactRowHeight : RowHeight;
        for (var index = 0; index < _items.Count; index++)
        {
            var row = new Rect(0, index * pitch, Bounds.Width, pitch - RowSpacing);
            var selected = index == SelectedIndex;
            context.DrawRectangle(
                selected ? UiTheme.CardBrush : Brushes.Transparent,
                new Pen(selected ? UiTheme.AccentBrush : UiTheme.BorderSubtleBrush, selected ? 2 : 1),
                row.Deflate(2));

            var number = new FormattedText(
                $"{index:D2}",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                IsCompact ? 11 : 12,
                selected ? UiTheme.TextPrimaryBrush : UiTheme.TextSecondaryBrush);

            if (IsCompact)
            {
                context.DrawText(
                    number,
                    new Point(
                        (Bounds.Width - number.Width) / 2,
                        row.Y + (row.Height - number.Height) / 2));
                continue;
            }

            context.DrawText(number, new Point(10, row.Y + 7));
            var name = FitText(
                _items[index].Name,
                Math.Max(0, Bounds.Width - 20),
                selected ? UiTheme.TextPrimaryBrush : UiTheme.TextSecondaryBrush);
            context.DrawText(name, new Point(10, row.Y + 26));
        }
    }

    /// <summary>
    /// 把层名压进可用宽度。Avalonia 11 的 <see cref="FormattedText"/> 只有带 CultureInfo 的
    /// 构造函数、没有约束参数，所以这里自己按宽度二分裁剪，避免长文件名把整行撑破。
    /// </summary>
    private static FormattedText FitText(string text, double maxWidth, IBrush foreground)
    {
        var formatted = Measure(text, foreground);
        if (formatted.Width <= maxWidth || maxWidth <= 0)
            return formatted;

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Measure(text[..middle] + "…", foreground).Width <= maxWidth)
                low = middle;
            else
                high = middle - 1;
        }

        return low <= 0
            ? Measure("…", foreground)
            : Measure(text[..low] + "…", foreground);
    }

    private static FormattedText Measure(string text, IBrush foreground) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        Typeface.Default,
        10,
        foreground);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var index = GetIndexAt(e.GetPosition(this).Y, _items.Count, IsCompact);
        if (index < 0)
            return;

        LayerClicked?.Invoke(this, index);
        e.Handled = true;
    }
}
