using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// 可折叠的日志面板：标题行 + 控制台风格日志框。
/// 折叠后隐藏多行日志框，只保留标题行，并把最近一条日志内联显示在标题右侧，
/// 面板高度随之收成一行，把底部空间让给预览区。
/// 宿主可订阅 <see cref="CollapsedChanged"/> 同步调整所在 Grid 的行高。
/// </summary>
internal sealed class LogPanelView
{
    private readonly TextBox _log;
    private readonly Grid _layout;
    private readonly TextBlock _summary;
    private readonly Button _toggleButton;
    private bool _collapsed;

    public LogPanelView(TextBox log, string title)
    {
        _log = log;
        _layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6
        };

        _summary = new TextBlock
        {
            FontFamily = UiTheme.MonoFont,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 0, 4, 0),
            IsVisible = false
        };
        // 光标依赖平台服务，等真正挂到可视化树上再设置，便于无头环境单测。
        _summary.AttachedToVisualTree += (_, _) =>
            _summary.Cursor = new Cursor(StandardCursorType.Hand);
        _summary.PointerReleased += (_, _) => SetCollapsed(false);

        var clearButton = new Button { Content = "清空" };
        UiTheme.ApplyGhostStyle(clearButton, small: true);
        clearButton.Click += (_, _) => log.Clear();

        _toggleButton = new Button { Content = "折叠" };
        UiTheme.ApplyGhostStyle(_toggleButton, small: true);
        ToolTip.SetTip(_toggleButton, "折叠后只显示最新一条日志");
        _toggleButton.Click += (_, _) => SetCollapsed(!_collapsed);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 6,
            Children =
            {
                Place(UiTheme.PanelLabel(title), 0),
                Place(_summary, 1),
                Place(clearButton, 2),
                Place(_toggleButton, 3)
            }
        };

        Grid.SetRow(log, 1);
        _layout.Children.Add(header);
        _layout.Children.Add(log);

        Root = new Border
        {
            Padding = new Thickness(14, 10, 14, 12),
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = UiTheme.CardRadius,
            Background = UiTheme.PanelBrush,
            Child = _layout
        };

        log.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                RefreshSummary();
        };
        RefreshSummary();
    }

    /// <summary>面板根控件，直接放进宿主布局。</summary>
    public Control Root { get; }

    /// <summary>是否处于折叠状态（只显示最新一条日志）。</summary>
    public bool IsCollapsed => _collapsed;

    /// <summary>折叠时显示的那一行文字（日志的最后一条非空行）。</summary>
    public string SummaryText => _summary.Text ?? string.Empty;

    /// <summary>折叠状态变化时触发；宿主据此切换所在 Grid 的行高。</summary>
    public event EventHandler? CollapsedChanged;

    public void SetCollapsed(bool value)
    {
        if (_collapsed == value)
            return;

        _collapsed = value;
        _summary.IsVisible = value;
        _log.IsVisible = !value;
        _layout.RowSpacing = value ? 0 : 6;
        _toggleButton.Content = value ? "展开" : "折叠";
        ToolTip.SetTip(_toggleButton,
            value ? "展开完整日志" : "折叠后只显示最新一条日志");
        if (value)
            RefreshSummary();

        CollapsedChanged?.Invoke(this, EventArgs.Empty);
    }

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
