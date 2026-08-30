using System;
using Avalonia.Media;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class UiThemeContrastTests
{
    [TestMethod]
    public void LightTheme_CoreTextAndAccentMeetContrastTargets() =>
        AssertCoreContrast(AppColorScheme.Light);

    [TestMethod]
    public void DarkTheme_CoreTextAndAccentMeetContrastTargets() =>
        AssertCoreContrast(AppColorScheme.Dark);

    private static void AssertCoreContrast(AppColorScheme scheme)
    {
        try
        {
            UiTheme.ApplyScheme(scheme);

            Assert.IsGreaterThanOrEqualTo(
                7.0,
                Contrast(UiTheme.TextPrimaryColor, UiTheme.RootColor),
                "主文字与窗口背景应达到增强级对比度");
            Assert.IsGreaterThanOrEqualTo(
                4.5,
                Contrast(UiTheme.TextSecondaryColor, UiTheme.CardColor),
                "次要文字与卡片背景应达到普通文字对比度");
            Assert.IsGreaterThanOrEqualTo(
                4.5,
                Contrast(UiTheme.AccentTextColor, UiTheme.AccentColor),
                "主按钮文字与强调色背景应达到普通文字对比度");
            Assert.IsGreaterThanOrEqualTo(
                4.5,
                Contrast(UiTheme.DangerTextBrush.Color, UiTheme.CardColor),
                "错误文字与卡片背景应达到普通文字对比度");
            Assert.IsGreaterThanOrEqualTo(
                3.0,
                Contrast(UiTheme.FocusRingColor, UiTheme.CardColor),
                "焦点环与卡片背景应达到非文字控件对比度");
            Assert.IsGreaterThanOrEqualTo(
                3.0,
                Contrast(UiTheme.BorderStrongColor, UiTheme.SunkenColor),
                "强输入边界与下沉表面应达到非文字控件对比度");
        }
        finally
        {
            UiTheme.ApplyScheme(AppColorScheme.Dark);
        }
    }

    [TestMethod]
    public void ApplyScheme_UpdatesExistingBrushInstances()
    {
        var rootBrush = UiTheme.RootBrush;
        try
        {
            UiTheme.ApplyScheme(AppColorScheme.Light);
            var light = rootBrush.Color;

            UiTheme.ApplyScheme(AppColorScheme.Dark);

            Assert.AreSame(rootBrush, UiTheme.RootBrush);
            Assert.AreNotEqual(light, rootBrush.Color);
            Assert.AreEqual(UiTheme.RootColor, rootBrush.Color);
        }
        finally
        {
            UiTheme.ApplyScheme(AppColorScheme.Dark);
        }
    }

    private static double Contrast(Color first, Color second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte component)
    {
        var value = component / 255.0;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
