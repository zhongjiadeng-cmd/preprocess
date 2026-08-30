using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// DXF 预览界面的统一宿主：左侧图层侧栏 + 右侧工具栏 / 画布 / 状态。
///
/// 纹理界面（<see cref="GrayscaleLayerPreviewControl"/>）本身就是「侧栏选层 + 工具栏 + 画布」的
/// 三段式结构，DXF 界面过去用的是下拉框选层 + 分散的视图按钮，两套交互各说各话。
/// 这里把 DXF 侧也收敛成同一套：同一个 <see cref="DxfLayerRailCanvas"/> 侧栏、同一根
/// <see cref="CollapseHandle"/> 抽屉把手、同一排「上一层 / 下一层 / 缩放 / 适应窗口 / 滚轮 /
/// 切层保持视图」工具栏，用法完全一致。
///
/// 宿主只负责呈现与选层；<b>真正把 DXF 读进画布由宿主的调用方完成</b>——通过
/// <see cref="LoadLayer"/> 注入。这样 MainWindow 既有的错误处理与纹理叠加逻辑不用搬进来。
/// </summary>
public sealed class DxfPreviewHost : Grid
{
    /// <summary>侧栏展开 / 收起后的卡片宽度，收拢动画在这两个数值之间过渡。</summary>
    private const double RailExpandedWidth = 180;
    private const double RailCompactWidth = 44;

    /// <summary>把手骑在侧栏卡片右边框上、一半探出卡片的像素数。</summary>
    private const double HandleOverhang = 10;

    private static readonly TimeSpan PanelMotion = TimeSpan.FromMilliseconds(260);
    private static readonly Easing Motion = new CubicEaseOut();

    private static readonly GrayscalePreviewWheelMode[] WheelModeOrder =
    [
        GrayscalePreviewWheelMode.Auto,
        GrayscalePreviewWheelMode.Scroll,
        GrayscalePreviewWheelMode.Zoom
    ];

    private readonly DxfPreviewControl _preview;
    private readonly DxfLayerRailCanvas _rail = new();
    private readonly CollapseHandle _railHandle;
    private readonly ColumnDefinition _railColumn = new(GridLength.Auto);
    // 在 MakeRailColumn 里组装后回填，因此不能是 readonly。
    private Border? _railCard;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly TextBlock _zoomLabel;
    private readonly ComboBox _wheelModeBox;
    private readonly CheckBox _keepViewBox;
    private bool _motionAttached;

    private IReadOnlyList<DxfLayerPreviewItem> _items = [];
    private int _selectedIndex = -1;

