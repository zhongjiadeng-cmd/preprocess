using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// 集中管理应用的视觉设计令牌与常用控件工厂，
/// 保证深色界面的背景层次、文字层级、强调色与圆角全局一致。
/// </summary>
internal static class UiTheme
{
    // ---- 背景层次（由深到浅：窗口 → 面板 → 卡片）----
    public static readonly Color RootColor = Color.FromRgb(15, 18, 22);
    public static readonly Color HeaderColor = Color.FromRgb(22, 26, 31);
    public static readonly Color PanelColor = Color.FromRgb(25, 29, 35);
    public static readonly Color CardColor = Color.FromRgb(33, 38, 46);
    public static readonly Color BarColor = Color.FromRgb(28, 33, 40);
    public static readonly Color SunkenColor = Color.FromRgb(13, 16, 20);

    // ---- 文字层级 ----
    public static readonly Color TextPrimaryColor = Color.FromRgb(233, 237, 242);
    public static readonly Color TextSecondaryColor = Color.FromRgb(165, 173, 186);
    public static readonly Color TextFaintColor = Color.FromRgb(122, 130, 143);

    // ---- 强调色 ----
    public static readonly Color AccentColor = Color.FromRgb(245, 166, 35);
    public static readonly Color AccentTextColor = Color.FromRgb(28, 23, 12);

    // ---- 边框 ----
    public static readonly Color BorderSubtleColor = Color.FromArgb(20, 255, 255, 255);
    public static readonly Color BorderMediumColor = Color.FromArgb(42, 255, 255, 255);

    // ---- 圆角 ----
    public static readonly CornerRadius CardRadius = new(12);
    public static readonly CornerRadius ControlRadius = new(8);
    public static readonly CornerRadius BadgeRadius = new(999);

    // ---- 画刷 ----
    public static readonly IBrush RootBrush = new SolidColorBrush(RootColor);
    public static readonly IBrush HeaderBrush = new SolidColorBrush(HeaderColor);
    public static readonly IBrush PanelBrush = new SolidColorBrush(PanelColor);
    public static readonly IBrush CardBrush = new SolidColorBrush(CardColor);
    public static readonly IBrush BarBrush = new SolidColorBrush(BarColor);
    public static readonly IBrush SunkenBrush = new SolidColorBrush(SunkenColor);
    public static readonly IBrush AccentBrush = new SolidColorBrush(AccentColor);
    public static readonly IBrush AccentTextBrush = new SolidColorBrush(AccentTextColor);
    public static readonly IBrush TextPrimaryBrush = new SolidColorBrush(TextPrimaryColor);
    public static readonly IBrush TextSecondaryBrush = new SolidColorBrush(TextSecondaryColor);
    public static readonly IBrush TextFaintBrush = new SolidColorBrush(TextFaintColor);
    public static readonly IBrush BorderSubtleBrush = new SolidColorBrush(BorderSubtleColor);
    public static readonly IBrush BorderMediumBrush = new SolidColorBrush(BorderMediumColor);

    public static readonly FontFamily MonoFont = FontFamily.Parse("Menlo, monospace");

    /// <summary>页面大标题（检查器顶部）。</summary>
    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeight.SemiBold,
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

    /// <summary>小面板标题（日志、预览等区块标题）。</summary>
    public static TextBlock PanelLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = TextSecondaryBrush
    };

    /// <summary>表单字段标签（次要层级的小字）。</summary>
    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = TextSecondaryBrush
    };

    /// <summary>为已有的按钮应用主操作样式（橙色强调、圆角、加高）。</summary>
    public static void ApplyPrimaryStyle(Button button)
    {
        button.Height = 44;
        button.FontSize = 15;
        button.FontWeight = FontWeight.SemiBold;
        button.CornerRadius = ControlRadius;
        button.Background = AccentBrush;
        button.Foreground = AccentTextBrush;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
    }

    /// <summary>为次级按钮应用柔和样式（细边框、圆角）。</summary>
    public static void ApplyGhostStyle(Button button)
    {
        button.MinHeight = 34;
        button.CornerRadius = ControlRadius;
        button.BorderBrush = BorderMediumBrush;
        button.BorderThickness = new Thickness(1);
        button.Background = Brushes.Transparent;
    }

    /// <summary>主进度条（强调色前景）。</summary>
    public static ProgressBar CreateProgress() => new()
    {
        IsIndeterminate = false,
        Height = 6,
        Foreground = AccentBrush
    };

    /// <summary>标题行左侧的强调色小色条，用于预览等区块的视觉锚点。</summary>
    public static Border AccentBar(double width = 4, double height = 16) => new()
    {
        Width = width,
        Height = height,
        CornerRadius = new CornerRadius(2),
        Background = AccentBrush,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>头部右侧的小徽章（胶囊描边）。</summary>
    public static Border Badge(string text) => new()
    {
        Padding = new Thickness(10, 4),
        CornerRadius = BadgeRadius,
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
        Padding = new Thickness(10, 8)
    };

    /// <summary>把可折叠分组包成圆角卡片（浮起表面 + 细描边）。</summary>
    public static Control CardExpander(string title, Control content)
    {
        var expander = new Expander
        {
            Header = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextPrimaryBrush
            },
            IsExpanded = true,
            Background = Brushes.Transparent,
            Padding = new Thickness(14, 10, 14, 14),
            Content = content
        };
        return new Border
        {
            Background = CardBrush,
            BorderBrush = BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = CardRadius,
            ClipToBounds = true,
            Child = expander
        };
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
