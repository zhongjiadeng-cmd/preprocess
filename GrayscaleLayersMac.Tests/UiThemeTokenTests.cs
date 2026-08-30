using Avalonia.Media;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class UiThemeTokenTests
{
    [TestMethod]
    public void LightSchemeExposesEveryRequiredSemanticRole() =>
        AssertSemanticRoles(AppColorScheme.Light);

    [TestMethod]
    public void DarkSchemeExposesEveryRequiredSemanticRole() =>
        AssertSemanticRoles(AppColorScheme.Dark);

    private static void AssertSemanticRoles(AppColorScheme scheme)
    {
        try
        {
            UiTheme.ApplyScheme(scheme);

            var brushes = new SolidColorBrush[]
            {
                UiTheme.RootBrush, UiTheme.HeaderBrush, UiTheme.PanelBrush,
                UiTheme.CardBrush, UiTheme.BarBrush, UiTheme.SunkenBrush,
                UiTheme.PopupBrush, UiTheme.TextPrimaryBrush, UiTheme.TextSecondaryBrush,
                UiTheme.TextFaintBrush, UiTheme.TextDisabledBrush,
                UiTheme.BorderSubtleBrush, UiTheme.BorderMediumBrush,
                UiTheme.BorderStrongBrush, UiTheme.AccentBrush, UiTheme.AccentHoverBrush,
                UiTheme.AccentPressedBrush, UiTheme.SelectionBrush, UiTheme.FocusRingBrush,
                UiTheme.DisabledBackgroundBrush, UiTheme.DangerBrush, UiTheme.DangerTextBrush,
                UiTheme.WarningBrush, UiTheme.WarningTextBrush, UiTheme.SuccessBrush,
                UiTheme.SuccessTextBrush, UiTheme.InfoBrush, UiTheme.InfoTextBrush,
                UiTheme.IconBrush, UiTheme.IconHoverBrush, UiTheme.IconPressedBrush,
                UiTheme.IconDisabledBrush
            };

            Assert.IsTrue(brushes.All(brush => brush is not null));
        }
        finally
        {
            UiTheme.ApplyScheme(AppColorScheme.Dark);
        }
    }

    [TestMethod]
    public void SharedMetricsMatchTheApprovedDesktopContract()
    {
        CollectionAssert.AreEqual(
            new[] { 36d, 32d, 44d, 8d, 12d, 9d },
            new[]
            {
                UiTheme.ControlHeight, UiTheme.IconButtonSize, UiTheme.PrimaryButtonHeight,
                UiTheme.ControlRadius.TopLeft, UiTheme.CardRadius.TopLeft,
                UiTheme.SegmentRadius.TopLeft
            });
    }

    [TestMethod]
    public void ApplySchemeKeepsNewBrushInstancesStable()
    {
        var popup = UiTheme.PopupBrush;
        var focus = UiTheme.FocusRingBrush;
        var disabled = UiTheme.TextDisabledBrush;

        try
        {
            UiTheme.ApplyScheme(AppColorScheme.Light);
            UiTheme.ApplyScheme(AppColorScheme.Dark);

            Assert.AreSame(popup, UiTheme.PopupBrush);
            Assert.AreSame(focus, UiTheme.FocusRingBrush);
            Assert.AreSame(disabled, UiTheme.TextDisabledBrush);
        }
        finally
        {
            UiTheme.ApplyScheme(AppColorScheme.Dark);
        }
    }
}