    /// <param name="preview">承载 3D / 顶视图绘制的 DXF 画布。</param>
    /// <param name="status">画布下方的状态行，由调用方写入文案。</param>
    /// <param name="extraTools">
    /// 追加到标准工具栏上的 DXF 专属按钮（顶视图 / 等轴测 / 方向箭头…）。
    /// 传进来的控件会被插在「100%」之后、「滚轮」之前。
    /// </param>
    /// <param name="extraRow">工具栏与画布之间的附加控制行（纹理叠加开关等），可为 null。</param>
    public DxfPreviewHost(
        DxfPreviewControl preview,
        TextBlock status,
        IEnumerable<Control>? extraTools = null,
        Control? extraRow = null)
    {
        _preview = preview;
        Status = status;
        _railHandle = new CollapseHandle(
            CollapseHandleOrientation.Vertical,
            "收起图层列表",
            "展开图层列表");

        ColumnDefinitions = new ColumnDefinitions();
        ColumnDefinitions.Add(_railColumn);
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        _rail.LayerClicked += (_, index) => TrySelectCore(index);
        _railHandle.Toggled += (_, _) => ToggleRail();

        _prevButton = MakeButton(UiIcon.PreviousLayer, "上一层", () => TrySelectCore(_selectedIndex - 1));
        _nextButton = MakeButton(UiIcon.NextLayer, "下一层", () => TrySelectCore(_selectedIndex + 1));
        var minus = MakeButton(UiIcon.ZoomOut, "缩小", () => _preview.ZoomOut());
        var plus = MakeButton(UiIcon.ZoomIn, "放大", () => _preview.ZoomIn());
        var fit = MakeButton(UiIcon.Fit, "适应窗口", () => _preview.FitToView());
        var actual = MakeButton(UiIcon.ActualSize, "实际尺寸", () => _preview.ActualSize());
        ToolTip.SetTip(fit, "缩放到适应窗口，并回到居中位置。");
        ToolTip.SetTip(
            actual,
            "把缩放恢复成 100%（即适应窗口的基准倍率），保留当前平移位置。");

        _zoomLabel = new TextBlock
        {
            FontFamily = UiTheme.MonoFont,
            FontSize = 11,
            MinWidth = 52,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = UiTheme.TextPrimaryBrush
        };

        _wheelModeBox = new ComboBox
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
        UiTheme.ApplyInputStyle(_wheelModeBox);
        _wheelModeBox.SelectionChanged += (_, _) =>
        {
            var index = Math.Clamp(_wheelModeBox.SelectedIndex, 0, WheelModeOrder.Length - 1);
            _preview.WheelMode = WheelModeOrder[index];
        };

        _keepViewBox = new CheckBox
        {
            Content = "切层保持视图",
            IsChecked = true,
            FontSize = 11,
            Foreground = UiTheme.TextSecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(
            _keepViewBox,
            "开启后切换 DXF 层保留当前缩放、平移与视角，便于逐层对照；关闭则回到适应窗口。");

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };
        toolbar.Children.Add(_prevButton);
        toolbar.Children.Add(_nextButton);
        toolbar.Children.Add(minus);
        toolbar.Children.Add(_zoomLabel);
        toolbar.Children.Add(plus);
        toolbar.Children.Add(fit);
        toolbar.Children.Add(actual);
        if (extraTools is not null)
            foreach (var tool in extraTools)
                toolbar.Children.Add(tool);
        toolbar.Children.Add(_wheelModeBox);
        toolbar.Children.Add(_keepViewBox);

        status.FontFamily = UiTheme.UiFont;
        status.FontSize = 11;
        status.TextWrapping = TextWrapping.Wrap;

        _preview.ViewChanged += (_, _) => UpdateZoomLabel();

        var main = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                AtRow(toolbar, 0),
                AtRow(extraRow ?? new Grid(), 1),
                // 与纹理预览同一档最小高度，两个标签页切来切去时画布不会跳高。
                AtRow(CanvasCard(_preview), 2),
                AtRow(status, 3)
            }
        };

        Children.Add(Place(MakeRailColumn(), 0));
        Children.Add(Place(main, 1));

        // 过渡依赖 IGlobalClock，无头环境没有这个服务，一改带 Transitions 的属性就抛异常，
        // 因此推迟到真正挂上可视化树时再装配。宽度初值在装配前已由 UpdateRailVisibility 写好。
        AttachedToVisualTree += (_, _) => AttachMotion();

        UpdateRailVisibility();
        UpdateNavigation();
        UpdateZoomLabel();
    }

    /// <summary>底层的 DXF 画布，供宿主之外叠加纹理、切换视角等操作。</summary>
    public DxfPreviewControl Preview => _preview;

    /// <summary>画布下方的状态行。</summary>
    public TextBlock Status { get; }

    /// <summary>「切层保持视图」勾选框的当前值；调用方据此决定换层时是否重置视图。</summary>
    public bool KeepView => _keepViewBox.IsChecked == true;

    /// <summary>当前层列表。</summary>
    public IReadOnlyList<DxfLayerPreviewItem> Items => _items;

    /// <summary>当前选中层的索引，未选中为 -1。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>
    /// 选层时真正把 DXF 读进画布的回调，由调用方注入。返回 false 表示读取失败，
    /// 宿主会保持原选中项不变（避免侧栏高亮跳到一个加载失败的层上）。
    /// </summary>
    public Func<DxfLayerPreviewItem, bool>? LoadLayer { get; set; }

    /// <summary>侧栏是否处于收起态（无层可列时把手隐藏，本属性随之下发为 false）。</summary>
    public bool IsRailCollapsed =>
        _railHandle is { IsVisible: true } && _railHandle.IsCollapsed;

    /// <summary>用户点击把手切换侧栏展开 / 收起后触发，供宿主持久化。</summary>
    public event EventHandler? RailCollapsedChanged;

    /// <summary>用新的层列表刷新侧栏；原来的选中项若还在列表中则保持选中。</summary>
    public void SetItems(IReadOnlyList<DxfLayerPreviewItem> items)
    {
        var previous = _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex]
            : null;
        _items = items ?? [];
        _selectedIndex = previous is null ? -1 : IndexOf(previous);
        _rail.SetItems(_items);
        _rail.SelectedIndex = _selectedIndex;
        _rail.InvalidateVisual();
        UpdateRailVisibility();
        UpdateNavigation();
    }

    /// <summary>选中指定索引的层；索引越界或与当前选中项相同时不做任何事。</summary>
    public bool SelectIndex(int index) => TrySelectCore(index);

    /// <summary>选中指定层；不在列表中或加载失败返回 false。</summary>
    public bool SelectItem(DxfLayerPreviewItem item)
    {
        var index = IndexOf(item);
        return index >= 0 && TrySelectCore(index);
    }

    /// <summary>清除选中态（不清空列表），清空缓存时用。</summary>
    public void ClearSelection()
    {
        _selectedIndex = -1;
        _rail.SelectedIndex = -1;
        _rail.InvalidateVisual();
        UpdateNavigation();
    }

    /// <summary>
    /// 程序化设置侧栏折叠态：把手的 <see cref="CollapseHandle.SetCollapsed"/>
    /// 会同步箭头角度，状态未变时早返回，所以从持久化恢复时不会多余触发回调。
    /// </summary>
    public void SetRailCollapsed(bool collapsed)
    {
        _railHandle.SetCollapsed(collapsed);
        // SetCollapsed 会顺着 Toggled 走到 ToggleRail，但层不足两层时那里早返回了；
        // 补一次宽度同步，保证从持久化恢复出来的初值与实际折叠态一致。
        if (_railCard is not null && _items.Count > 1)
            _railCard.Width = CurrentRailWidth();
    }

    private int IndexOf(DxfLayerPreviewItem item)
    {
        for (var index = 0; index < _items.Count; index++)
            if (_items[index] == item)
                return index;
        return -1;
    }

    private bool TrySelectCore(int index)
    {
        if (index < 0 || index >= _items.Count || index == _selectedIndex)
            return false;

        var loader = LoadLayer;
        if (loader is not null && !loader(_items[index]))
            return false;

        _selectedIndex = index;
        _rail.SelectedIndex = index;
        _rail.InvalidateVisual();
        UpdateNavigation();
        UpdateZoomLabel();
        return true;
    }

    private Control MakeRailColumn()
    {
        var scroll = new ScrollViewer
        {
            Content = _rail,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // 宽度写在卡片上而不是列上：Width 是 double，能挂 DoubleTransition 做出收拢动画；
        // 列用 Auto 跟着卡片的测量结果长。ClipToBounds 让收拢时溢出的行被裁掉。
        _railCard = new Border
        {
            Padding = new Thickness(6),
            Background = UiTheme.CardBrush,
            CornerRadius = UiTheme.CardRadius,
            ClipToBounds = true,
            Width = RailExpandedWidth,
            Child = scroll
        };

        // 把手骑在卡片右边框、垂直居中：一半探出卡片，正好落在列间距里。
        _railHandle.HorizontalAlignment = HorizontalAlignment.Right;
        _railHandle.VerticalAlignment = VerticalAlignment.Center;
        _railHandle.Margin = new Thickness(0, 0, -HandleOverhang, 0);

        return new Grid
        {
            Children = { _railCard, _railHandle }
        };
    }

    private void AttachMotion()
    {
        if (_motionAttached)
            return;

        _motionAttached = true;

        if (_railCard is null || !MotionPreferences.AnimateSpatialProperties)
            return;

        _railCard.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Layoutable.WidthProperty,
                Duration = PanelMotion,
                Easing = Motion
            }
        };
    }

    private bool IsRailCollapsedState => _railHandle.IsCollapsed;

    private double CurrentRailWidth() =>
        IsRailCollapsedState ? RailCompactWidth : RailExpandedWidth;

    private void UpdateRailVisibility()
    {
        // 只有一层时侧栏没有信息量，收起来把宽度全部留给画布。
        var visible = _items.Count > 1;
        ColumnSpacing = visible ? 12 : 0;
        // 无层可列时直接隐藏：IsVisible 为 false 的控件不参与测量，Auto 列自然收到 0。
        _railCard!.IsVisible = visible;
        _railCard.Width = visible ? CurrentRailWidth() : 0;
        _rail.SetCompact(IsRailCollapsedState);
        // 把手与卡片一起显隐——无层时侧栏整体不可见，把手孤零零探在画布左边像 UI bug。
        _railHandle.IsVisible = visible;
        _railHandle.IsEnabled = visible;
    }

    private void UpdateNavigation()
    {
        var count = _items.Count;
        _prevButton.IsEnabled = count > 1 && _selectedIndex > 0;
        _nextButton.IsEnabled = count > 1 &&
            _selectedIndex >= 0 &&
            _selectedIndex < count - 1;
    }

    private void UpdateZoomLabel() =>
        _zoomLabel.Text = _preview.HasContent ? FormatZoom(_preview.Zoom) : "—";

    private static string FormatZoom(double zoom) => $"{zoom * 100:0.#}%";

    private void ToggleRail()
    {
        if (_railCard is null || _items.Count <= 1)
            return;

        // 把手已经自己翻好箭头与状态，这里只跟进宽度；卡片的 Width 变动由过渡动画接管。
        _railCard.Width = CurrentRailWidth();
        _rail.SetCompact(IsRailCollapsedState);
        RailCollapsedChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Border CanvasCard(Control content)
    {
        var card = UiTheme.CanvasCard(content);
        card.MinHeight = 320;
        return card;
    }

    private static Button MakeButton(UiIcon icon, string actionName, Action action)
    {
        var button = new Button { Content = UiIcons.Create(icon) };
        UiTheme.ApplyIconStyle(button, actionName);
        ToolTip.SetTip(button, actionName);
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
