using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

internal sealed class ProcessingProgressWindow : Window
{
    private readonly Button _cancelButton;
    private readonly TextBlock _messageText;
    private bool _closingFromOwner;

    public event EventHandler? CancelRequested;

    public ProcessingProgressWindow(string title, string message)
    {
        Title = title;
        Width = 420;
        Height = 190;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _cancelButton.Click += (_, _) => RequestCancel();
        Closing += (_, _) => RequestCancel();
        UiTheme.ApplyGhostStyle(_cancelButton);
        UiTheme.MarkDanger(_cancelButton);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                _messageText,
                new ProgressBar { IsIndeterminate = true },
                _cancelButton
            }
        };
    }

    public void CloseFromOwner()
    {
        _closingFromOwner = true;
        Close();
    }

    public void UpdateMessage(string message) => _messageText.Text = message;

    private void RequestCancel()
    {
        if (_closingFromOwner || !_cancelButton.IsEnabled)
            return;

        _cancelButton.IsEnabled = false;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
