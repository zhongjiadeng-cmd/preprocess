using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GrayscaleLayersMac;

/// <summary>
/// 纹理 / 分层的统一预览控件。
///
/// 纹理和分层在概念上都是"灰度图的一种视图"：纹理是输入的整图，分层是按阈值
/// 切出的若干图。本控件把两者的交互统一到同一份画布与工具栏上——缩放／平移／
/// 滚轮语义／像素网格／双击切换适配——仅左侧缩略图面板按场景开关：
///
/// <list type="bullet">
///   <item><description>
///     <b>单图模式</b>（参数默认或显式 <see cref="GrayscaleLayerPreviewControl()"/>）：
///     纹理预览用。无缩略图、无上下层，每次只显示导入的源纹理。
///     Bitmap 由调用方（如 <c>TexturePreviewController</c>）负责生命周期，
///     控件不释放。
///   </description></item>
///   <item><description>
///     <b>分层模式</b>（<see cref="GrayscaleLayerPreviewControl(Func{string, CancellationToken, Task{TextureImageInspection}})"/>）：
///     灰度分层预览用。带缩略图、上下层切换、"切层保持视图"等分层专属能力。
///     控件内部缓存并按需释放每层的 Bitmap。
///   </description></item>
/// </list>
/// </summary>
public sealed class GrayscaleLayerPreviewControl : Grid, IDisposable
{
    private static readonly GrayscalePreviewWheelMode[] WheelModeOrder =
    [
        GrayscalePreviewWheelMode.Auto,
        GrayscalePreviewWheelMode.Scroll,
        GrayscalePreviewWheelMode.Zoom
    ];

    private const string InteractionHint =
        "滚轮滚动 · ⌘/Ctrl+滚轮缩放 · 拖动或空格+拖动平移 · 双击适应窗口/100%";

    private readonly Func<string, CancellationToken, Task<TextureImageInspection>>? _loadPreview;
    private readonly bool _layerMode;

    private readonly GrayscaleLayerPreviewCanvas _canvas = new();
    private readonly GrayscaleLayerThumbnailCanvas? _thumbnails;
    private readonly ColumnDefinition? _thumbnailColumn;
    private readonly Button? _collapseButton;
    private readonly TextBlock _zoomLabel = new()
    {
        FontFamily = UiTheme.MonoFont,
        FontSize = 11,
        MinWidth = 52,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Foreground = UiTheme.TextPrimaryBrush
    };
    private readonly TextBlock _status = new()
    {
        FontFamily = UiTheme.MonoFont,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextSecondaryBrush
    };
    private readonly TextBlock _secondaryStatus = new()
    {
        FontSize = 11,
        Foreground = UiTheme.TextSecondaryBrush
    };
    private readonly CheckBox? _keepViewBox;
    private readonly ComboBox _wheelModeBox = new()
    {
        Width = 148,
        FontSize = 11,
        SelectedIndex = 0,
        VerticalAlignment = VerticalAlignment.Center,
        ItemsSource = new[]
        {
            "滚轮：自动（滚不动时缩放）",
            "滚轮：始终滚动",
            "滚轮：始终缩放"
        }
    };
    private GrayscaleLayerPreviewController _controller = new();

    /// <summary>单图模式（纹理预览）。</summary>
    public GrayscaleLayerPreviewControl()
        : this(loadPreview: null)
    {
    }

    /// <summary>
    /// 分层模式（灰度分层预览）。<paramref name="loadPreview"/> 用于异步读取单层 TIFF 的预览图，
    /// 传 null 即回到单图模式。
    /// </summary>
    public GrayscaleLayerPreviewControl(
        Func<string, CancellationToken, Task<TextureImageInspection>>? loadPreview)
    {
        _loadPreview = loadPreview;
        _layerMode = loadPreview is not null;

        if (_layerMode)
        {
            _thumbnails = new GrayscaleLayerThumbnailCanvas();
            _thumbnailColumn = new ColumnDefinition(new GridLength(180, GridUnitType.Pixel));
            _collapseButton = new Button { Content = "‹", Width = 34, Height = 28 };
            _keepViewBox = new CheckBox
            {
                Content = "切层保持视图",
                IsChecked = true,
                FontSize = 11,
                Foreground = UiTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        ColumnDefinitions = new ColumnDefinitions();
        if (_thumbnailColumn is not null)
        {
            ColumnDefinitions.Add(_thumbnailColumn);
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ColumnSpacing = 12;
        }
        else
        {
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        Control? thumbColumnControl = null;
        if (_layerMode)
        {
            _thumbnails!.LayerClicked += (_, index) => TrySelect(index);
            UiTheme.ApplyGhostStyle(_collapseButton!, small: true);
            _collapseButton!.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(_collapseButton, "折叠图层缩略图");
            _collapseButton.Click += (_, _) => ToggleThumbnailPanel();
            var thumbnailScroll = new ScrollViewer
            {
                Content = _thumbnails,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            thumbColumnControl = new Border
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
            };
        }

        _canvas.ViewChanged += (_, _) => UpdateStatus();

        ToolTip.SetTip(_wheelModeBox, InteractionHint);
        _wheelModeBox.SelectionChanged += (_, _) =>
        {
            var index = Math.Clamp(_wheelModeBox.SelectedIndex, 0, WheelModeOrder.Length - 1);
            _canvas.WheelMode = WheelModeOrder[index];
        };

        Button? prev = null, next = null;
        if (_layerMode)
        {
            prev = MakeButton("上一层", () => TrySelect(_thumbnails!.SelectedIndex - 1));
            next = MakeButton("下一层", () => TrySelect(_thumbnails!.SelectedIndex + 1));
            ToolTip.SetTip(_keepViewBox!, "开启后切换图层保留当前缩放与位置，便于逐层对照；关闭则回到 100% 居中。");
        }

        var fit = MakeButton("适应窗口", () => _canvas.Fit());
        var minus = MakeButton("−", () => _canvas.ZoomOut());
        var plus = MakeButton("+", () => _canvas.ZoomIn());
        var actual = MakeButton("100%", () => _canvas.ActualSize());

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };
        toolbar.Children.AddRange(BuildToolbarChildren(prev, next, minus, plus, fit, actual));

        var canvasCard = UiTheme.CanvasCard(_canvas);
        canvasCard.MinHeight = 320;

        var statusStack = new StackPanel
        {
            Spacing = 2,
            Children = { _status, _secondaryStatus }
        };

        if (_layerMode && thumbColumnControl is not null)
        {
            Children.Add(Place(thumbColumnControl, 0));
            Children.Add(Place(new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 8,
                Children =
                {
                    AtRow(toolbar, 0),
                    AtRow(canvasCard, 1),
                    AtRow(statusStack, 2)
                }
            }, 1));
        }
        else
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto");
            RowSpacing = 8;
            Children.Add(AtRow(toolbar, 0));
            Children.Add(AtRow(canvasCard, 1));
            Children.Add(AtRow(statusStack, 2));
        }

        UpdateZoomLabel();
        UpdateStatus();
    }

