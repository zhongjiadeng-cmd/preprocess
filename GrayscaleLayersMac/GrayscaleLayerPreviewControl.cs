using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GrayscaleLayersMac;

public sealed class GrayscaleLayerPreviewControl : Grid, IDisposable
{
    private readonly Func<string, CancellationToken, Task<TextureImageInspection>> _loadPreview;
    private readonly GrayscaleLayerThumbnailCanvas _thumbnails = new();
    private readonly ColumnDefinition _thumbnailColumn = new(new GridLength(180, GridUnitType.Pixel));
    private readonly Button _collapseButton = new() { Content = "‹", Width = 34, Height = 28 };
    private readonly Image _mainImage = new() { Stretch = Stretch.None };
    private readonly ScrollViewer _mainScroll = new();
    private readonly TextBlock _status = new() { Foreground = UiTheme.TextSecondaryBrush };
    private GrayscaleLayerPreviewController _controller = new();
    private Bitmap? _mainBitmap;
    private double _zoom = 1;

    public GrayscaleLayerPreviewControl(
        Func<string, CancellationToken, Task<TextureImageInspection>> loadPreview)
    {
        _loadPreview = loadPreview ?? throw new ArgumentNullException(nameof(loadPreview));
        ColumnDefinitions = new ColumnDefinitions { _thumbnailColumn, new ColumnDefinition(GridLength.Star) };
        ColumnSpacing = 12;

        _thumbnails.LayerClicked += (_, index) => TrySelect(index);
        UiTheme.ApplyGhostStyle(_collapseButton, small: true);
        _collapseButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        ToolTip.SetTip(_collapseButton, "折叠图层缩略图");
        _collapseButton.Click += (_, _) => ToggleThumbnailPanel();
        var thumbnailScroll = new ScrollViewer
        {
            Content = _thumbnails,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _mainScroll.Content = _mainImage;
        _mainScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _mainScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _mainScroll.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            try
            {
                SetZoom(_zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1));
                e.Handled = true;
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);

        var previous = MakeButton("上一层", () => TrySelect(_thumbnails.SelectedIndex - 1));
        var next = MakeButton("下一层", () => TrySelect(_thumbnails.SelectedIndex + 1));
        var fit = MakeButton("适应窗口", Fit);
        var minus = MakeButton("−", () => SetZoom(_zoom / 1.25));
        var plus = MakeButton("+", () => SetZoom(_zoom * 1.25));
        var actual = MakeButton("100%", () => SetZoom(1));
        _status.FontFamily = UiTheme.MonoFont;
        _status.FontSize = 11;

        Children.Add(Place(new Border
        {
            Padding = new Thickness(6),
            Background = UiTheme.CardBrush,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 6,
                Children =
                {
                    AtRow(_collapseButton, 0),
                    AtRow(thumbnailScroll, 1)
                }
            }
        }, 0));
        Children.Add(Place(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                AtRow(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { previous, next, fit, minus, plus, actual } }, 0),
                AtRow(UiTheme.CanvasCard(_mainScroll), 1),
                AtRow(_status, 2)
            }
        }, 1));
    }

    public async Task LoadAsync(string directory, CancellationToken cancellationToken)
    {
        var candidate = new GrayscaleLayerPreviewController();
        candidate.Refresh(directory);
        try
        {
            foreach (var item in candidate.Items)
            {
                try
                {
                    var inspection = await _loadPreview(item.FilePath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = new MemoryStream(inspection.PreviewPng, writable: false);
                    var thumbnail = Bitmap.DecodeToWidth(stream, 120, BitmapInterpolationMode.MediumQuality);
                    item.SetPreview(inspection.PreviewPng, inspection.Info.PixelWidth, inspection.Info.PixelHeight, thumbnail);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    item.SetError(error.Message);
                }
            }
        }
        catch
        {
            candidate.Dispose();
            throw;
        }

        ClearMainImage();
        _controller.Dispose();
        _controller = candidate;
        _thumbnails.SetItems(_controller.Items);
        _thumbnails.SetCompact(_thumbnailColumn.Width.Value <= 60);
        TrySelect(_controller.Items.Count > 0 ? 0 : -1);
    }

    public void Dispose()
    {
        ClearMainImage();
        _controller.Dispose();
    }

    private void TrySelect(int index)
    {
        try
        {
            if (index < 0 || !_controller.Select(index))
                return;
            var item = _controller.SelectedItem!;
            if (item.PreviewPng is null)
            {
                ShowError(new InvalidOperationException(item.Error ?? "该层没有可用预览。"));
                return;
            }

            using var stream = new MemoryStream(item.PreviewPng, writable: false);
            var candidate = new Bitmap(stream);
            if (candidate.PixelSize.Width != item.PixelWidth || candidate.PixelSize.Height != item.PixelHeight)
            {
                candidate.Dispose();
                throw new InvalidOperationException("分层预览像素尺寸与源 TIFF 不一致。");
            }

            var previous = _mainBitmap;
            _mainBitmap = candidate;
            _mainImage.Source = candidate;
            _zoom = 1;
            _thumbnails.SelectedIndex = index;
            _thumbnails.InvalidateVisual();
            ApplyZoom();
            _status.ClearValue(TextBlock.ForegroundProperty);
            _status.Text = $"{item.DisplayName} · {item.PixelWidth} × {item.PixelHeight} · 缩放 {_zoom:P0}";
            if (previous is not null)
                Dispatcher.UIThread.Post(previous.Dispose, DispatcherPriority.Background);
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void Fit()
    {
        if (_mainBitmap is null || _mainScroll.Bounds.Width <= 0 || _mainScroll.Bounds.Height <= 0)
            return;
        SetZoom(Math.Min(_mainScroll.Bounds.Width / _mainBitmap.PixelSize.Width, _mainScroll.Bounds.Height / _mainBitmap.PixelSize.Height));
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.05, 16);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (_mainBitmap is null)
            return;
        _mainImage.Width = _mainBitmap.PixelSize.Width * _zoom;
        _mainImage.Height = _mainBitmap.PixelSize.Height * _zoom;
        _status.Text = $"{_controller.SelectedItem?.DisplayName} · {_mainBitmap.PixelSize.Width} × {_mainBitmap.PixelSize.Height} · 缩放 {_zoom:P0}";
    }

    private void ClearMainImage()
    {
        _mainImage.Source = null;
        var previous = _mainBitmap;
        _mainBitmap = null;
        if (previous is not null)
            Dispatcher.UIThread.Post(previous.Dispose, DispatcherPriority.Background);
    }

    private void ShowError(Exception error)
    {
        _status.Text = $"预览错误：{error.Message}";
        _status.Foreground = Brushes.OrangeRed;
    }

    private void ToggleThumbnailPanel()
    {
        var compact = _thumbnailColumn.Width.Value > 60;
        _thumbnailColumn.Width = new GridLength(compact ? 44 : 180);
        _thumbnails.SetCompact(compact);
        _collapseButton.Content = compact ? "›" : "‹";
        ToolTip.SetTip(_collapseButton, compact ? "展开图层缩略图" : "折叠图层缩略图");
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text };
        UiTheme.ApplyGhostStyle(button, small: true);
        button.Click += (_, _) => action();
        return button;
    }

    private static T Place<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
