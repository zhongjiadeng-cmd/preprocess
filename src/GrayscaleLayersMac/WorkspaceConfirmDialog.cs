using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>A modal confirmation using the same surface and controls as workspace flyouts.</summary>
internal sealed class WorkspaceConfirmDialog : Window
{
    public WorkspaceConfirmDialog(string title, string message, string confirmText)
    {
        Title = title;
        Width = 430; SizeToContent = SizeToContent.Height; CanResize = false;
        ShowInTaskbar = false; SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UiTheme.PopupBrush; FontFamily = UiTheme.UiFont; FontSize = 12.5;
        Styles.Add(UiTheme.CreateGlobalStyles());
        UiTheme.ApplyFluentResourceOverrides(this);

        var cancel = new Button { Content = "继续编辑", IsCancel = true, IsDefault = true, MinWidth = 92 };
        UiTheme.ApplySecondaryStyle(cancel);
        cancel.Click += (_, _) => Close(false);
        var confirm = new Button { Content = confirmText, MinWidth = 116 };
        UiTheme.ApplySecondaryStyle(confirm); UiTheme.MarkDanger(confirm);
        confirm.Click += (_, _) => Close(true);
        var close = new Button { Content = "×" };
        UiTheme.ApplyIconStyle(close, "关闭确认窗口");
        close.Click += (_, _) => Close(false);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        var heading = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = UiTheme.TextPrimaryBrush, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(heading);
        Grid.SetColumn(close, 1); header.Children.Add(close);
        heading.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, confirm } };
        var content = new StackPanel { Spacing = 18, Children =
        {
            header,
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.TextSecondaryBrush, LineHeight = 21 },
            actions
        } };
        var surface = UiTheme.FlyoutSurface(content);
        surface.Padding = new Thickness(20, 14, 20, 20);
        Content = surface;
        Opened += (_, _) => cancel.Focus();
    }
}
