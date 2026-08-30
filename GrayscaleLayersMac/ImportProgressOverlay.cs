using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace GrayscaleLayersMac;

/// <summary>
/// 锚定在导入按钮下方的短生命周期进度浮层。它的表面在关闭时先反向收拢，
/// 并使用代次令新一轮导入能够中断旧一轮的延迟关闭。
/// </summary>
internal sealed class ImportProgressOverlay
{
    private static readonly TimeSpan SpatialMotion = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan SuccessHold = TimeSpan.FromMilliseconds(600);
    private static readonly Easing Motion = new CubicEaseOut();
    private const double ExpandedHeight = 136;
    private const double CollapsedOffset = -8;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Border _surface;
    private readonly TranslateTransform _surfaceTranslation = new();
    private readonly Border _icon;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly TextBlock _counter;
    private readonly TextBlock _liveRegion;
    private readonly ProgressBar _progress;
    private readonly Button _closeButton;
    private ImportProgressStage? _lastAnnouncedStage;
    private long _generation;
    private bool _motionAttached;

    public ImportProgressOverlay(
        Control anchor,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        _delay = delay ?? Task.Delay;

        _icon = (Border)UiIcons.Create(UiIcon.Import);
        _title = new TextBlock
        {
            FontFamily = UiTheme.UiFont,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = UiTheme.TextPrimaryBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _detail = new TextBlock
        {
            FontFamily = UiTheme.UiFont,
            FontSize = 11.5,
            Foreground = UiTheme.TextSecondaryBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _counter = new TextBlock
        {
            FontFamily = UiTheme.UiFont,
            FontSize = 11.5,
            Foreground = UiTheme.TextSecondaryBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _liveRegion = new TextBlock { IsVisible = false };
        AutomationProperties.SetLiveSetting(_liveRegion, AutomationLiveSetting.Polite);
        _progress = UiTheme.CreateProgress();
        _progress.Maximum = 1;

        _closeButton = new Button
        {
            Content = "关闭",
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        UiTheme.ApplyGhostStyle(_closeButton, small: true);
        _closeButton.Click += (_, _) => Close();

        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 9,
            Children = { Place(_icon, 0), Place(_title, 1) }
        };
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            RowSpacing = 7,
            Children =
            {
                AtRow(titleRow, 0),
                AtRow(_detail, 1),
                AtRow(_progress, 2),
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children = { Place(_counter, 0), Place(_closeButton, 1) }
                }, 3),
                _liveRegion
            }
        };
        _surface = new Border
        {
            Width = 320,
            Height = ExpandedHeight,
            Padding = new Thickness(14, 12),
            Background = UiTheme.PopupBrush,
            BorderBrush = UiTheme.BorderMediumBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = UiTheme.CardRadius,
            ClipToBounds = true,
            Opacity = 1,
            RenderTransform = _surfaceTranslation,
            Child = content
        };
        _surface.AttachedToVisualTree += (_, _) => AttachMotion();

        Root = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            VerticalOffset = 8,
            IsLightDismissEnabled = false,
            Child = _surface
        };
    }

    public Popup Root { get; }

    public bool IsOpen => Root.IsOpen;
    public double SurfaceHeight => _surface.Height;
    public double SurfaceOpacity => _surface.Opacity;
    public string TitleText => _title.Text ?? string.Empty;
    public string DetailText => _detail.Text ?? string.Empty;
    public string CounterText => _counter.Text ?? string.Empty;
    public bool CloseButtonVisible => _closeButton.IsVisible;
    public bool HasSpatialTransitions => MotionPreferences.AnimateSpatialProperties;
    public PlacementMode Placement => Root.Placement;

    public void Show(ImportProgressState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _generation++;
        Root.IsOpen = true;
        Apply(state);
        _surface.IsHitTestVisible = true;
        _surface.Height = ExpandedHeight;
        _surface.Opacity = 1;
        _surfaceTranslation.Y = 0;
    }

    public async Task ShowSucceededAndCollapseAsync(
        ImportProgressState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var generation = _generation;
        Apply(state);
        await _delay(SuccessHold, cancellationToken);
        if (generation == _generation)
            Close();
    }

    public void ShowFailure(ImportProgressState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsError)
            throw new ArgumentException("Failure overlay requires a failed progress state.", nameof(state));

        Show(state);
        _closeButton.IsVisible = true;
        _closeButton.Focus();
    }

    public void Close()
    {
        var generation = ++_generation;
        _surface.IsHitTestVisible = false;
        _surface.Height = MotionPreferences.AnimateSpatialProperties ? 0 : ExpandedHeight;
        _surface.Opacity = 0;
        _surfaceTranslation.Y = MotionPreferences.AnimateSpatialProperties ? CollapsedOffset : 0;

        if (!_motionAttached)
        {
            Root.IsOpen = false;
            return;
        }

        _ = CloseAfterTransitionAsync(generation);
    }

    private async Task CloseAfterTransitionAsync(long generation)
    {
        await _delay(MotionPreferences.FadeDuration(SpatialMotion), CancellationToken.None);
        if (generation == _generation)
            Root.IsOpen = false;
    }

    private void Apply(ImportProgressState state)
    {
        var isSuccess = state.Stage == ImportProgressStage.Succeeded;
        var isFailure = state.IsError;
        _title.Text = state.Message;
        _detail.Text = state.CurrentFileName is null
            ? string.Empty
            : Path.GetFileName(state.CurrentFileName);
        ToolTip.SetTip(_detail, _detail.Text);
        _counter.Text = state.CounterText;
        _progress.IsIndeterminate = state.IsIndeterminate;
        _progress.Value = state.ProgressValue ?? 0;
        _progress.Foreground = isSuccess ? UiTheme.SuccessBrush : isFailure ? UiTheme.WarningBrush : UiTheme.AccentBrush;
        _title.Foreground = isSuccess ? UiTheme.SuccessTextBrush : isFailure ? UiTheme.WarningTextBrush : UiTheme.TextPrimaryBrush;
        _detail.Foreground = isFailure ? UiTheme.WarningTextBrush : UiTheme.TextSecondaryBrush;
        _counter.Foreground = isFailure ? UiTheme.WarningTextBrush : UiTheme.TextSecondaryBrush;
        _closeButton.IsVisible = isFailure;
        SetIcon(isSuccess ? UiIcon.Success : isFailure ? UiIcon.Error : UiIcon.Import,
            isSuccess ? UiTheme.SuccessBrush : isFailure ? UiTheme.WarningBrush : UiTheme.IconBrush);

        if (_lastAnnouncedStage != state.Stage || state.IsTerminal)
        {
            _lastAnnouncedStage = state.Stage;
            AutomationProperties.SetName(_liveRegion, state.AutomationText);
        }
    }

    private void SetIcon(UiIcon kind, IBrush brush)
    {
        var glyph = UiIcons.Create(kind);
        _icon.OpacityMask = ((Border)glyph).OpacityMask;
        _icon.Background = brush;
    }

    private void AttachMotion()
    {
        if (_motionAttached)
            return;

        _motionAttached = true;
        var transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = MotionPreferences.FadeDuration(SpatialMotion),
                Easing = Motion
            }
        };
        if (MotionPreferences.AnimateSpatialProperties)
        {
            transitions.Add(new DoubleTransition
            {
                Property = Layoutable.HeightProperty,
                Duration = SpatialMotion,
                Easing = Motion
            });
            _surfaceTranslation.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.YProperty,
                    Duration = SpatialMotion,
                    Easing = Motion
                }
            };
        }

        _surface.Transitions = transitions;
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T Place<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
