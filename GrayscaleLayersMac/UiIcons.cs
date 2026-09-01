using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    OpenFolder,
    Success,
    Error,
    Save,
    Undo,
    PmtMatrix,
    Renumber,
    Lock,
    Unlock,
    Nodes,
    Source
}

/// <summary>
/// 应用内可见操作图标的唯一入口。固定使用 Fluent Regular 单色字形，
/// 并把字形作为透明蒙版交给语义画刷着色，规避 macOS 上字体图标前景色
/// 被 Avalonia 内容模板重置为黑色的问题。
/// </summary>
internal static class UiIcons
{
    public static Control Create(UiIcon kind) => Create(kind, IconSize.Size20);

    public static Control CreateSmall(UiIcon kind) => Create(kind, IconSize.Size16);

    public static Control Labeled(UiIcon kind, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            CreateSmall(kind),
            new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = Avalonia.Media.FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center
            }
        }
    };

    private static Control Create(UiIcon kind, IconSize size)
    {
        var pixels = size switch
        {
            IconSize.Size16 => 16d,
            IconSize.Size20 => 20d,
            _ => 20d
        };
        var glyph = new FluentIcon
        {
            Icon = Resolve(kind),
            IconVariant = IconVariant.Regular,
            IconSize = size,
            FontSize = pixels,
            Width = pixels,
            Height = pixels,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new Border
        {
            Width = pixels,
            Height = pixels,
            Background = UiTheme.IconBrush,
            OpacityMask = new VisualBrush(glyph)
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

    internal static bool IsFluentIconControl(object? content) =>
        content is Border
        {
            OpacityMask: VisualBrush { Visual: FluentIcon }
        };

    private static Icon Resolve(UiIcon kind) => kind switch
    {
        UiIcon.Import => Icon.ArrowImport,
        UiIcon.ClearCache => Icon.DeleteDismiss,
        UiIcon.Appearance => Icon.WeatherMoon,
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
        UiIcon.Success => Icon.CheckmarkCircle,
        UiIcon.Error => Icon.ErrorCircle,
        UiIcon.Save => Icon.DocumentCheckmark,
        UiIcon.Undo => Icon.ArrowUndo,
        UiIcon.PmtMatrix => Icon.TableAdd,
        UiIcon.Renumber => Icon.NumberSymbolSquare,
        UiIcon.Lock => Icon.LockClosed,
        UiIcon.Unlock => Icon.LockClosedKey,
        UiIcon.Nodes => Icon.FlowchartCircle,
        UiIcon.Source => Icon.DatabaseSwitch,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
