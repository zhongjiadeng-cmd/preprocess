using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GrayscaleLayersMac;

/// <summary>
/// 从纹理导入交给预览控件的负载。
///
/// 之所以传递 PNG 字节而不是 <see cref="Bitmap"/>：源位图的生命周期归
/// <c>TexturePreviewController</c>，而控件需要一份自己能长期持有、切走再切回时
/// 能重新解码的副本。PNG 字节正好是第 0 层和各分层共用的存储形式。
/// </summary>
public sealed class TexturePreviewPayload : IDisposable
{
    public TexturePreviewPayload(byte[] previewPng, int pixelWidth, int pixelHeight)
    {
        PreviewPng = previewPng ?? throw new ArgumentNullException(nameof(previewPng));
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public byte[] PreviewPng { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    // 字节数组无需释放；实现 IDisposable 只是为了满足控制器的统一生命周期契约。
    public void Dispose()
    {
    }
}

/// <summary>
/// 纹理界面（唯一的预览界面）。
///
/// 纹理与分层本质是同一张图的不同视图，所以不再分成两个标签页：这里始终维护一条
/// 图层序列——<b>第 0 层是导入的源纹理，1..N 是灰度分层结果</b>。用户在同一块画布上
/// 用上下层切换对照，缩放／平移／滚轮语义对纹理和分层完全一致。
///
/// 画布交互（<see cref="GrayscaleLayerPreviewCanvas"/>）与视图数学
/// （<see cref="GrayscalePreviewViewMath"/>）不区分层的来源，只认当前位图。
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

    /// <summary>缩略图列展开 / 收起后的卡片宽度，收拢动画在这两个数值之间过渡。</summary>
    private const double ThumbnailExpandedWidth = 180;
    private const double ThumbnailCompactWidth = 44;

    /// <summary>把手骑在缩略图卡片右边框上、一半探出卡片的像素数。</summary>
    private const double HandleOverhang = 10;

    private static readonly TimeSpan PanelMotion = TimeSpan.FromMilliseconds(260);
    private static readonly Easing Motion = new CubicEaseOut();

    private readonly Func<string, CancellationToken, Task<TextureImageInspection>>? _loadPreview;
    private readonly bool _supportsLayers;

    private readonly GrayscaleLayerPreviewCanvas _canvas = new();
    private readonly GrayscaleLayerThumbnailCanvas? _thumbnails;
    private readonly ColumnDefinition? _thumbnailColumn;
    private readonly CollapseHandle? _collapseHandle;
    // 在 MakeThumbnailColumn 里组装后回填，因此不能是 readonly。
    private Border? _thumbnailCard;
    private readonly Button? _prevButton;
    private readonly Button? _nextButton;
    private readonly CheckBox? _keepViewBox;
    private bool _motionAttached;

    /// <summary>缩略图侧栏的折叠态始终由把手的 IsCollapsed 持有，不再保留独立的镜像字段，
    /// 避免持久化恢复时"两个状态不同步"的隐患。</summary>
    private bool IsThumbnailsCollapsedState => _collapseHandle?.IsCollapsed ?? false;

    private readonly TextBlock _zoomLabel = new()
    {
        FontFamily = UiTheme.MonoFont,
        FontSize = 11,
        MinWidth = 52,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Foreground = UiTheme.TextPrimaryBrush
    };

    /// <summary>纹理信息（尺寸、位深、DPI…），由 <see cref="SetMetadata"/> 写入。</summary>
    private readonly TextBlock _metadata = new()
    {
        FontFamily = UiTheme.MonoFont,
        FontSize = 11,
        Text = "尚未选择图片",
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextSecondaryBrush
    };

    /// <summary>物理尺寸，由 <see cref="SetPhysicalSize"/> 写入。</summary>
    private readonly TextBlock _physicalSize = new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextSecondaryBrush
    };

    /// <summary>当前层、缩放与交互提示。</summary>
    private readonly TextBlock _layerStatus = new()
    {
        FontFamily = UiTheme.MonoFont,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextSecondaryBrush
    };

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

    private GrayscaleLayerPreviewController _controller;

    /// <summary>只预览源纹理（不做灰度分层的页面）。</summary>
    public GrayscaleLayerPreviewControl()
        : this(loadPreview: null)
    {
    }

