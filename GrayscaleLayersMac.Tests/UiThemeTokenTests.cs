using Avalonia.Media;
using System;
using System.IO;
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

    [TestMethod]
    public void LightSchemeUsesCoolNeutralIndustrialSurfaces()
    {
        try
        {
            UiTheme.ApplyScheme(AppColorScheme.Light);

            CollectionAssert.AreEqual(
                new[]
                {
                    Color.FromRgb(229, 233, 238),
                    Color.FromRgb(246, 248, 250),
                    Color.FromRgb(240, 243, 246),
                    Color.FromRgb(252, 253, 254),
                    Color.FromRgb(238, 241, 245),
                    Color.FromRgb(232, 236, 241),
                    Colors.White
                },
                new[]
                {
                    UiTheme.RootBrush.Color,
                    UiTheme.HeaderBrush.Color,
                    UiTheme.PanelBrush.Color,
                    UiTheme.CardBrush.Color,
                    UiTheme.BarBrush.Color,
                    UiTheme.SunkenBrush.Color,
                    UiTheme.PopupBrush.Color
                });
        }
        finally
        {
            UiTheme.ApplyScheme(AppColorScheme.Dark);
        }
    }

    [TestMethod]
    public void LightSchemeUsesCoolNeutralInteractionSurfaces()
    {
        try
        {
            UiTheme.ApplyScheme(AppColorScheme.Light);

            Assert.AreEqual(Color.FromArgb(14, 61, 73, 89), UiTheme.DisabledBackgroundBrush.Color);
            Assert.AreEqual(Color.FromArgb(32, 61, 73, 89), UiTheme.BorderSubtleBrush.Color);
            Assert.AreEqual(Color.FromArgb(64, 61, 73, 89), UiTheme.BorderMediumBrush.Color);
            Assert.AreEqual(Color.FromArgb(160, 61, 73, 89), UiTheme.BorderStrongBrush.Color);
            Assert.AreEqual(Color.FromArgb(8, 61, 73, 89), UiTheme.GhostBrush.Color);
            Assert.AreEqual(Color.FromArgb(20, 61, 73, 89), UiTheme.GhostHoverBrush.Color);
            Assert.AreEqual(Color.FromArgb(32, 61, 73, 89), UiTheme.GhostPressedBrush.Color);
        }
        finally
        {
            UiTheme.ApplyScheme(AppColorScheme.Dark);
        }
    }

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
            new[] { 32d, 32d, 32d, 4d, 6d, 4d },
            new[]
            {
                UiTheme.ControlHeight, UiTheme.IconButtonSize, UiTheme.PrimaryButtonHeight,
                UiTheme.ControlRadius.TopLeft, UiTheme.CardRadius.TopLeft,
                UiTheme.SegmentRadius.TopLeft
            });
    }

    [TestMethod]
    public void TypographyProvidesExplicitChineseFallbacks()
    {
        if (OperatingSystem.IsMacOS())
            Assert.AreEqual("PingFang SC", UiTheme.UiFont.Name);
        else if (OperatingSystem.IsWindows())
            Assert.AreEqual("Microsoft YaHei UI", UiTheme.UiFont.Name);

        Assert.AreNotEqual("Inter", UiTheme.UiFont.Name);
        Assert.AreEqual(UiTheme.UiFont, UiTheme.UiTypeface.FontFamily);
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

    [TestMethod]
    public void GeneralStatusUiDoesNotUseHardCodedErrorBrushes()
    {
        var root = FindRepositoryRoot();
        foreach (var file in new[] { "MainWindow.cs", "GrayscaleLayerPreviewControl.cs", "App.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, "GrayscaleLayersMac", file));
            Assert.DoesNotContain("Brushes.OrangeRed", source, file);
        }

        var appSource = File.ReadAllText(Path.Combine(root, "GrayscaleLayersMac", "App.cs"));
        Assert.DoesNotContain("Color.FromRgb", appSource);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "GrayscaleLayersMac")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位测试仓库根目录。");
    }
}
