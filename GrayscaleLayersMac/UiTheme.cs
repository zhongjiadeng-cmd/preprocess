using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace GrayscaleLayersMac;

/// <summary>
/// 集中管理应用的视觉设计令牌、全局交互样式与常用控件工厂。
/// 浅色与深色都通过同一组可变语义画刷表达；组件不得持有主题相关的独立 RGB 值。
/// 注意：按钮的背景/前景/边框一律走类名样式（CreateGlobalStyles），
/// 不要直接设置本地值——本地值优先级高于伪类样式，会堵死悬停反馈。
/// </summary>
internal static class UiTheme
{
    private static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(140);

    private sealed record Palette(
        Color Root,
        Color Header,
        Color Panel,
        Color Card,
        Color Bar,
        Color Sunken,
        Color TextPrimary,
        Color TextSecondary,
        Color TextFaint,
        Color Accent,
        Color AccentHover,
        Color AccentPressed,
        Color AccentText,
        Color Danger,
        Color DangerText,
        Color BorderSubtle,
        Color BorderMedium,
        Color BorderStrong,
        Color Ghost,
        Color GhostHover,
        Color GhostPressed,
        Color Handle,
        Color HandleHover,
        Color Selection);

    private static readonly Palette DarkPalette = new(
        Root: Color.FromRgb(12, 14, 18),
        Header: Color.FromRgb(18, 21, 27),
        Panel: Color.FromRgb(22, 25, 32),
        Card: Color.FromRgb(28, 32, 40),
        Bar: Color.FromRgb(25, 29, 36),
        Sunken: Color.FromRgb(8, 10, 14),
        TextPrimary: Color.FromRgb(242, 244, 248),
        TextSecondary: Color.FromRgb(177, 185, 198),
        TextFaint: Color.FromRgb(121, 131, 147),
        Accent: Color.FromRgb(10, 111, 209),
        AccentHover: Color.FromRgb(42, 134, 224),
        AccentPressed: Color.FromRgb(0, 86, 170),
        AccentText: Colors.White,
        Danger: Color.FromRgb(232, 88, 82),
        DangerText: Color.FromRgb(255, 165, 159),
        BorderSubtle: Color.FromArgb(24, 255, 255, 255),
        BorderMedium: Color.FromArgb(48, 255, 255, 255),
        BorderStrong: Color.FromArgb(78, 255, 255, 255),
        Ghost: Color.FromArgb(9, 255, 255, 255),
        GhostHover: Color.FromArgb(24, 255, 255, 255),
        GhostPressed: Color.FromArgb(38, 255, 255, 255),
        Handle: Color.FromRgb(35, 40, 50),
        HandleHover: Color.FromRgb(47, 54, 67),
        Selection: Color.FromArgb(48, 10, 111, 209));

    private static readonly Palette LightPalette = new(
        Root: Color.FromRgb(241, 239, 235),
        Header: Color.FromRgb(249, 248, 245),
        Panel: Color.FromRgb(238, 241, 245),
        Card: Color.FromRgb(253, 253, 252),
        Bar: Color.FromRgb(247, 248, 250),
        Sunken: Color.FromRgb(231, 235, 240),
        TextPrimary: Color.FromRgb(28, 32, 39),
        TextSecondary: Color.FromRgb(75, 84, 98),
        TextFaint: Color.FromRgb(108, 118, 132),
        Accent: Color.FromRgb(0, 101, 204),
        AccentHover: Color.FromRgb(0, 119, 230),
        AccentPressed: Color.FromRgb(0, 78, 164),
        AccentText: Colors.White,
        Danger: Color.FromRgb(190, 45, 42),
        DangerText: Color.FromRgb(166, 35, 34),
        BorderSubtle: Color.FromArgb(28, 35, 45, 60),
        BorderMedium: Color.FromArgb(52, 35, 45, 60),
        BorderStrong: Color.FromArgb(82, 35, 45, 60),
        Ghost: Color.FromArgb(8, 35, 45, 60),
        GhostHover: Color.FromArgb(17, 35, 45, 60),
        GhostPressed: Color.FromArgb(28, 35, 45, 60),
        Handle: Color.FromRgb(247, 248, 250),
        HandleHover: Color.FromRgb(232, 237, 243),
        Selection: Color.FromArgb(32, 0, 101, 204));

