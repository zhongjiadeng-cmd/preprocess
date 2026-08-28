using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace GrayscaleLayersMac;

/// <summary>
/// 集中管理应用的视觉设计令牌、全局交互样式与常用控件工厂。
/// 配色采用"蓝调石墨"深色系：近黑窗口 + 逐级抬升的表面 + 单一蓝色强调色。
/// 注意：按钮的背景/前景/边框一律走类名样式（CreateGlobalStyles），
/// 不要直接设置本地值——本地值优先级高于伪类样式，会堵死悬停反馈。
/// </summary>
internal static class UiTheme
{
    private static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(140);

    // ---- 背景层次（由深到浅：窗口 → 头部 → 面板 → 卡片 → 下沉控制台）----
    public static readonly Color RootColor = Color.FromRgb(10, 12, 16);
    public static readonly Color HeaderColor = Color.FromRgb(15, 18, 24);
    public static readonly Color PanelColor = Color.FromRgb(18, 21, 28);
    public static readonly Color CardColor = Color.FromRgb(24, 28, 37);
    public static readonly Color BarColor = Color.FromRgb(21, 25, 32);
    public static readonly Color SunkenColor = Color.FromRgb(7, 9, 13);

    // ---- 文字层级 ----
    public static readonly Color TextPrimaryColor = Color.FromRgb(237, 240, 245);
    public static readonly Color TextSecondaryColor = Color.FromRgb(154, 163, 178);
    public static readonly Color TextFaintColor = Color.FromRgb(94, 103, 116);

    // ---- 强调色（蓝）----
    public static readonly Color AccentColor = Color.FromRgb(0, 120, 212);
    public static readonly Color AccentHoverColor = Color.FromRgb(45, 145, 235);
    public static readonly Color AccentPressedColor = Color.FromRgb(0, 95, 170);
    public static readonly Color AccentTextColor = Color.FromRgb(255, 255, 255);

    // ---- 危险色（取消等破坏性操作）----
    public static readonly Color DangerColor = Color.FromRgb(229, 83, 75);

    // ---- 边框 ----
    public static readonly Color BorderSubtleColor = Color.FromArgb(20, 255, 255, 255);
    public static readonly Color BorderMediumColor = Color.FromArgb(45, 255, 255, 255);
    public static readonly Color BorderStrongColor = Color.FromArgb(70, 255, 255, 255);

    // ---- 圆角 ----
    public static readonly CornerRadius CardRadius = new(12);
    public static readonly CornerRadius ControlRadius = new(8);

    // ---- 画刷 ----
    public static readonly IBrush RootBrush = new SolidColorBrush(RootColor);
    public static readonly IBrush HeaderBrush = new SolidColorBrush(HeaderColor);
    public static readonly IBrush PanelBrush = new SolidColorBrush(PanelColor);
    public static readonly IBrush CardBrush = new SolidColorBrush(CardColor);
    public static readonly IBrush BarBrush = new SolidColorBrush(BarColor);
    public static readonly IBrush SunkenBrush = new SolidColorBrush(SunkenColor);
    public static readonly IBrush AccentBrush = new SolidColorBrush(AccentColor);
    public static readonly IBrush AccentHoverBrush = new SolidColorBrush(AccentHoverColor);
    public static readonly IBrush AccentPressedBrush = new SolidColorBrush(AccentPressedColor);
    public static readonly IBrush AccentTextBrush = new SolidColorBrush(AccentTextColor);
    public static readonly IBrush TextPrimaryBrush = new SolidColorBrush(TextPrimaryColor);
    public static readonly IBrush TextSecondaryBrush = new SolidColorBrush(TextSecondaryColor);
    public static readonly IBrush TextFaintBrush = new SolidColorBrush(TextFaintColor);
    public static readonly IBrush BorderSubtleBrush = new SolidColorBrush(BorderSubtleColor);
    public static readonly IBrush BorderMediumBrush = new SolidColorBrush(BorderMediumColor);
    public static readonly IBrush BorderStrongBrush = new SolidColorBrush(BorderStrongColor);