    /// <summary>
    /// 可加载灰度分层的纹理界面。<paramref name="loadPreview"/> 用于异步读取单层 TIFF 的预览图。
    /// </summary>
    public GrayscaleLayerPreviewControl(
        Func<string, CancellationToken, Task<TextureImageInspection>>? loadPreview)
    {
        _loadPreview = loadPreview;
        _supportsLayers = loadPreview is not null;
        _controller = new GrayscaleLayerPreviewController(reserveSourceSlot: _supportsLayers);

        if (_supportsLayers)
        {
            _thumbnails = new GrayscaleLayerThumbnailCanvas();
            // 列宽交给 Auto：真正被动画的是卡片自身的 Width，列只是跟着测量结果长，
            // GridLength 本身没有过渡动画可用。
            _thumbnailColumn = new ColumnDefinition(GridLength.Auto);
            _collapseHandle = new CollapseHandle(
                CollapseHandleOrientation.Vertical,
                "收起图层缩略图",
                "展开图层缩略图");
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
        if (_supportsLayers)
        {
            ColumnDefinitions.Add(_thumbnailColumn!);
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ColumnSpacing = 12;
        }
        else
        {
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        _canvas.ViewChanged += (_, _) => UpdateStatus();

        ToolTip.SetTip(_wheelModeBox, InteractionHint);
        _wheelModeBox.SelectionChanged += (_, _) =>
        {
            var index = Math.Clamp(_wheelModeBox.SelectedIndex, 0, WheelModeOrder.Length - 1);
            _canvas.WheelMode = WheelModeOrder[index];
        };

        if (_supportsLayers)
        {
            _thumbnails!.LayerClicked += (_, index) => TrySelect(index);
            _collapseHandle!.Toggled += (_, _) => ToggleThumbnailPanel();
            _prevButton = MakeButton("上一层", () => TrySelect(_controller.SelectedIndex - 1));
            _nextButton = MakeButton("下一层", () => TrySelect(_controller.SelectedIndex + 1));
            ToolTip.SetTip(_keepViewBox!, "开启后切换图层保留当前缩放与位置，便于逐层对照；关闭则回到 100% 居中。");
        }

        var minus = MakeButton("−", () => _canvas.ZoomOut());
        var plus = MakeButton("+", () => _canvas.ZoomIn());
        var fit = MakeButton("适应窗口", () => _canvas.Fit());
        var actual = MakeButton("100%", () => _canvas.ActualSize());

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };
        toolbar.Children.AddRange(
            BuildToolbarChildren(_prevButton, _nextButton, minus, plus, fit, actual));

        var canvasCard = UiTheme.CanvasCard(_canvas);
        canvasCard.MinHeight = 320;

        var statusStack = new StackPanel
        {
            Spacing = 2,
            Children = { _metadata, _physicalSize, _layerStatus }
        };

        var mainColumn = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                AtRow(toolbar, 0),
                AtRow(canvasCard, 1),
                AtRow(statusStack, 2)
            }
        };

        if (_supportsLayers)
        {
            Children.Add(Place(MakeThumbnailColumn(), 0));
            Children.Add(Place(mainColumn, 1));
        }
        else
        {
            Children.Add(mainColumn);
        }

        AttachedToVisualTree += (_, _) => AttachMotion();