    public static AppColorScheme CurrentScheme { get; private set; } = AppColorScheme.Dark;

    public static Color RootColor { get; private set; } = DarkPalette.Root;
    public static Color HeaderColor { get; private set; } = DarkPalette.Header;
    public static Color PanelColor { get; private set; } = DarkPalette.Panel;
    public static Color CardColor { get; private set; } = DarkPalette.Card;
    public static Color BarColor { get; private set; } = DarkPalette.Bar;
    public static Color SunkenColor { get; private set; } = DarkPalette.Sunken;
    public static Color TextPrimaryColor { get; private set; } = DarkPalette.TextPrimary;
    public static Color TextSecondaryColor { get; private set; } = DarkPalette.TextSecondary;
    public static Color TextFaintColor { get; private set; } = DarkPalette.TextFaint;
    public static Color AccentColor { get; private set; } = DarkPalette.Accent;
    public static Color AccentHoverColor { get; private set; } = DarkPalette.AccentHover;
    public static Color AccentPressedColor { get; private set; } = DarkPalette.AccentPressed;
    public static Color AccentTextColor { get; private set; } = DarkPalette.AccentText;
    public static Color DangerColor { get; private set; } = DarkPalette.Danger;
    public static Color BorderSubtleColor { get; private set; } = DarkPalette.BorderSubtle;
    public static Color BorderMediumColor { get; private set; } = DarkPalette.BorderMedium;
    public static Color BorderStrongColor { get; private set; } = DarkPalette.BorderStrong;

    // ---- 圆角 ----
    public static readonly CornerRadius CardRadius = new(12);
    public static readonly CornerRadius ControlRadius = new(8);

    // ---- 画刷 ----
    public static readonly SolidColorBrush RootBrush = new(RootColor);
    public static readonly SolidColorBrush HeaderBrush = new(HeaderColor);
    public static readonly SolidColorBrush PanelBrush = new(PanelColor);
    public static readonly SolidColorBrush CardBrush = new(CardColor);
    public static readonly SolidColorBrush BarBrush = new(BarColor);
    public static readonly SolidColorBrush SunkenBrush = new(SunkenColor);
    public static readonly SolidColorBrush AccentBrush = new(AccentColor);
    public static readonly SolidColorBrush AccentHoverBrush = new(AccentHoverColor);
    public static readonly SolidColorBrush AccentPressedBrush = new(AccentPressedColor);
    public static readonly SolidColorBrush AccentTextBrush = new(AccentTextColor);
    public static readonly SolidColorBrush TextPrimaryBrush = new(TextPrimaryColor);
    public static readonly SolidColorBrush TextSecondaryBrush = new(TextSecondaryColor);
    public static readonly SolidColorBrush TextFaintBrush = new(TextFaintColor);
    public static readonly SolidColorBrush BorderSubtleBrush = new(BorderSubtleColor);
    public static readonly SolidColorBrush BorderMediumBrush = new(BorderMediumColor);
    public static readonly SolidColorBrush BorderStrongBrush = new(BorderStrongColor);
    public static readonly SolidColorBrush GhostBrush = new(DarkPalette.Ghost);
    public static readonly SolidColorBrush GhostHoverBrush = new(DarkPalette.GhostHover);
    public static readonly SolidColorBrush GhostPressedBrush = new(DarkPalette.GhostPressed);
    public static readonly SolidColorBrush HandleBrush = new(DarkPalette.Handle);
    public static readonly SolidColorBrush HandleHoverBrush = new(DarkPalette.HandleHover);
    public static readonly SolidColorBrush DangerBrush = new(DarkPalette.Danger);
    public static readonly SolidColorBrush DangerTextBrush = new(DarkPalette.DangerText);
    public static readonly SolidColorBrush SelectionBrush = new(DarkPalette.Selection);

