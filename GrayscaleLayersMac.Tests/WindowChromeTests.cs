using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class WindowChromeTests
{
    [TestMethod]
    public void MainWindowChromeKeepsNativeButtonsInsideTheAppHeader()
    {
        Assert.AreEqual(SystemDecorations.Full, MainWindow.AppSystemDecorations);
        Assert.IsTrue(MainWindow.AppExtendsIntoWindowDecorations);
        Assert.IsTrue(MainWindow.AppChromeHints.HasFlag(
            ExtendClientAreaChromeHints.PreferSystemChrome));
        Assert.IsTrue(MainWindow.AppChromeHints.HasFlag(
            ExtendClientAreaChromeHints.OSXThickTitleBar));
    }

    [TestMethod]
    public void AppHeaderPaddingReservesTheMacTrafficLightSafeArea()
    {
        Assert.IsGreaterThanOrEqualTo(72, MainWindow.AppHeaderPadding.Left);
        Assert.AreEqual(8, MainWindow.AppHeaderPadding.Top);
        Assert.AreEqual(20, MainWindow.AppHeaderPadding.Right);
        Assert.AreEqual(8, MainWindow.AppHeaderPadding.Bottom);
    }

    [TestMethod]
    public void HeaderDragOnlyBeginsForThePrimaryPointerButton()
    {
        Assert.IsTrue(MainWindow.IsHeaderDragGesture(PointerUpdateKind.LeftButtonPressed));
        Assert.IsFalse(MainWindow.IsHeaderDragGesture(PointerUpdateKind.RightButtonPressed));
        Assert.IsFalse(MainWindow.IsHeaderDragGesture(PointerUpdateKind.MiddleButtonPressed));
    }
}
