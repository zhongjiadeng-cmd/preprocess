using Avalonia.Styling;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class AppAppearanceResolverTests
{
    [TestMethod]
    public void RequestedThemeVariant_MapsAllChoices()
    {
        Assert.AreSame(
            ThemeVariant.Default,
            AppAppearanceResolver.RequestedThemeVariant(AppAppearance.System));
        Assert.AreSame(
            ThemeVariant.Light,
            AppAppearanceResolver.RequestedThemeVariant(AppAppearance.Light));
        Assert.AreSame(
            ThemeVariant.Dark,
            AppAppearanceResolver.RequestedThemeVariant(AppAppearance.Dark));
    }

    [TestMethod]
    public void EffectiveScheme_LightOnlyForLightVariant()
    {
        Assert.AreEqual(
            AppColorScheme.Light,
            AppAppearanceResolver.EffectiveScheme(ThemeVariant.Light));
        Assert.AreEqual(
            AppColorScheme.Dark,
            AppAppearanceResolver.EffectiveScheme(ThemeVariant.Dark));
        Assert.AreEqual(
            AppColorScheme.Dark,
            AppAppearanceResolver.EffectiveScheme(null));
    }

    [TestMethod]
    public void SystemChangesOnlyApplyWhileFollowingSystem()
    {
        Assert.IsTrue(AppAppearanceResolver.ShouldFollowSystem(AppAppearance.System));
        Assert.IsFalse(AppAppearanceResolver.ShouldFollowSystem(AppAppearance.Light));
        Assert.IsFalse(AppAppearanceResolver.ShouldFollowSystem(AppAppearance.Dark));
    }
}