    public static readonly FontFamily MonoFont = FontFamily.Parse("Menlo, monospace");

    public static event EventHandler? SchemeChanged;

    public static void ApplyScheme(AppColorScheme scheme)
    {
        var palette = scheme == AppColorScheme.Light ? LightPalette : DarkPalette;
        CurrentScheme = scheme;

        RootColor = palette.Root;
        HeaderColor = palette.Header;
        PanelColor = palette.Panel;
        CardColor = palette.Card;
        BarColor = palette.Bar;
        SunkenColor = palette.Sunken;
        TextPrimaryColor = palette.TextPrimary;
        TextSecondaryColor = palette.TextSecondary;
        TextFaintColor = palette.TextFaint;
        AccentColor = palette.Accent;
        AccentHoverColor = palette.AccentHover;
        AccentPressedColor = palette.AccentPressed;
        AccentTextColor = palette.AccentText;
        DangerColor = palette.Danger;
        BorderSubtleColor = palette.BorderSubtle;
        BorderMediumColor = palette.BorderMedium;
        BorderStrongColor = palette.BorderStrong;

        RootBrush.Color = palette.Root;
        HeaderBrush.Color = palette.Header;
        PanelBrush.Color = palette.Panel;
        CardBrush.Color = palette.Card;
        BarBrush.Color = palette.Bar;
        SunkenBrush.Color = palette.Sunken;
        TextPrimaryBrush.Color = palette.TextPrimary;
        TextSecondaryBrush.Color = palette.TextSecondary;
        TextFaintBrush.Color = palette.TextFaint;
        AccentBrush.Color = palette.Accent;
        AccentHoverBrush.Color = palette.AccentHover;
        AccentPressedBrush.Color = palette.AccentPressed;
        AccentTextBrush.Color = palette.AccentText;
        BorderSubtleBrush.Color = palette.BorderSubtle;
        BorderMediumBrush.Color = palette.BorderMedium;
        BorderStrongBrush.Color = palette.BorderStrong;
        GhostBrush.Color = palette.Ghost;
        GhostHoverBrush.Color = palette.GhostHover;
        GhostPressedBrush.Color = palette.GhostPressed;
        HandleBrush.Color = palette.Handle;
        HandleHoverBrush.Color = palette.HandleHover;
        DangerBrush.Color = palette.Danger;
        DangerTextBrush.Color = palette.DangerText;
        SelectionBrush.Color = palette.Selection;

        SchemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 窗口级全局交互样式：按钮按类名（accent / btn-ghost / danger）提供
    /// 悬停与按压状态。窗口样式优先级高于 Fluent 主题样式，可确定性覆盖。
    /// </summary>
    public static Styles CreateGlobalStyles()
    {
        var styles = new Styles();

        // ---- 主操作按钮（统一蓝色强调）----
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
        ghost.Setters.Add(new Setter(Button.BackgroundProperty, GhostBrush));
        ghost.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        ghost.Setters.Add(new Setter(Button.BorderBrushProperty, BorderMediumBrush));
        styles.Add(ghost);

        var ghostHover = new Style(x => x.OfType<Button>().Class("btn-ghost").Class(":pointerover"));
        ghostHover.Setters.Add(new Setter(Button.BackgroundProperty, GhostHoverBrush));
        ghostHover.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        ghostHover.Setters.Add(new Setter(Button.BorderBrushProperty, BorderStrongBrush));
        styles.Add(ghostHover);

        var ghostPressed = new Style(x => x.OfType<Button>().Class("btn-ghost").Class(":pressed"));
        ghostPressed.Setters.Add(new Setter(Button.BackgroundProperty, GhostPressedBrush));
        styles.Add(ghostPressed);

        // ---- 危险变体（取消按钮：悬停泛红）----
        var danger = new Style(x => x.OfType<Button>().Class("btn-ghost").Class("danger"));
        danger.Setters.Add(new Setter(Button.ForegroundProperty, DangerTextBrush));
        styles.Add(danger);

        var dangerHover = new Style(
            x => x.OfType<Button>().Class("btn-ghost").Class("danger").Class(":pointerover"));
        dangerHover.Setters.Add(new Setter(Button.BackgroundProperty, GhostPressedBrush));
        dangerHover.Setters.Add(new Setter(Button.BorderBrushProperty, DangerBrush));
        dangerHover.Setters.Add(new Setter(Button.ForegroundProperty, DangerTextBrush));
        styles.Add(dangerHover);

        // ---- 日志面板的抽屉把手（居中悬浮的小箭头胶囊）----
        var handle = new Style(x => x.OfType<Button>().Class("panel-handle"));
        handle.Setters.Add(new Setter(Button.BackgroundProperty, HandleBrush));
        handle.Setters.Add(new Setter(Button.BorderBrushProperty, BorderMediumBrush));
        handle.Setters.Add(new Setter(Button.ForegroundProperty, TextSecondaryBrush));
        styles.Add(handle);

        var handleHover = new Style(
            x => x.OfType<Button>().Class("panel-handle").Class(":pointerover"));
        handleHover.Setters.Add(new Setter(Button.BackgroundProperty, HandleHoverBrush));
        handleHover.Setters.Add(new Setter(Button.BorderBrushProperty, AccentBrush));
        handleHover.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        styles.Add(handleHover);

        var handlePressed = new Style(
            x => x.OfType<Button>().Class("panel-handle").Class(":pressed"));
        handlePressed.Setters.Add(new Setter(Button.BackgroundProperty, AccentPressedBrush));
        handlePressed.Setters.Add(new Setter(Button.BorderBrushProperty, AccentPressedBrush));
        handlePressed.Setters.Add(new Setter(Button.ForegroundProperty, AccentTextBrush));
        styles.Add(handlePressed);

        // ---- 纹理 / DXF 分段选择器：颜色、描边和字重共同表达选中状态 ----
        var previewTab = new Style(x => x.OfType<ToggleButton>().Class("preview-tab"));
        previewTab.Setters.Add(new Setter(Button.BackgroundProperty, GhostBrush));
        previewTab.Setters.Add(new Setter(Button.ForegroundProperty, TextSecondaryBrush));
        previewTab.Setters.Add(new Setter(Button.BorderBrushProperty, BorderMediumBrush));
        previewTab.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        previewTab.Setters.Add(new Setter(Button.CornerRadiusProperty, ControlRadius));
        previewTab.Setters.Add(new Setter(Button.MinHeightProperty, 34d));
        previewTab.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(14, 5)));
        styles.Add(previewTab);

