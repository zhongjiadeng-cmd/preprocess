using Avalonia.Styling;

namespace GrayscaleLayersMac;

internal enum AppAppearance
{
    System,
    Light,
    Dark
}

internal enum AppColorScheme
{
    Light,
    Dark
}

internal static class AppAppearanceResolver
{
    public static bool ShouldFollowSystem(AppAppearance appearance) =>
        appearance == AppAppearance.System;

    public static ThemeVariant RequestedThemeVariant(AppAppearance appearance) => appearance switch
    {
        AppAppearance.Light => ThemeVariant.Light,
        AppAppearance.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    public static AppColorScheme EffectiveScheme(ThemeVariant? actualThemeVariant) =>
        actualThemeVariant == ThemeVariant.Light
            ? AppColorScheme.Light
            : AppColorScheme.Dark;
}
