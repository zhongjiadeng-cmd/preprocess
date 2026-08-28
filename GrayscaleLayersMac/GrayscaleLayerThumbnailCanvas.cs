using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class GrayscaleLayerThumbnailCanvas : Control
{
    private const double RowHeight = 112;
    private const double CompactRowHeight = 36;
    private IReadOnlyList<GrayscaleLayerPreviewItem> _items = [];

    public event EventHandler<int>? LayerClicked;

    public int SelectedIndex { get; set; } = -1;
    public bool IsCompact { get; private set; }

    public void SetCompact(bool compact)
    {
        IsCompact = compact;
        Height = _items.Count * (compact ? CompactRowHeight : RowHeight);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public static int GetIndexAt(double y, int itemCount, bool compact = false)
    {
        if (y < 0)
            return -1;
        var index = (int)(y / (compact ? CompactRowHeight : RowHeight));
        return index >= 0 && index < itemCount ? index : -1;
    }

    public void SetItems(IReadOnlyList<GrayscaleLayerPreviewItem> items)
    {
        _items = items ?? [];
        Height = _items.Count * RowHeight;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        try
        {
            for (var index = 0; index < _items.Count; index++)
            {
                var rowHeight = IsCompact ? CompactRowHeight : RowHeight;
                var row = new Rect(0, index * rowHeight, Bounds.Width, rowHeight - 4);
                var selected = index == SelectedIndex;
                context.DrawRectangle(
                    selected ? UiTheme.CardBrush : Brushes.Transparent,
                    new Pen(selected ? UiTheme.AccentBrush : UiTheme.BorderSubtleBrush, selected ? 2 : 1),
                    row.Deflate(2));

                var item = _items[index];
                if (IsCompact)
                {
                    var layerText = new FormattedText(
                        $"{index + 1:D2}",
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        Typeface.Default,
                        11,
                        selected ? UiTheme.TextPrimaryBrush : UiTheme.TextSecondaryBrush);
                    context.DrawText(
                        layerText,
                        new Point((Bounds.Width - layerText.Width) / 2, row.Y + 8));
                    continue;
                }
                var imageRect = new Rect(8, row.Y + 8, Math.Max(0, Bounds.Width - 16), 70);
                if (item.Thumbnail is not null)
                {
                    var scale = Math.Min(
                        imageRect.Width / item.Thumbnail.Size.Width,
                        imageRect.Height / item.Thumbnail.Size.Height);
                    var target = new Rect(
                        imageRect.X + (imageRect.Width - item.Thumbnail.Size.Width * scale) / 2,
                        imageRect.Y + (imageRect.Height - item.Thumbnail.Size.Height * scale) / 2,
                        item.Thumbnail.Size.Width * scale,
                        item.Thumbnail.Size.Height * scale);
                    context.DrawImage(
                        item.Thumbnail,
                        new Rect(item.Thumbnail.Size),
                        target);
                }
                else
                {
                    context.DrawRectangle(UiTheme.SunkenBrush, null, imageRect);
                }

                var text = new FormattedText(
                    item.DisplayName,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    10,
                    UiTheme.TextSecondaryBrush);
                context.DrawText(text, new Point(8, row.Y + 83));
            }
        }
        catch
        {
            // A damaged thumbnail must not escape Avalonia's render loop.
        }
    }

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
