using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class PmtMatrixSelectionEventArgs(int rows, int columns) : EventArgs
{
    public int Rows { get; } = rows;
    public int Columns { get; } = columns;
}

/// <summary>WPS-style rectangular matrix chooser with live hover preview.</summary>
public sealed class PmtMatrixPicker : Control
{
    private const double CellSize = 22;
    private const double Gap = 4;
    private const double HeaderHeight = 38;
    private const double PaddingSize = 14;
    private int _hoverRows;
    private int _hoverColumns;

    public int MaximumRows { get; init; } = 12;
    public int MaximumColumns { get; init; } = 16;
    public event EventHandler<PmtMatrixSelectionEventArgs>? PreviewChanged;
    public event EventHandler<PmtMatrixSelectionEventArgs>? SelectionCommitted;
    public event EventHandler? PreviewCancelled;

    public PmtMatrixPicker()
    {
        Width = PaddingSize * 2 + 16 * (CellSize + Gap) - Gap;
        Height = HeaderHeight + PaddingSize + 12 * (CellSize + Gap) - Gap + PaddingSize;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(UiTheme.CardBrush, new Rect(Bounds.Size));
        var heading = _hoverRows > 0
            ? $"{_hoverRows} 行 × {_hoverColumns} 列  ·  {_hoverRows * _hoverColumns} 个 PMT"
            : "拖过网格选择 PMT 矩阵";
        var text = new FormattedText(
            heading,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(UiTheme.UiFont, FontStyle.Normal, FontWeight.SemiBold),
            13,
            UiTheme.TextPrimaryBrush);
        context.DrawText(text, new Point(PaddingSize, 11));
        for (var row = 1; row <= MaximumRows; row++)
        for (var column = 1; column <= MaximumColumns; column++)
        {
            var rect = CellRect(row, column);
            var active = row <= _hoverRows && column <= _hoverColumns;
            context.DrawRectangle(
                active ? UiTheme.GhostPressedBrush : UiTheme.SunkenBrush,
                new Pen(active ? UiTheme.AccentBrush : UiTheme.BorderMediumBrush, active ? 1.5 : 1),
                rect,
                2,
                2);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var selection = HitTestCell(e.GetPosition(this));
        if (selection is null || selection.Value.Rows == _hoverRows && selection.Value.Columns == _hoverColumns)
            return;
        (_hoverRows, _hoverColumns) = selection.Value;
        InvalidateVisual();
        PreviewChanged?.Invoke(this, new PmtMatrixSelectionEventArgs(_hoverRows, _hoverColumns));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverRows = _hoverColumns = 0;
        InvalidateVisual();
        PreviewCancelled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var selection = HitTestCell(e.GetPosition(this));
        if (selection is null)
            return;
        SelectionCommitted?.Invoke(
            this,
            new PmtMatrixSelectionEventArgs(selection.Value.Rows, selection.Value.Columns));
        e.Handled = true;
    }

    internal (int Rows, int Columns)? HitTestCell(Point point)
    {
        var x = point.X - PaddingSize;
        var y = point.Y - HeaderHeight - PaddingSize;
        if (x < 0 || y < 0)
            return null;
        var column = (int)(x / (CellSize + Gap)) + 1;
        var row = (int)(y / (CellSize + Gap)) + 1;
        if (row > MaximumRows || column > MaximumColumns ||
            x % (CellSize + Gap) > CellSize || y % (CellSize + Gap) > CellSize)
            return null;
        return (row, column);
    }

    private static Rect CellRect(int row, int column) => new(
        PaddingSize + (column - 1) * (CellSize + Gap),
        HeaderHeight + PaddingSize + (row - 1) * (CellSize + Gap),
        CellSize,
        CellSize);
}