        var previewTabHover = new Style(
            x => x.OfType<ToggleButton>().Class("preview-tab").Class(":pointerover"));
        previewTabHover.Setters.Add(new Setter(Button.BackgroundProperty, GhostHoverBrush));
        previewTabHover.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        styles.Add(previewTabHover);

        var previewTabChecked = new Style(
            x => x.OfType<ToggleButton>().Class("preview-tab").Class(":checked"));
        previewTabChecked.Setters.Add(new Setter(Button.BackgroundProperty, SelectionBrush));
        previewTabChecked.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        previewTabChecked.Setters.Add(new Setter(Button.BorderBrushProperty, AccentBrush));
        previewTabChecked.Setters.Add(new Setter(Button.FontWeightProperty, FontWeight.SemiBold));
        styles.Add(previewTabChecked);

        return styles;
    }

    /// <summary>
    /// 覆盖 Fluent 按钮状态资源（PointerOver/Pressed/Accent 系列），
    /// 与类名样式形成双保险：无论主题用控件级样式还是模板级资源，悬停反馈都可控。
    /// </summary>
    public static void ApplyFluentResourceOverrides(Window window)
    {
        window.Resources["ButtonBackgroundPointerOver"] = GhostHoverBrush;
        window.Resources["ButtonBackgroundPressed"] = GhostPressedBrush;
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

    /// <summary>主操作按钮：走 Fluent accent 类 + 类名样式，悬停/按压自带蓝色反馈。</summary>
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

    /// <summary>
    /// 主操作分割按钮：左侧执行默认操作，右侧窄区打开附加操作。
    /// 通过 SplitButton 自身的动态资源统一原生模板中两个按钮的交互色。
    /// </summary>
    public static void ApplyPrimaryStyle(SplitButton button)
    {
        button.Classes.Add("accent");
        button.Height = 44;
        button.MinWidth = 150;
        button.FontSize = 15;
        button.FontWeight = FontWeight.SemiBold;
        button.CornerRadius = ControlRadius;
        button.Foreground = AccentTextBrush;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;

        button.Resources["SplitButtonBackground"] = AccentBrush;
        button.Resources["SplitButtonBackgroundPointerOver"] = AccentHoverBrush;
        button.Resources["SplitButtonBackgroundPressed"] = AccentPressedBrush;
        button.Resources["SplitButtonBackgroundDisabled"] = SelectionBrush;
        button.Resources["SplitButtonForeground"] = AccentTextBrush;
        button.Resources["SplitButtonForegroundPointerOver"] = AccentTextBrush;
        button.Resources["SplitButtonForegroundPressed"] = AccentTextBrush;
        button.Resources["SplitButtonForegroundDisabled"] = TextFaintBrush;
        button.Resources["SplitButtonBorderBrush"] = BorderMediumBrush;
        button.Resources["SplitButtonBorderBrushPointerOver"] = BorderMediumBrush;
        button.Resources["SplitButtonBorderBrushPressed"] = BorderMediumBrush;
        button.Resources["SplitButtonBorderBrushDisabled"] = BorderSubtleBrush;
        button.Resources["SplitButtonMinHeight"] = 44d;
        button.Resources["SplitButtonSecondaryButtonSize"] = 40d;
        button.Resources["SplitButtonSeparatorWidth"] = 1d;

        button.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Button>("PART_PrimaryButton") is { } primary)
                AttachButtonTransitions(primary);
            if (e.NameScope.Find<Button>("PART_SecondaryButton") is not { } secondary)
                return;

            AttachButtonTransitions(secondary);
            if (secondary.Content is PathIcon arrow)
            {
                arrow.RenderTransformOrigin = new RelativePoint(
                    0.5, 0.5, RelativeUnit.Relative);
                arrow.RenderTransform = new RotateTransform(180);
            }
        };
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
        var expander = StyleExpander(new Expander
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
        });
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

    /// <summary>用轻量资源覆盖 Fluent Expander 的重色标题栏，同时保留原生键盘与自动化语义。</summary>
    public static Expander StyleExpander(Expander expander)
    {
        expander.Resources["ExpanderHeaderBackground"] = CardBrush;
        expander.Resources["ExpanderHeaderBackgroundPointerOver"] = GhostHoverBrush;
        expander.Resources["ExpanderHeaderBackgroundPressed"] = GhostPressedBrush;
        expander.Resources["ExpanderHeaderBorderBrush"] = BorderSubtleBrush;
        expander.Resources["ExpanderHeaderBorderBrushPointerOver"] = BorderMediumBrush;
        expander.Resources["ExpanderHeaderBorderBrushPressed"] = AccentBrush;
        expander.Resources["ExpanderHeaderForeground"] = TextPrimaryBrush;
        expander.Resources["ExpanderHeaderForegroundPointerOver"] = TextPrimaryBrush;
        expander.Resources["ExpanderHeaderForegroundPressed"] = TextPrimaryBrush;
        expander.Resources["ExpanderChevronBackground"] = Brushes.Transparent;
        expander.Resources["ExpanderChevronBackgroundPointerOver"] = SelectionBrush;
        expander.Resources["ExpanderChevronBackgroundPressed"] = SelectionBrush;
        expander.Resources["ExpanderChevronForeground"] = TextSecondaryBrush;
        expander.Resources["ExpanderChevronForegroundPointerOver"] = AccentBrush;
        expander.Resources["ExpanderChevronForegroundPressed"] = AccentPressedBrush;
        expander.Resources["ExpanderContentBackground"] = CardBrush;
        expander.Resources["ExpanderContentBorderBrush"] = BorderSubtleBrush;
        return expander;
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