    public static readonly FontFamily MonoFont = FontFamily.Parse("Menlo, monospace");

    /// <summary>
    /// 窗口级全局交互样式：按钮按类名（accent / btn-ghost / danger）提供
    /// 悬停与按压状态。窗口样式优先级高于 Fluent 主题样式，可确定性覆盖。
    /// </summary>
    public static Styles CreateGlobalStyles()
    {
        var styles = new Styles();

        // ---- 主操作按钮（琥珀强调）----
        var primary = new Style(x => x.OfType<Button>().Class("accent"));
        primary.Setters.Add(new Setter(Button.BackgroundProperty, AccentBrush));
        styles.Add(primary);

        var primaryHover = new Style(x => x.OfType<Button>().Class("accent").Class(":pointerover"));
        primaryHover.Setters.Add(new Setter(Button.BackgroundProperty, AccentHoverBrush));
        styles.Add(primaryHover);

        var primaryPressed = new Style(x => x.OfType<Button>().Class("accent").Class(":pressed"));
        primaryPressed.Setters.Add(new Setter(Button.BackgroundProperty, AccentPressedBrush));
        styles.Add(primaryPressed);

        // ---- 幽灵按钮（细描边、透明底）----
        var ghost = new Style(x => x.OfType<Button>().Class("btn-ghost"));
        ghost.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(8, 255, 255, 255))));
        ghost.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        ghost.Setters.Add(new Setter(Button.BorderBrushProperty, BorderMediumBrush));
        styles.Add(ghost);

        var ghostHover = new Style(x => x.OfType<Button>().Class("btn-ghost").Class(":pointerover"));
        ghostHover.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(22, 255, 255, 255))));
        ghostHover.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        ghostHover.Setters.Add(new Setter(Button.BorderBrushProperty, BorderStrongBrush));
        styles.Add(ghostHover);

        var ghostPressed = new Style(x => x.OfType<Button>().Class("btn-ghost").Class(":pressed"));
        ghostPressed.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(34, 255, 255, 255))));
        styles.Add(ghostPressed);

        // ---- 危险变体（取消按钮：悬停泛红）----
        var danger = new Style(x => x.OfType<Button>().Class("btn-ghost").Class("danger"));
        danger.Setters.Add(new Setter(Button.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(255, 155, 147))));
        styles.Add(danger);

        var dangerHover = new Style(
            x => x.OfType<Button>().Class("btn-ghost").Class("danger").Class(":pointerover"));
        dangerHover.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(38, 229, 83, 75))));
        dangerHover.Setters.Add(new Setter(Button.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(100, 229, 83, 75))));
        dangerHover.Setters.Add(new Setter(Button.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(255, 173, 166))));
        styles.Add(dangerHover);

        // ---- 日志面板的抽屉把手（居中悬浮的小箭头胶囊）----
        var handle = new Style(x => x.OfType<Button>().Class("panel-handle"));
        handle.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(31, 36, 47))));
        handle.Setters.Add(new Setter(Button.BorderBrushProperty, BorderMediumBrush));
        handle.Setters.Add(new Setter(Button.ForegroundProperty, TextSecondaryBrush));
        styles.Add(handle);

        var handleHover = new Style(
            x => x.OfType<Button>().Class("panel-handle").Class(":pointerover"));
        handleHover.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(44, 51, 66))));
        handleHover.Setters.Add(new Setter(Button.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(110, 0, 120, 212))));
        handleHover.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        styles.Add(handleHover);

        var handlePressed = new Style(
            x => x.OfType<Button>().Class("panel-handle").Class(":pressed"));
        handlePressed.Setters.Add(new Setter(Button.BackgroundProperty, AccentPressedBrush));
        handlePressed.Setters.Add(new Setter(Button.BorderBrushProperty, AccentPressedBrush));
        handlePressed.Setters.Add(new Setter(Button.ForegroundProperty, AccentTextBrush));
        styles.Add(handlePressed);

        return styles;
    }

    /// <summary>
    /// 覆盖 Fluent 按钮状态资源（PointerOver/Pressed/Accent 系列），
    /// 与类名样式形成双保险：无论主题用控件级样式还是模板级资源，悬停反馈都可控。
    /// </summary>
    public static void ApplyFluentResourceOverrides(Window window)
    {
        window.Resources["ButtonBackgroundPointerOver"] =
            new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
        window.Resources["ButtonBackgroundPressed"] =
            new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
        window.Resources["ButtonForegroundPointerOver"] = TextPrimaryBrush;
        window.Resources["ButtonForegroundPressed"] = TextPrimaryBrush;
        window.Resources["ButtonBorderBrushPointerOver"] = BorderStrongBrush;
        window.Resources["ButtonBorderBrushPressed"] = BorderStrongBrush;
        window.Resources["AccentButtonBackground"] = AccentBrush;
        window.Resources["AccentButtonBackgroundPointerOver"] = AccentHoverBrush;
        window.Resources["AccentButtonBackgroundPressed"] = AccentPressedBrush;
        window.Resources["AccentButtonForeground"] = AccentTextBrush;
        window.Resources["AccentButtonForegroundPointerOver"] = AccentTextBrush;
        window.Resources["AccentButtonForegroundPressed"] = AccentTextBrush;
    }

    /// <summary>页面大标题（检查器顶部）。</summary>
    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 20,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.2,
        Foreground = TextPrimaryBrush
    };

    /// <summary>页面副标题说明文字。</summary>
    public static TextBlock PageSubtitle(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = TextSecondaryBrush
    };

    /// <summary>小面板标题（日志、预览等区块标题，带字距的微型标签风）。</summary>
    public static TextBlock PanelLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 1,
        Foreground = TextSecondaryBrush,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>表单字段标签（次要层级的小字）。</summary>
    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Foreground = TextSecondaryBrush
    };

    /// <summary>主操作按钮：走 Fluent accent 类 + 类名样式，悬停/按压自带琥珀反馈。</summary>
    public static void ApplyPrimaryStyle(Button button)
    {
        button.Classes.Add("accent");
        button.Height = 44;
        button.FontSize = 15;
        button.FontWeight = FontWeight.SemiBold;
        button.CornerRadius = ControlRadius;
        button.Foreground = AccentTextBrush;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        AttachButtonTransitions(button);
    }

    /// <summary>次级按钮：幽灵样式（细描边、悬停提亮）。small 用于工具栏与日志面板。</summary>
    public static void ApplyGhostStyle(Button button, bool small = false)
    {
        if (!button.Classes.Contains("btn-ghost"))
            button.Classes.Add("btn-ghost");
        button.MinHeight = small ? 26 : 34;
        button.FontSize = small ? 11.5 : 13;
        button.FontWeight = small ? FontWeight.Medium : FontWeight.Regular;
        button.Padding = small ? new Thickness(10, 2, 10, 2) : new Thickness(16, 6, 16, 6);
        button.CornerRadius = small ? new CornerRadius(6) : ControlRadius;
        AttachButtonTransitions(button);
    }

    /// <summary>把幽灵按钮标记为危险操作（悬停泛红），用于"取消"。</summary>
    public static void MarkDanger(Button button)
    {
        if (!button.Classes.Contains("danger"))
            button.Classes.Add("danger");
    }

    /// <summary>按钮状态色的过渡动画；把手等自定义按钮也复用这一套。</summary>
    internal static void AttachButtonTransitions(Button button)
    {
        button.Transitions = new Transitions
        {
            new BrushTransition { Property = Button.BackgroundProperty, Duration = HoverDuration },
            new BrushTransition { Property = Button.BorderBrushProperty, Duration = HoverDuration },
            new BrushTransition { Property = Button.ForegroundProperty, Duration = HoverDuration }
        };
    }

    /// <summary>主进度条（强调色前景、圆角轨道）。</summary>
    public static ProgressBar CreateProgress() => new()
    {
        IsIndeterminate = false,
        Height = 6,
        CornerRadius = new CornerRadius(3),
        Foreground = AccentBrush
    };

    /// <summary>标题行左侧的强调色小色条，用于预览等区块的视觉锚点。</summary>
    public static Border AccentBar(double width = 3, double height = 14) => new()
    {
        Width = width,
        Height = height,
        CornerRadius = new CornerRadius(1.5),
        Background = AccentBrush,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>头部右侧的小徽章（胶囊描边）。</summary>
    public static Border Badge(string text) => new()
    {
        Padding = new Thickness(10, 4),
        CornerRadius = new CornerRadius(999),
        BorderBrush = BorderMediumBrush,
        BorderThickness = new Thickness(1),
        Background = Brushes.Transparent,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = TextSecondaryBrush
        }
    };

    /// <summary>
    /// 日志面板展开时日志区的固定高度（与日志框 MinHeight 一致，整卡约 224px）。
    /// 必须写死成具体数值（而非 Auto/NaN），这样折叠时 Height → 0
    /// 才能走 DoubleTransition 产生连续的收拢动画。
    /// </summary>
    public const double LogAreaExpandedHeight = 170;

    /// <summary>深色控制台风格的只读日志框。</summary>
    public static TextBox CreateLogBox(double minHeight = 170) => new()
    {
        AcceptsReturn = true,
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = minHeight,
        FontFamily = MonoFont,
        FontSize = 12,
        Foreground = TextSecondaryBrush,
        Background = SunkenBrush,
        BorderBrush = BorderSubtleBrush,
        CornerRadius = ControlRadius,
        Padding = new Thickness(12, 10)
    };

    /// <summary>
    /// 日志面板卡片：顶部居中的抽屉把手 + 标题 + 最新一条日志（折叠时） +
    /// 清空按钮 + 控制台风格日志框。折叠后日志区高度动画收拢为 0，
    /// 面板所在行需设为 Auto 才能跟着一起收。
    /// </summary>
    public static LogPanelView LogPanel(TextBox log, string title) => new(log, title);

    /// <summary>把可折叠分组包成圆角卡片（浮起表面 + 细描边）。</summary>
    public static Control CardExpander(string title, Control content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        var expander = new Expander
        {
            Header = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 0.3,
                Foreground = TextPrimaryBrush
            },
            IsExpanded = true,
            Background = Brushes.Transparent,
            Padding = new Thickness(16, 12, 16, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content
        };
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = CardBrush,
            BorderBrush = BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = CardRadius,
            ClipToBounds = true,
            Child = expander
        };
    }

    /// <summary>双栏工作区的原生分隔条；必须直接放进需要调整的 Grid。</summary>
    public static GridSplitter WorkspaceSplitter()
    {
        var control = new GridSplitter
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = false,
            DragIncrement = 1,
            KeyboardIncrement = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var isDragging = false;
        control.PointerEntered += (_, _) => control.Background = AccentBrush;
        control.PointerExited += (_, _) =>
        {
            if (!isDragging)
                control.Background = Brushes.Transparent;
        };
        control.DragStarted += (_, _) =>
        {
            isDragging = true;
            control.Background = AccentBrush;
        };
        control.DragCompleted += (_, _) =>
        {
            isDragging = false;
            control.Background = control.IsPointerOver ? AccentBrush : Brushes.Transparent;
        };

        return control;
    }

    /// <summary>预览画布外框（圆角卡片容器，承载 DXF 预览控件）。</summary>
    public static Border CanvasCard(Control child) => new()
    {
        BorderBrush = BorderSubtleBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = CardRadius,
        Background = SunkenBrush,
        ClipToBounds = true,
        Child = child
    };
}
