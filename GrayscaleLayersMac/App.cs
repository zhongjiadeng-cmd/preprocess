using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace GrayscaleLayersMac;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());

        // 全局强调色接管：复选框、页签指示条、输入框焦点、下拉、滚动条等
        // Fluent 控件全部跟随琥珀橙，与自定义 UI 的强调色保持统一。
        Resources["SystemAccentColor"] = Color.FromRgb(245, 166, 35);
        Resources["SystemAccentColorLight1"] = Color.FromRgb(255, 184, 77);
        Resources["SystemAccentColorLight2"] = Color.FromRgb(255, 196, 102);
        Resources["SystemAccentColorLight3"] = Color.FromRgb(255, 214, 140);
        Resources["SystemAccentColorDark1"] = Color.FromRgb(217, 142, 26);
        Resources["SystemAccentColorDark2"] = Color.FromRgb(194, 122, 20);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