        SyncItems();
        UpdateZoomLabel();
    }

    /// <summary>
    /// 装配缩略图卡片宽度的收拢过渡。过渡依赖 IGlobalClock，无头环境没有这个服务，
    /// 一旦带 Transitions 的属性变化就会抛异常，因此推迟到真正挂上可视化树时再装。
    /// 宽度初值在装配前已通过 <see cref="UpdateThumbnailVisibility"/> 写好。
    /// </summary>
    private void AttachMotion()
    {
        if (_motionAttached)
            return;

        _motionAttached = true;

        if (_thumbnailCard is null)
            return;

        _thumbnailCard.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Layoutable.WidthProperty,
                Duration = PanelMotion,
                Easing = Motion
            }
        };
    }

    /// <summary>
    /// 设置第 0 层源纹理。传 <paramref name="payload"/> 为 null 表示纹理被清除
    /// （重新导入、读取失败或窗口关闭）。
    /// </summary>
    public void SetSourceTexture(TexturePreviewPayload? payload)
    {
        if (payload is null)
        {
            _controller.SetSource(null);
            SyncItems();
            return;
        }

        GrayscaleLayerPreviewItem item;
        try
        {
            using var stream = new MemoryStream(payload.PreviewPng, writable: false);
            var thumbnail = Bitmap.DecodeToWidth(stream, 120, BitmapInterpolationMode.MediumQuality);
            item = GrayscaleLayerPreviewItem.ForSourceTexture(null);
            item.SetPreview(payload.PreviewPng, payload.PixelWidth, payload.PixelHeight, thumbnail);
        }
        catch (Exception error)
        {
            _controller.SetSource(null);
            SyncItems();
            ShowError(error);
            return;
        }

        _controller.SetSource(item);
        SyncItems();
    }

    /// <summary>设置纹理信息（尺寸、位深、DPI 等）。</summary>
    public void SetMetadata(string text, bool isError = false)
    {
        _metadata.Text = text;
        _metadata.Foreground = isError ? Brushes.OrangeRed : UiTheme.TextSecondaryBrush;
    }

    /// <summary>设置物理尺寸文本。</summary>
    public void SetPhysicalSize(string text) => _physicalSize.Text = text;

    /// <summary>
    /// 读取一个目录里的灰度分层 TIFF，作为第 1..N 层接到源纹理之后。
    /// 第 0 层（源纹理）保持不变，所以纹理与分层始终在同一条序列里。
    /// </summary>
    public async Task LoadLayersAsync(string directory, CancellationToken cancellationToken)
    {
        if (!_supportsLayers || _loadPreview is null)
            return;

        var items = _controller.Refresh(directory);
        try
        {
            foreach (var item in items)
            {
                if (item.IsSourceTexture)
                    continue;   // 第 0 层的预览已经在内存里，不用再去读文件

                try
                {
                    var inspection = await _loadPreview(item.FilePath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = new MemoryStream(inspection.PreviewPng, writable: false);
                    var thumbnail = Bitmap.DecodeToWidth(stream, 120, BitmapInterpolationMode.MediumQuality);
                    item.SetPreview(
                        inspection.PreviewPng,
                        inspection.Info.PixelWidth,
                        inspection.Info.PixelHeight,
                        thumbnail);
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
        finally
        {
            SyncItems();
        }
    }

    public void Dispose()
    {
        ClearMainImage();
        _controller.Dispose();
        _canvas.Dispose();
    }

    /// <summary>缩略图侧栏是否处于收起态（不分层时把手被隐藏，本属性随之下发为 false）。</summary>
    public bool IsThumbnailsCollapsed =>
        _collapseHandle is { IsVisible: true } && _collapseHandle.IsCollapsed;

    /// <summary>用户点击把手切换缩略图侧栏的展开 / 收起后触发，供宿主持久化。</summary>
    public event EventHandler? ThumbnailsCollapsedChanged;

    /// <summary>
    /// 程序化设置缩略图侧栏的折叠态：把手的 <see cref="CollapseHandle.SetCollapsed"/>
    /// 会同步箭头角度，状态未变时早返回，所以从持久化恢复时不会多余触发回调。
    /// </summary>
    public void SetThumbnailsCollapsed(bool collapsed)
    {
        if (_collapseHandle is null)
            return;
        _collapseHandle.SetCollapsed(collapsed);
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

    private Control MakeThumbnailColumn()
    {
        var thumbnailScroll = new ScrollViewer
        {
            Content = _thumbnails,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // 宽度写在卡片上而不是列上：Width 是 double，能挂 DoubleTransition 做出收拢动画；
        // 列用 Auto 跟着卡片的测量结果长。ClipToBounds 让收拢时溢出的缩略图被裁掉。
        _thumbnailCard = new Border
        {
            Padding = new Thickness(6),
            Background = UiTheme.CardBrush,
            CornerRadius = UiTheme.CardRadius,
            ClipToBounds = true,
            Width = ThumbnailExpandedWidth,
            Child = thumbnailScroll
        };

        // 把手骑在卡片右边框、垂直居中：一半探出卡片，正好落在列间距里，
        // 箭头朝左表示"收起"、朝右表示"展开"，与水平收拢方向一致。
        _collapseHandle!.HorizontalAlignment = HorizontalAlignment.Right;
        _collapseHandle.VerticalAlignment = VerticalAlignment.Center;
        _collapseHandle.Margin = new Thickness(0, 0, -HandleOverhang, 0);

        return new Grid
        {
            Children = { _thumbnailCard, _collapseHandle }
        };
    }

    /// <summary>把控制器的当前状态同步到缩略图、导航按钮、状态栏与主画布。</summary>
    private void SyncItems()
    {
        var items = _controller.Items;
        _thumbnails?.SetItems(items);
        UpdateThumbnailVisibility();
        UpdateNavigation(items.Count);
        RenderSelected();
        UpdateStatus();
    }

    private void RenderSelected()
    {
        var index = _controller.SelectedIndex;
        if (index < 0)
        {
            ClearMainImage();
            return;
        }

        var item = _controller.SelectedItem!;
        if (item.PreviewPng is null)
        {
            // 第 0 层的占位（还没导入纹理）不算错误，给一句安静的提示。
            if (item.IsSourceTexture)
            {
                ClearMainImage();
                return;
            }

            ShowError(new InvalidOperationException(item.Error ?? "该层没有可用预览。"));
            return;
        }

        try
        {
            using var stream = new MemoryStream(item.PreviewPng, writable: false);
            var candidate = new Bitmap(stream);
            if (candidate.PixelSize.Width != item.PixelWidth ||
                candidate.PixelSize.Height != item.PixelHeight)
            {
                candidate.Dispose();
                throw new InvalidOperationException("预览像素尺寸与源图不一致。");
            }

            _layerStatus.ClearValue(TextBlock.ForegroundProperty);
            _canvas.SetImage(candidate, keepView: _keepViewBox?.IsChecked == true);
            if (_thumbnails is not null)
            {
                _thumbnails.SelectedIndex = index;
                _thumbnails.InvalidateVisual();
            }
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void TrySelect(int index)
    {
        if (!_controller.Select(index))
            return;
        RenderSelected();
        UpdateStatus();
    }

    private void UpdateThumbnailVisibility()
    {
        if (_thumbnailColumn is null)
            return;

        // 只有源纹理（还没有分层）时缩略图列表没有信息量，收起来把宽度留给画布。
        var visible = _controller.Items.Count > 1;
        ColumnSpacing = visible ? 12 : 0;
        // 无分层时直接隐藏：IsVisible 为 false 的控件不参与测量，Auto 列自然收到 0。
        _thumbnailCard!.IsVisible = visible;
        _thumbnailCard.Width = visible ? CurrentThumbnailWidth() : 0;
        _thumbnails!.SetCompact(IsThumbnailsCollapsedState);
        if (_collapseHandle is not null)
        {
            // 把手与卡片一起显隐——无分层时缩略图区域整体不可见，把手孤零零地
            // 探在主画布左边反而像 UI bug。
            _collapseHandle.IsVisible = visible;
            _collapseHandle.IsEnabled = visible;
        }
    }

    private double CurrentThumbnailWidth() =>
        IsThumbnailsCollapsedState ? ThumbnailCompactWidth : ThumbnailExpandedWidth;

    private void UpdateNavigation(int itemCount)
    {
        if (_prevButton is null || _nextButton is null)
            return;
        _prevButton.IsEnabled = itemCount > 1 && _controller.SelectedIndex > 0;
        _nextButton.IsEnabled = itemCount > 1 &&
            _controller.SelectedIndex >= 0 &&
            _controller.SelectedIndex < itemCount - 1;
    }

    private void UpdateStatus()
    {
        var item = _controller.SelectedItem;
        if (item is null)
        {
            _layerStatus.Text = _supportsLayers
                ? "尚未导入纹理图 · " + InteractionHint
                : InteractionHint;
        }
        else if (item.PreviewPng is null && item.IsSourceTexture)
        {
            _layerStatus.Text = "第 00 层 · 尚未导入纹理图 · " + InteractionHint;
        }
        else
        {
            var name = item.IsSourceTexture
                ? "第 00 层 · 源纹理"
                : $"{item.DisplayName}";
            _layerStatus.Text = $"{name} · {item.PixelWidth} × {item.PixelHeight} · " +
                $"缩放 {FormatZoom(_canvas.Zoom)} · {InteractionHint}";
        }

        UpdateNavigation(_controller.Items.Count);
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
        if (_thumbnails is not null)
        {
            _thumbnails.SelectedIndex = _controller.SelectedIndex;
            _thumbnails.InvalidateVisual();
        }
    }

    private void ShowError(Exception error)
    {
        _layerStatus.Text = $"预览错误：{error.Message}";
        _layerStatus.Foreground = Brushes.OrangeRed;
    }

    private void ToggleThumbnailPanel()
    {
        if (_collapseHandle is null || _thumbnailCard is null)
            return;
        if (_controller.Items.Count <= 1)
            return;

        // 把手已经自己翻好箭头与状态，这里只跟进宽度；卡片的 Width 变动由过渡动画接管。
        _thumbnailCard.Width = CurrentThumbnailWidth();
        _thumbnails!.SetCompact(IsThumbnailsCollapsedState);
        ThumbnailsCollapsedChanged?.Invoke(this, EventArgs.Empty);
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
