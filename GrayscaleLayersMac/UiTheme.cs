using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
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
        Color Popup,
        Color TextPrimary,
        Color TextSecondary,
        Color TextFaint,
        Color TextDisabled,
        Color Accent,
        Color AccentHover,
        Color AccentPressed,
        Color AccentText,
        Color Danger,
        Color DangerText,
        Color Warning,
        Color WarningText,
        Color Success,
        Color SuccessText,
        Color Info,
        Color InfoText,
        Color FocusRing,
        Color DisabledBackground,
        Color BorderSubtle,
        Color BorderMedium,
        Color BorderStrong,
        Color Ghost,
        Color GhostHover,
        Color GhostPressed,
        Color Handle,
        Color HandleHover,
        Color Selection,
        Color Icon,
        Color IconHover,
        Color IconPressed,
        Color IconDisabled);

    private static readonly Palette DarkPalette = new(
        Root: Color.FromRgb(12, 14, 18),
        Header: Color.FromRgb(18, 21, 27),
        Panel: Color.FromRgb(22, 25, 32),
        Card: Color.FromRgb(28, 32, 40),
        Bar: Color.FromRgb(25, 29, 36),
        Sunken: Color.FromRgb(8, 10, 14),
        Popup: Color.FromRgb(32, 36, 45),
        TextPrimary: Color.FromRgb(242, 244, 248),
        TextSecondary: Color.FromRgb(177, 185, 198),
        TextFaint: Color.FromRgb(121, 131, 147),
        TextDisabled: Color.FromRgb(104, 113, 128),
        Accent: Color.FromRgb(10, 111, 209),
        AccentHover: Color.FromRgb(42, 134, 224),
        AccentPressed: Color.FromRgb(0, 86, 170),
        AccentText: Colors.White,
        Danger: Color.FromRgb(232, 88, 82),
        DangerText: Color.FromRgb(255, 165, 159),
        Warning: Color.FromRgb(244, 180, 0),
        WarningText: Color.FromRgb(255, 214, 102),
        Success: Color.FromRgb(48, 201, 116),
        SuccessText: Color.FromRgb(117, 231, 165),
        Info: Color.FromRgb(76, 154, 255),
        InfoText: Color.FromRgb(145, 196, 255),
        FocusRing: Color.FromRgb(91, 174, 255),
        DisabledBackground: Color.FromArgb(18, 255, 255, 255),
        BorderSubtle: Color.FromArgb(24, 255, 255, 255),
        BorderMedium: Color.FromArgb(48, 255, 255, 255),
        BorderStrong: Color.FromArgb(78, 255, 255, 255),
        Ghost: Color.FromArgb(9, 255, 255, 255),
        GhostHover: Color.FromArgb(24, 255, 255, 255),
        GhostPressed: Color.FromArgb(38, 255, 255, 255),
        Handle: Color.FromRgb(35, 40, 50),
        HandleHover: Color.FromRgb(47, 54, 67),
        Selection: Color.FromArgb(48, 10, 111, 209),
        Icon: Color.FromRgb(177, 185, 198),
        IconHover: Color.FromRgb(242, 244, 248),
        IconPressed: Colors.White,
        IconDisabled: Color.FromRgb(104, 113, 128));

    private static readonly Palette LightPalette = new(
        Root: Color.FromRgb(241, 239, 235),
        Header: Color.FromRgb(249, 248, 245),
        Panel: Color.FromRgb(238, 241, 245),
        Card: Color.FromRgb(253, 253, 252),
        Bar: Color.FromRgb(247, 248, 250),
        Sunken: Color.FromRgb(231, 235, 240),
        Popup: Colors.White,
        TextPrimary: Color.FromRgb(28, 32, 39),
        TextSecondary: Color.FromRgb(75, 84, 98),
        TextFaint: Color.FromRgb(108, 118, 132),
        TextDisabled: Color.FromRgb(136, 143, 154),
        Accent: Color.FromRgb(0, 101, 204),
        AccentHover: Color.FromRgb(0, 119, 230),
        AccentPressed: Color.FromRgb(0, 78, 164),
        AccentText: Colors.White,
        Danger: Color.FromRgb(190, 45, 42),
        DangerText: Color.FromRgb(166, 35, 34),
        Warning: Color.FromRgb(161, 103, 0),
        WarningText: Color.FromRgb(126, 75, 0),
        Success: Color.FromRgb(19, 121, 66),
        SuccessText: Color.FromRgb(15, 105, 56),
        Info: Color.FromRgb(0, 93, 184),
        InfoText: Color.FromRgb(0, 78, 155),
        FocusRing: Color.FromRgb(0, 101, 204),
        DisabledBackground: Color.FromArgb(14, 35, 45, 60),
        BorderSubtle: Color.FromArgb(28, 35, 45, 60),
        BorderMedium: Color.FromArgb(52, 35, 45, 60),
        BorderStrong: Color.FromArgb(82, 35, 45, 60),
        Ghost: Color.FromArgb(8, 35, 45, 60),
        GhostHover: Color.FromArgb(17, 35, 45, 60),
        GhostPressed: Color.FromArgb(28, 35, 45, 60),
        Handle: Color.FromRgb(247, 248, 250),
        HandleHover: Color.FromRgb(232, 237, 243),
        Selection: Color.FromArgb(32, 0, 101, 204),
        Icon: Color.FromRgb(75, 84, 98),
        IconHover: Color.FromRgb(28, 32, 39),
        IconPressed: Colors.White,
        IconDisabled: Color.FromRgb(136, 143, 154));

    public static AppColorScheme CurrentScheme { get; private set; } = AppColorScheme.Dark;

    public static Color RootColor { get; private set; } = DarkPalette.Root;
    public static Color HeaderColor { get; private set; } = DarkPalette.Header;
    public static Color PanelColor { get; private set; } = DarkPalette.Panel;
    public static Color CardColor { get; private set; } = DarkPalette.Card;
    public static Color BarColor { get; private set; } = DarkPalette.Bar;
    public static Color SunkenColor { get; private set; } = DarkPalette.Sunken;
    public static Color PopupColor { get; private set; } = DarkPalette.Popup;
    public static Color TextPrimaryColor { get; private set; } = DarkPalette.TextPrimary;
    public static Color TextSecondaryColor { get; private set; } = DarkPalette.TextSecondary;
    public static Color TextFaintColor { get; private set; } = DarkPalette.TextFaint;
    public static Color TextDisabledColor { get; private set; } = DarkPalette.TextDisabled;
    public static Color AccentColor { get; private set; } = DarkPalette.Accent;
    public static Color AccentHoverColor { get; private set; } = DarkPalette.AccentHover;
    public static Color AccentPressedColor { get; private set; } = DarkPalette.AccentPressed;
    public static Color AccentTextColor { get; private set; } = DarkPalette.AccentText;
    public static Color DangerColor { get; private set; } = DarkPalette.Danger;
    public static Color FocusRingColor { get; private set; } = DarkPalette.FocusRing;
    public static Color BorderSubtleColor { get; private set; } = DarkPalette.BorderSubtle;
    public static Color BorderMediumColor { get; private set; } = DarkPalette.BorderMedium;
    public static Color BorderStrongColor { get; private set; } = DarkPalette.BorderStrong;

    // ---- 圆角 ----
    public const double ControlHeight = 36;
    public const double IconButtonSize = 32;
    public const double PrimaryButtonHeight = 44;
    public static readonly CornerRadius CardRadius = new(12);
    public static readonly CornerRadius ControlRadius = new(8);
    public static readonly CornerRadius SegmentRadius = new(9);

    // ---- 画刷 ----
    public static readonly SolidColorBrush RootBrush = new(RootColor);
    public static readonly SolidColorBrush HeaderBrush = new(HeaderColor);
    public static readonly SolidColorBrush PanelBrush = new(PanelColor);
    public static readonly SolidColorBrush CardBrush = new(CardColor);
    public static readonly SolidColorBrush BarBrush = new(BarColor);
    public static readonly SolidColorBrush SunkenBrush = new(SunkenColor);
    public static readonly SolidColorBrush PopupBrush = new(PopupColor);
    public static readonly SolidColorBrush AccentBrush = new(AccentColor);
    public static readonly SolidColorBrush AccentHoverBrush = new(AccentHoverColor);
    public static readonly SolidColorBrush AccentPressedBrush = new(AccentPressedColor);
    public static readonly SolidColorBrush AccentTextBrush = new(AccentTextColor);
    public static readonly SolidColorBrush TextPrimaryBrush = new(TextPrimaryColor);
    public static readonly SolidColorBrush TextSecondaryBrush = new(TextSecondaryColor);
    public static readonly SolidColorBrush TextFaintBrush = new(TextFaintColor);
    public static readonly SolidColorBrush TextDisabledBrush = new(TextDisabledColor);
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
    public static readonly SolidColorBrush WarningBrush = new(DarkPalette.Warning);
    public static readonly SolidColorBrush WarningTextBrush = new(DarkPalette.WarningText);
    public static readonly SolidColorBrush SuccessBrush = new(DarkPalette.Success);
    public static readonly SolidColorBrush SuccessTextBrush = new(DarkPalette.SuccessText);
    public static readonly SolidColorBrush InfoBrush = new(DarkPalette.Info);
    public static readonly SolidColorBrush InfoTextBrush = new(DarkPalette.InfoText);
    public static readonly SolidColorBrush FocusRingBrush = new(DarkPalette.FocusRing);
    public static readonly SolidColorBrush DisabledBackgroundBrush = new(DarkPalette.DisabledBackground);
    public static readonly SolidColorBrush SelectionBrush = new(DarkPalette.Selection);
    public static readonly SolidColorBrush IconBrush = new(DarkPalette.Icon);
    public static readonly SolidColorBrush IconHoverBrush = new(DarkPalette.IconHover);
    public static readonly SolidColorBrush IconPressedBrush = new(DarkPalette.IconPressed);
    public static readonly SolidColorBrush IconDisabledBrush = new(DarkPalette.IconDisabled);

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
        PopupColor = palette.Popup;
        TextPrimaryColor = palette.TextPrimary;
        TextSecondaryColor = palette.TextSecondary;
        TextFaintColor = palette.TextFaint;
        TextDisabledColor = palette.TextDisabled;
        AccentColor = palette.Accent;
        AccentHoverColor = palette.AccentHover;
        AccentPressedColor = palette.AccentPressed;
        AccentTextColor = palette.AccentText;
        DangerColor = palette.Danger;
        FocusRingColor = palette.FocusRing;
        BorderSubtleColor = palette.BorderSubtle;
        BorderMediumColor = palette.BorderMedium;
        BorderStrongColor = palette.BorderStrong;

        RootBrush.Color = palette.Root;
        HeaderBrush.Color = palette.Header;
        PanelBrush.Color = palette.Panel;
        CardBrush.Color = palette.Card;
        BarBrush.Color = palette.Bar;
        SunkenBrush.Color = palette.Sunken;
        PopupBrush.Color = palette.Popup;
        TextPrimaryBrush.Color = palette.TextPrimary;
        TextSecondaryBrush.Color = palette.TextSecondary;
        TextFaintBrush.Color = palette.TextFaint;
        TextDisabledBrush.Color = palette.TextDisabled;
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
        WarningBrush.Color = palette.Warning;
        WarningTextBrush.Color = palette.WarningText;
        SuccessBrush.Color = palette.Success;
        SuccessTextBrush.Color = palette.SuccessText;
        InfoBrush.Color = palette.Info;
        InfoTextBrush.Color = palette.InfoText;
        FocusRingBrush.Color = palette.FocusRing;
        DisabledBackgroundBrush.Color = palette.DisabledBackground;
        SelectionBrush.Color = palette.Selection;
        IconBrush.Color = palette.Icon;
        IconHoverBrush.Color = palette.IconHover;
        IconPressedBrush.Color = palette.IconPressed;
        IconDisabledBrush.Color = palette.IconDisabled;

        SchemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 窗口级全局交互样式：按钮按语义类名提供
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

        var primaryDisabled = new Style(x => x.OfType<Button>().Class("accent").Class(":disabled"));
        primaryDisabled.Setters.Add(new Setter(Button.BackgroundProperty, DisabledBackgroundBrush));
        primaryDisabled.Setters.Add(new Setter(Button.ForegroundProperty, TextDisabledBrush));
        primaryDisabled.Setters.Add(new Setter(Button.BorderBrushProperty, BorderSubtleBrush));
        styles.Add(primaryDisabled);

        // ---- 次级按钮（有边界、有重量，但不与主操作竞争）----
        var secondary = new Style(x => x.OfType<Button>().Class("btn-secondary"));
        secondary.Setters.Add(new Setter(Button.BackgroundProperty, CardBrush));
        secondary.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        secondary.Setters.Add(new Setter(Button.BorderBrushProperty, BorderMediumBrush));
        secondary.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        styles.Add(secondary);

        var secondaryHover = new Style(
            x => x.OfType<Button>().Class("btn-secondary").Class(":pointerover"));
        secondaryHover.Setters.Add(new Setter(Button.BackgroundProperty, GhostHoverBrush));
        secondaryHover.Setters.Add(new Setter(Button.BorderBrushProperty, BorderStrongBrush));
        styles.Add(secondaryHover);

        var secondaryPressed = new Style(
            x => x.OfType<Button>().Class("btn-secondary").Class(":pressed"));
        secondaryPressed.Setters.Add(new Setter(Button.BackgroundProperty, GhostPressedBrush));
        secondaryPressed.Setters.Add(new Setter(Button.BorderBrushProperty, AccentBrush));
        styles.Add(secondaryPressed);

        var secondaryFocus = new Style(
            x => x.OfType<Button>().Class("btn-secondary").Class(":focus"));
        secondaryFocus.Setters.Add(new Setter(Button.BorderBrushProperty, FocusRingBrush));
        secondaryFocus.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(2)));
        styles.Add(secondaryFocus);

        var secondaryDisabled = new Style(
            x => x.OfType<Button>().Class("btn-secondary").Class(":disabled"));
        secondaryDisabled.Setters.Add(new Setter(Button.BackgroundProperty, DisabledBackgroundBrush));
        secondaryDisabled.Setters.Add(new Setter(Button.ForegroundProperty, TextDisabledBrush));
        secondaryDisabled.Setters.Add(new Setter(Button.BorderBrushProperty, BorderSubtleBrush));
        styles.Add(secondaryDisabled);

        // ---- 轻量按钮（菜单与低频操作，默认不显示实线边框）----
        var quiet = new Style(x => x.OfType<Button>().Class("btn-quiet"));
        quiet.Setters.Add(new Setter(Button.BackgroundProperty, GhostBrush));
        quiet.Setters.Add(new Setter(Button.ForegroundProperty, TextSecondaryBrush));
        quiet.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.Transparent));
        quiet.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        styles.Add(quiet);

        var quietHover = new Style(x => x.OfType<Button>().Class("btn-quiet").Class(":pointerover"));
        quietHover.Setters.Add(new Setter(Button.BackgroundProperty, GhostHoverBrush));
        quietHover.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        styles.Add(quietHover);

        var quietPressed = new Style(x => x.OfType<Button>().Class("btn-quiet").Class(":pressed"));
        quietPressed.Setters.Add(new Setter(Button.BackgroundProperty, GhostPressedBrush));
        quietPressed.Setters.Add(new Setter(Button.ForegroundProperty, TextPrimaryBrush));
        styles.Add(quietPressed);

        var quietFocus = new Style(x => x.OfType<Button>().Class("btn-quiet").Class(":focus"));
        quietFocus.Setters.Add(new Setter(Button.BorderBrushProperty, FocusRingBrush));
        quietFocus.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(2)));
        styles.Add(quietFocus);

        var quietDisabled = new Style(x => x.OfType<Button>().Class("btn-quiet").Class(":disabled"));
        quietDisabled.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        quietDisabled.Setters.Add(new Setter(Button.ForegroundProperty, TextDisabledBrush));
        styles.Add(quietDisabled);

        // ---- 纯图标按钮（小操作，命中区固定且状态靠图标与底色同时反馈）----
        var icon = new Style(x => x.OfType<Button>().Class("btn-icon"));
        icon.Setters.Add(new Setter(Button.BackgroundProperty, GhostBrush));
        icon.Setters.Add(new Setter(Button.ForegroundProperty, IconBrush));
        icon.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.Transparent));
        icon.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        styles.Add(icon);

        var iconHover = new Style(x => x.OfType<Button>().Class("btn-icon").Class(":pointerover"));
        iconHover.Setters.Add(new Setter(Button.BackgroundProperty, GhostHoverBrush));
        iconHover.Setters.Add(new Setter(Button.ForegroundProperty, IconHoverBrush));
        styles.Add(iconHover);

        var iconPressed = new Style(x => x.OfType<Button>().Class("btn-icon").Class(":pressed"));
        iconPressed.Setters.Add(new Setter(Button.BackgroundProperty, AccentPressedBrush));
        iconPressed.Setters.Add(new Setter(Button.ForegroundProperty, IconPressedBrush));
        styles.Add(iconPressed);

        var iconFocus = new Style(x => x.OfType<Button>().Class("btn-icon").Class(":focus"));
        iconFocus.Setters.Add(new Setter(Button.BorderBrushProperty, FocusRingBrush));
        iconFocus.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(2)));
        styles.Add(iconFocus);

        var iconDisabled = new Style(x => x.OfType<Button>().Class("btn-icon").Class(":disabled"));
        iconDisabled.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        iconDisabled.Setters.Add(new Setter(Button.ForegroundProperty, IconDisabledBrush));
        styles.Add(iconDisabled);

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

        // ---- 输入控件（文本、数字与下拉框共用一套边界与状态）----
        AddInputStyles<TextBox>(styles);
        AddInputStyles<NumericUpDown>(styles);
        AddInputStyles<ComboBox>(styles);

        var readOnlyInput = new Style(
            x => x.OfType<TextBox>().Class("input-control").Class("input-readonly"));
        readOnlyInput.Setters.Add(new Setter(TextBox.BackgroundProperty, DisabledBackgroundBrush));
        readOnlyInput.Setters.Add(new Setter(TextBox.ForegroundProperty, TextSecondaryBrush));
        styles.Add(readOnlyInput);

        var errorTextInput = new Style(
            x => x.OfType<TextBox>().Class("input-control").Class("input-error"));
        errorTextInput.Setters.Add(new Setter(TextBox.BorderBrushProperty, DangerBrush));
        errorTextInput.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(2)));
        styles.Add(errorTextInput);

        var errorNumberInput = new Style(
            x => x.OfType<NumericUpDown>().Class("input-control").Class("input-error"));
        errorNumberInput.Setters.Add(new Setter(NumericUpDown.BorderBrushProperty, DangerBrush));
        errorNumberInput.Setters.Add(new Setter(NumericUpDown.BorderThicknessProperty, new Thickness(2)));
        styles.Add(errorNumberInput);

        var errorComboInput = new Style(
            x => x.OfType<ComboBox>().Class("input-control").Class("input-error"));
        errorComboInput.Setters.Add(new Setter(ComboBox.BorderBrushProperty, DangerBrush));
        errorComboInput.Setters.Add(new Setter(ComboBox.BorderThicknessProperty, new Thickness(2)));
        styles.Add(errorComboInput);

        // ---- 外观菜单选项：保留原生 RadioButton 语义，只统一命中区与反馈 ----
        var appearanceOption = new Style(x => x.OfType<RadioButton>().Class("appearance-option"));
        appearanceOption.Setters.Add(new Setter(RadioButton.ForegroundProperty, TextPrimaryBrush));
        appearanceOption.Setters.Add(new Setter(RadioButton.BackgroundProperty, Brushes.Transparent));
        appearanceOption.Setters.Add(new Setter(RadioButton.PaddingProperty, new Thickness(10, 6)));
        appearanceOption.Setters.Add(new Setter(RadioButton.MinHeightProperty, ControlHeight));
        styles.Add(appearanceOption);

        var appearanceOptionHover = new Style(
            x => x.OfType<RadioButton>().Class("appearance-option").Class(":pointerover"));
        appearanceOptionHover.Setters.Add(new Setter(RadioButton.BackgroundProperty, GhostHoverBrush));
        styles.Add(appearanceOptionHover);

        var appearanceOptionFocus = new Style(
            x => x.OfType<RadioButton>().Class("appearance-option").Class(":focus"));
        appearanceOptionFocus.Setters.Add(new Setter(RadioButton.BorderBrushProperty, FocusRingBrush));
        appearanceOptionFocus.Setters.Add(new Setter(RadioButton.BorderThicknessProperty, new Thickness(2)));
        styles.Add(appearanceOptionFocus);

        // ---- 危险变体（取消按钮：悬停泛红）----
        var danger = new Style(x => x.OfType<Button>().Class("danger"));
        danger.Setters.Add(new Setter(Button.ForegroundProperty, DangerTextBrush));
        styles.Add(danger);

        var dangerHover = new Style(
            x => x.OfType<Button>().Class("danger").Class(":pointerover"));
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

    private static void AddInputStyles<TControl>(Styles styles)
        where TControl : TemplatedControl
    {
        var input = new Style(x => x.OfType<TControl>().Class("input-control"));
        input.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, SunkenBrush));
        input.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, TextPrimaryBrush));
        input.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, BorderMediumBrush));
        input.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        input.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, ControlRadius));
        styles.Add(input);

        var hover = new Style(
            x => x.OfType<TControl>().Class("input-control").Class(":pointerover"));
        hover.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, BorderStrongBrush));
        styles.Add(hover);

        var focus = new Style(x => x.OfType<TControl>().Class("input-control").Class(":focus"));
        focus.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, FocusRingBrush));
        focus.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(2)));
        styles.Add(focus);

        var disabled = new Style(
            x => x.OfType<TControl>().Class("input-control").Class(":disabled"));
        disabled.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, DisabledBackgroundBrush));
        disabled.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, TextDisabledBrush));
        disabled.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, BorderSubtleBrush));
        styles.Add(disabled);
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
        window.Resources["ComboBoxBackground"] = SunkenBrush;
        window.Resources["ComboBoxBackgroundPointerOver"] = SunkenBrush;
        window.Resources["ComboBoxBackgroundPressed"] = SunkenBrush;
        window.Resources["ComboBoxBorderBrush"] = BorderMediumBrush;
        window.Resources["ComboBoxBorderBrushPointerOver"] = BorderStrongBrush;
        window.Resources["ComboBoxBorderBrushFocused"] = FocusRingBrush;
        window.Resources["ComboBoxDropDownBackground"] = PopupBrush;
        window.Resources["ComboBoxItemBackgroundPointerOver"] = GhostHoverBrush;
        window.Resources["ComboBoxItemBackgroundSelected"] = SelectionBrush;
        window.Resources["MenuFlyoutPresenterBackground"] = PopupBrush;
        window.Resources["MenuFlyoutPresenterBorderBrush"] = BorderMediumBrush;
        window.Resources["MenuFlyoutItemBackgroundPointerOver"] = GhostHoverBrush;
        window.Resources["MenuFlyoutItemBackgroundPressed"] = GhostPressedBrush;
        window.Resources["ScrollBarThumbBackground"] = BorderStrongBrush;
        window.Resources["ScrollBarThumbBackgroundPointerOver"] = TextFaintBrush;
        window.Resources["NumericUpDownButtonBackgroundPointerOver"] = GhostHoverBrush;
        window.Resources["NumericUpDownButtonBackgroundPressed"] = GhostPressedBrush;
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
        button.Height = PrimaryButtonHeight;
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

    /// <summary>标准次级操作：清晰边界、与主按钮保持视觉层级。</summary>
    public static void ApplySecondaryStyle(Button button, bool small = false)
    {
        if (!button.Classes.Contains("btn-secondary"))
            button.Classes.Add("btn-secondary");
        button.MinHeight = small ? IconButtonSize : ControlHeight;
        button.FontSize = small ? 11.5 : 13;
        button.FontWeight = FontWeight.Medium;
        button.Padding = small ? new Thickness(10, 3) : new Thickness(14, 6);
        button.CornerRadius = ControlRadius;
        AttachButtonTransitions(button);
    }

    /// <summary>低频轻量操作：静止时克制，悬停与键盘焦点仍然明确。</summary>
    public static void ApplyQuietStyle(Button button, bool small = false)
    {
        if (!button.Classes.Contains("btn-quiet"))
            button.Classes.Add("btn-quiet");
        button.MinHeight = small ? IconButtonSize : ControlHeight;
        button.FontSize = small ? 11.5 : 13;
        button.FontWeight = FontWeight.Medium;
        button.Padding = small ? new Thickness(9, 3) : new Thickness(12, 6);
        button.CornerRadius = ControlRadius;
        AttachButtonTransitions(button);
    }

    /// <summary>小操作图标按钮：统一 32×32 命中区，并强制提供无障碍名称。</summary>
    public static void ApplyIconStyle(Button button, string automationName)
    {
        if (!button.Classes.Contains("btn-icon"))
            button.Classes.Add("btn-icon");
        button.Width = IconButtonSize;
        button.Height = IconButtonSize;
        button.MinWidth = IconButtonSize;
        button.MinHeight = IconButtonSize;
        button.Padding = new Thickness(6);
        button.CornerRadius = ControlRadius;
        AutomationProperties.SetName(button, automationName);
        AttachButtonTransitions(button);
    }

    /// <summary>文本、数字和下拉输入控件的统一高度、圆角与主题状态。</summary>
    public static void ApplyInputStyle(Control control)
    {
        if (!control.Classes.Contains("input-control"))
            control.Classes.Add("input-control");
        control.MinHeight = ControlHeight;
        if (control is TemplatedControl templated)
            templated.CornerRadius = ControlRadius;
        if (control is TextBox { IsReadOnly: true } && !control.Classes.Contains("input-readonly"))
            control.Classes.Add("input-readonly");
        if (control is TextBox textBox)
            textBox.Padding = new Thickness(10, 6);
        else if (control is NumericUpDown numberBox)
            numberBox.Padding = new Thickness(10, 5);
        else if (control is ComboBox comboBox)
            comboBox.Padding = new Thickness(10, 5);
    }

    /// <summary>显式切换输入错误状态，供校验逻辑复用，不改变字段值或绑定。</summary>
    public static void SetInputError(Control control, bool hasError)
    {
        if (hasError)
        {
            if (!control.Classes.Contains("input-error"))
                control.Classes.Add("input-error");
        }
        else
        {
            control.Classes.Remove("input-error");
        }
    }

    /// <summary>外观 Flyout 内的原生单选项。</summary>
    public static void ApplyAppearanceOptionStyle(RadioButton option)
    {
        if (!option.Classes.Contains("appearance-option"))
            option.Classes.Add("appearance-option");
        option.MinHeight = ControlHeight;
        option.CornerRadius = ControlRadius;
    }

    /// <summary>纹理 / DXF 原生 ToggleButton 分段项。</summary>
    public static void ApplyPreviewTabStyle(ToggleButton tab)
    {
        if (!tab.Classes.Contains("preview-tab"))
            tab.Classes.Add("preview-tab");
        tab.MinHeight = ControlHeight;
        tab.CornerRadius = ControlRadius;
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
        if (!expander.Classes.Contains("card-expander"))
            expander.Classes.Add("card-expander");
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
