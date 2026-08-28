using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// 可折叠的日志面板：顶部居中的抽屉把手 + 标题行 + 控制台风格日志框。
/// 折叠后日志区高度动画收拢到 0，只把最近一条日志淡入到标题右侧，
/// 面板随之收成一行，把底部空间让给预览区。
/// 宿主可订阅 <see cref="CollapsedChanged"/> 做联动（例如持久化折叠状态）。
/// </summary>
internal sealed class LogPanelView
{
    /// <summary>24×24 viewbox 的实心人字箭头（朝下 = 收起），笔画约 2px。</summary>
    private const string ChevronDownGeometry =
        "M12 16.5 L19.5 9 L18 7.5 L12 13.5 L6 7.5 L4.5 9 Z";

    private static readonly TimeSpan PanelMotion = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan IconMotion = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan FadeMotion = TimeSpan.FromMilliseconds(150);
    private static readonly Easing Motion = new CubicEaseOut();

    private readonly TextBox _log;
    private readonly Border _logArea;
    private readonly Border _card;
    private readonly Grid _layout;
    private readonly TextBlock _summary;
    private readonly Button _handle;
    private readonly RotateTransform _chevronRotation;
    private bool _collapsed;
    private bool _motionAttached;

    public LogPanelView(TextBox log, string title)
    {
        _log = log;
        _layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6
        };

        _chevronRotation = new RotateTransform { Angle = 0 };

        var chevron = new PathIcon
        {
            Width = 15,
            Height = 15,
            RenderTransform = _chevronRotation,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };

        // 抽屉把手：水平居中、骑在卡片上边框上，折叠/展开时箭头旋转 180°。
        _handle = new Button
        {
            Content = chevron,
            Width = 56,
            Height = 20,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -10, 0, 0),
            ZIndex = 1
        };
        _handle.Classes.Add("panel-handle");
        UiTheme.AttachButtonTransitions(_handle);
        // 光标与路径几何都依赖平台服务，等真正挂到可视化树上再设置，便于无头环境单测。
        _handle.AttachedToVisualTree += (_, _) =>
        {
            _handle.Cursor = new Cursor(StandardCursorType.Hand);
            chevron.Data = StreamGeometry.Parse(ChevronDownGeometry);
        };
        _handle.Click += (_, _) => SetCollapsed(!_collapsed);

        _summary = new TextBlock
        {
            FontFamily = UiTheme.MonoFont,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 0, 4, 0),
            IsHitTestVisible = false,
            Opacity = 0
        };
        _summary.AttachedToVisualTree += (_, _) =>
            _summary.Cursor = new Cursor(StandardCursorType.Hand);
        _summary.PointerReleased += (_, _) => SetCollapsed(false);

        var clearButton = new Button { Content = "清空" };
        UiTheme.ApplyGhostStyle(clearButton, small: true);
        clearButton.Click += (_, _) => log.Clear();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6,
            Children =
            {
                Place(UiTheme.PanelLabel(title), 0),
                Place(_summary, 1),
                Place(clearButton, 2)
            }
        };

        // 高度必须从具体数值过渡到 0 才有动画（NaN → 0 无动画），
        // 所以展开高度在这里写成固定值，所在行用 Auto 跟着它长。
        // ClipToBounds 让收拢过程中溢出的日志文字被裁掉，形成"卷起来"的观感。
        _logArea = new Border
        {
            ClipToBounds = true,
            Child = log,
            Height = UiTheme.LogAreaExpandedHeight,
            Opacity = 1
        };

        Grid.SetRow(_logArea, 1);
        _layout.Children.Add(header);
        _layout.Children.Add(_logArea);

        _card = new Border
        {
            Padding = new Thickness(14, 10, 14, 12),
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = UiTheme.CardRadius,
            Background = UiTheme.PanelBrush,
            Child = _layout
        };

        Root = new Grid
        {
            Children = { _card, _handle }
        };
        Root.AttachedToVisualTree += (_, _) => AttachMotion();

        log.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                RefreshSummary();
        };
        ApplyTooltip();
        RefreshSummary();
    }

    /// <summary>面板根控件，直接放进宿主布局。</summary>
    public Control Root { get; }

    /// <summary>是否处于折叠状态（只显示最新一条日志）。</summary>
    public bool IsCollapsed => _collapsed;

    /// <summary>折叠时显示的那一行文字（日志的最后一条非空行）。</summary>
    public string SummaryText => _summary.Text ?? string.Empty;

    /// <summary>日志区当前的目标高度：展开为 <see cref="UiTheme.LogAreaExpandedHeight"/>，折叠为 0。</summary>
    public double LogAreaHeight => _logArea.Height;

    /// <summary>日志区当前的目标不透明度，用于验证淡入淡出。</summary>
    public double LogAreaOpacity => _logArea.Opacity;

    /// <summary>把手箭头的目标旋转角：展开 0°（朝下）、折叠 180°（朝上）。</summary>
    public double ChevronAngle => _chevronRotation.Angle;

    /// <summary>最新一条日志文字的目标不透明度。</summary>
    public double SummaryOpacity => _summary.Opacity;

    /// <summary>折叠后日志区不再响应指针事件。</summary>
    public bool LogAreaHitTestVisible => _logArea.IsHitTestVisible;

    /// <summary>把手当前提示文案。</summary>
    public string HandleTooltip => ToolTip.GetTip(_handle)?.ToString() ?? string.Empty;

    /// <summary>折叠状态变化时触发。</summary>
    public event EventHandler? CollapsedChanged;

    public void SetCollapsed(bool value)
    {
        if (_collapsed == value)
            return;

        _collapsed = value;

        _logArea.Height = value ? 0 : UiTheme.LogAreaExpandedHeight;
        _logArea.Opacity = value ? 0 : 1;
        _logArea.IsHitTestVisible = !value;
        _log.IsTabStop = !value;
        _layout.RowSpacing = value ? 0 : 6;
        _summary.Opacity = value ? 1 : 0;
        _summary.IsHitTestVisible = value;
        _chevronRotation.Angle = value ? 180 : 0;
        _card.Background = value ? UiTheme.BarBrush : UiTheme.PanelBrush;
        _card.BorderBrush = value ? UiTheme.BorderMediumBrush : UiTheme.BorderSubtleBrush;
        ApplyTooltip();

        if (value)
            RefreshSummary();

        CollapsedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 装配所有过渡动画。过渡依赖 IGlobalClock，无头单测环境没有这个服务，
    /// 一旦某个带 Transitions 的属性变化就会抛异常；因此推迟到真正挂上可视化树时再装。
    /// 各属性的初值都在装配前写好，所以首帧不会有从 NaN / 旧值起步的抖动。
    /// </summary>
    private void AttachMotion()
    {
        if (_motionAttached)
            return;

        _motionAttached = true;

        _chevronRotation.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = RotateTransform.AngleProperty,
                Duration = IconMotion,
                Easing = Motion
            }
        };

        _logArea.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Layoutable.HeightProperty,
                Duration = PanelMotion,
                Easing = Motion
            },
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = FadeMotion,
                Easing = Motion
            }
        };

        _summary.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeMotion }
        };

        _card.Transitions = new Transitions
        {
            new BrushTransition { Property = Border.BackgroundProperty, Duration = PanelMotion },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = PanelMotion }
        };
    }

    private void ApplyTooltip() =>
        ToolTip.SetTip(_handle, _collapsed ? "上拉展开完整日志" : "下缩，只显示最新一条日志");

    private void RefreshSummary()
    {
        var latest = LastMeaningfulLine(_log.Text);
        _summary.Text = latest ?? "暂无日志";
        _summary.Foreground = latest is null
            ? UiTheme.TextFaintBrush
            : UiTheme.TextSecondaryBrush;
        ToolTip.SetTip(_summary, latest is null ? null : $"{latest}\n\n点击展开完整日志");
    }

    private static string? LastMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n');
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.Length > 0)
                return line;
        }

        return null;
    }

    private static T Place<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
