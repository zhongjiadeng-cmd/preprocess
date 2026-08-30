using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace GrayscaleLayersMac;

public sealed class App : Application
{
    private readonly WorkspaceSplitSettings _uiSettings = WorkspaceSplitSettings.CreateDefault();

    internal AppAppearance Appearance { get; private set; } = AppAppearance.System;

    internal event EventHandler? AppearanceChanged;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        UiTheme.ApplyScheme(AppColorScheme.Dark);
        UiTheme.SchemeChanged += (_, _) => ApplyAccentResources();
        ActualThemeVariantChanged += (_, _) =>
        {
            if (AppAppearanceResolver.ShouldFollowSystem(Appearance))
                UiTheme.ApplyScheme(AppAppearanceResolver.EffectiveScheme(ActualThemeVariant));
        };
        SetAppearance(_uiSettings.LoadAppearance(), persist: false);

        // 全局强调色接管：复选框、页签指示条、输入框焦点、下拉、滚动条等
        // Fluent 控件与自定义 UI 统一使用蓝色交互语义；橙色只留给警告与加工数据。
        ApplyAccentResources();
    }

    internal void SetAppearance(AppAppearance appearance, bool persist = true)
    {
        Appearance = appearance;
        RequestedThemeVariant = AppAppearanceResolver.RequestedThemeVariant(appearance);

        var scheme = appearance switch
        {
            AppAppearance.Light => AppColorScheme.Light,
            AppAppearance.Dark => AppColorScheme.Dark,
            _ => AppAppearanceResolver.EffectiveScheme(ActualThemeVariant)
        };
        UiTheme.ApplyScheme(scheme);

        if (persist)
            _uiSettings.TrySaveAppearance(appearance);

        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyAccentResources()
    {
        Resources["SystemAccentColor"] = UiTheme.AccentColor;
        Resources["SystemAccentColorLight1"] = UiTheme.AccentHoverColor;
        Resources["SystemAccentColorLight2"] = UiTheme.FocusRingColor;
        Resources["SystemAccentColorLight3"] = UiTheme.InfoBrush.Color;
        Resources["SystemAccentColorDark1"] = UiTheme.AccentPressedColor;
        Resources["SystemAccentColorDark2"] = UiTheme.AccentPressedColor;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