    /// <summary>
    /// 单图模式下显示图片。Bitmap 由调用方负责生命周期（典型场景是
    /// <c>TexturePreviewController</c>），控件不释放。
    /// </summary>
    public void SetImage(Bitmap? bitmap)
    {
        if (_layerMode)
            throw new InvalidOperationException("单图 API 仅在单图模式可用，分层模式请用 LoadAsync。");

        // ownsBitmap: false —— controller 拥有生命周期，控件只渲染。
        _canvas.SetImage(bitmap, ownsBitmap: false);
        UpdateStatus();
    }

    /// <summary>设置单图模式下的元数据 / 物理尺寸文本。</summary>
    public void SetMetadata(string text, bool isError = false)
    {
        _status.Text = text;
        _status.Foreground = isError ? Brushes.OrangeRed : UiTheme.TextSecondaryBrush;
    }

    public void SetPhysicalSize(string text) => _secondaryStatus.Text = text;

    /// <summary>分层模式加载一个目录里所有 TIFF 层。</summary>
    public async Task LoadAsync(string directory, CancellationToken cancellationToken)
    {
        if (!_layerMode || _loadPreview is null)
            throw new InvalidOperationException("LoadAsync 仅在分层模式（传入 loadPreview 时）可用。");

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
        _thumbnails!.SetItems(_controller.Items);
        _thumbnails.SetCompact(_thumbnailColumn!.Width.Value <= 60);
        TrySelect(_controller.Items.Count > 0 ? 0 : -1);
    }

    public void Dispose()
    {
        ClearMainImage();
        _controller.Dispose();
        _canvas.Dispose();
    }

    private IEnumerable<Control> BuildToolbarChildren(
        Button? prev, Button? next,
        Button minus, Button plus, Button fit, Button actual)
    {
        if (prev is not null) yield return prev;
        if (next is not null) yield return next;
        yield return minus;
        yield return _zoomLabel;
        yield return plus;
        yield return fit;
        yield return actual;
        yield return _wheelModeBox;
        if (_keepViewBox is not null) yield return _keepViewBox;
    }

    private void TrySelect(int index)
    {
        if (!_layerMode)
            return;
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

            _canvas.SetImage(candidate, keepView: _keepViewBox!.IsChecked == true);
            _thumbnails!.SelectedIndex = index;
            _thumbnails.InvalidateVisual();
            _status.ClearValue(TextBlock.ForegroundProperty);
            UpdateStatus();
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void UpdateStatus()
    {
        if (_layerMode)
        {
            var item = _controller.SelectedItem;
            var text = item is null
                ? "尚未选择图层"
                : $"{item.DisplayName} · {item.PixelWidth} × {item.PixelHeight}";
            _status.Text = $"{text} · 缩放 {FormatZoom(_canvas.Zoom)} · {InteractionHint}";
            _secondaryStatus.Text = string.Empty;
        }
        else
        {
            // 单图模式：_status / _secondaryStatus 由 SetMetadata / SetPhysicalSize 写入，
            // 不要在每次视图状态变化时把它们冲掉，否则外部控件的元数据会被吞掉。
            // 缩放读数仍然由工具栏左侧的 _zoomLabel 显示。
        }

        UpdateZoomLabel();
    }

    private void UpdateZoomLabel()
    {
        _zoomLabel.Text = _canvas.HasImage ? FormatZoom(_canvas.Zoom) : "—";
    }

    private static string FormatZoom(double zoom) => $"{zoom * 100:0.#}%";

    private void ClearMainImage()
    {
        _canvas.SetImage(null);
        UpdateStatus();
    }

    private void ShowError(Exception error)
    {
        _status.Text = $"预览错误：{error.Message}";
        _status.Foreground = Brushes.OrangeRed;
    }

    private void ToggleThumbnailPanel()
    {
        if (_thumbnailColumn is null || _collapseButton is null)
            return;
        var compact = _thumbnailColumn.Width.Value > 60;
        _thumbnailColumn.Width = new GridLength(compact ? 44 : 180);
        _thumbnails!.SetCompact(compact);
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
