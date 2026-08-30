using Avalonia.Controls;
using Avalonia.Layout;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace GrayscaleLayersMac;

internal enum UiIcon
{
    Import,
    ClearCache,
    Appearance,
    PreviousLayer,
    NextLayer,
    ZoomOut,
    ZoomIn,
    Fit,
    ActualSize,
    ClearLog,
    Collapse,
    Expand,
    OpenFolder
}

/// <summary>
/// 应用内可见操作图标的唯一入口。固定使用 Fluent Regular 单色字形，
/// 让图标继承按钮前景色，从而自动响应浅深主题、悬停、按下与禁用状态。
/// </summary>
internal static class UiIcons
{
    public static Control Create(UiIcon kind) => Create(kind, IconSize.Size20);

    public static Control CreateSmall(UiIcon kind) => Create(kind, IconSize.Size16);

    private static Control Create(UiIcon kind, IconSize size)
    {
        return new FluentIcon
        {
            Icon = Resolve(kind),
            IconVariant = IconVariant.Regular,
            IconSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>图标字体若在异常环境中不可用，调用方可保留完整动作文字。</summary>
    public static Control CreateTextFallback(string actionName) => new TextBlock
    {
        Text = actionName,
        FontSize = 12,
        FontWeight = Avalonia.Media.FontWeight.Medium,
        Foreground = UiTheme.TextPrimaryBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Icon Resolve(UiIcon kind) => kind switch
    {
        UiIcon.Import => Icon.ArrowImport,
        UiIcon.ClearCache => Icon.DeleteDismiss,
        UiIcon.Appearance => Icon.DarkTheme,
        UiIcon.PreviousLayer => Icon.ArrowPrevious,
        UiIcon.NextLayer => Icon.ArrowNext,
        UiIcon.ZoomOut => Icon.ZoomOut,
        UiIcon.ZoomIn => Icon.ZoomIn,
        UiIcon.Fit => Icon.ArrowFit,
        UiIcon.ActualSize => Icon.ResizeImage,
        UiIcon.ClearLog => Icon.Broom,
        UiIcon.Collapse => Icon.ChevronDown,
        UiIcon.Expand => Icon.ChevronUp,
        UiIcon.OpenFolder => Icon.FolderOpen,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
