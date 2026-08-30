using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class UiIconsTests
{
    [TestMethod]
    public void EveryRequiredOperationUsesARegularFluentIcon()
    {
        foreach (var kind in Enum.GetValues<UiIcon>())
        {
            var control = UiIcons.Create(kind);

            Assert.AreEqual("FluentIcon", control.GetType().Name, $"{kind} 应使用 FluentIcon，而不是字符图标");
            Assert.AreEqual("Regular", ReadProperty(control, "IconVariant"), $"{kind} 必须使用单色 Regular 图标");
        }
    }

    [TestMethod]
    public void RequiredOperationsMapToStableFluentGlyphs()
    {
        AssertIcon(UiIcon.Import, "ArrowImport");
        AssertIcon(UiIcon.ClearCache, "DeleteDismiss");
        AssertIcon(UiIcon.Appearance, "DarkTheme");
        AssertIcon(UiIcon.PreviousLayer, "ArrowPrevious");
        AssertIcon(UiIcon.NextLayer, "ArrowNext");
        AssertIcon(UiIcon.ZoomOut, "ZoomOut");
        AssertIcon(UiIcon.ZoomIn, "ZoomIn");
        AssertIcon(UiIcon.Fit, "ArrowFit");
        AssertIcon(UiIcon.ActualSize, "ResizeImage");
        AssertIcon(UiIcon.ClearLog, "Broom");
        AssertIcon(UiIcon.Collapse, "ChevronDown");
        AssertIcon(UiIcon.Expand, "ChevronUp");
        AssertIcon(UiIcon.OpenFolder, "FolderOpen");
    }

    [TestMethod]
    public void TextFallbackKeepsTheOperationVisible()
    {
        var fallback = UiIcons.CreateTextFallback("清空缓存");

        Assert.IsInstanceOfType<TextBlock>(fallback);
        Assert.AreEqual("清空缓存", ((TextBlock)fallback).Text);
    }

    [TestMethod]
    public void LabeledIconKeepsReadableActionText()
    {
        var content = UiIcons.Labeled(UiIcon.Import, "导入");

        Assert.IsInstanceOfType<StackPanel>(content);
        var panel = (StackPanel)content;
        Assert.AreEqual("FluentIcon", panel.Children[0].GetType().Name);
        Assert.AreEqual("导入", ((TextBlock)panel.Children[1]).Text);
    }

    [TestMethod]
    public void TexturePreviewUsesNamedFluentIconButtons()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "GrayscaleLayersMac", "GrayscaleLayerPreviewControl.cs"));

        StringAssert.Contains(source, "MakeButton(UiIcon.ZoomOut, \"缩小\"");
        StringAssert.Contains(source, "MakeButton(UiIcon.ZoomIn, \"放大\"");
        StringAssert.Contains(source, "MakeButton(UiIcon.Fit, \"适应窗口\"");
        StringAssert.Contains(source, "MakeButton(UiIcon.ActualSize, \"实际尺寸\"");
        Assert.DoesNotContain("MakeButton(\"−\"", source);
        Assert.DoesNotContain("MakeButton(\"+\"", source);
    }

    private static void AssertIcon(UiIcon kind, string expected)
    {
        Assert.AreEqual(expected, ReadProperty(UiIcons.Create(kind), "Icon"));
    }

    private static string ReadProperty(Control control, string propertyName) =>
        control.GetType().GetProperty(propertyName)?.GetValue(control)?.ToString()
        ?? throw new AssertFailedException($"{control.GetType().Name} 缺少 {propertyName} 属性");

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
