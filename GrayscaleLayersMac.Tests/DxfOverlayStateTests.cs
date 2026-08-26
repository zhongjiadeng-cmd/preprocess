using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class DxfOverlayStateTests
{
    [TestMethod]
    public void TextureLinesAndArrowsToggleIndependently()
    {
        var state = new DxfOverlayState();
        state.SetTextureAvailable(true);
        state.ShowTexture = false;

        Assert.IsTrue(state.ShowLines);
        Assert.IsTrue(state.ShowDirectionArrows);
        Assert.IsFalse(state.ShouldDrawTexture);
    }

    [TestMethod]
    public void IsometricSuppressesTextureWithoutLosingSelection()
    {
        var state = new DxfOverlayState();
        state.SetTextureAvailable(true);
        state.IsTopView = false;

        Assert.IsFalse(state.ShouldDrawTexture);
        Assert.IsTrue(state.ShowTexture);

        state.IsTopView = true;
        Assert.IsTrue(state.ShouldDrawTexture);
    }

    [TestMethod]
    public void ArrowsRequireVisibleDxfLines()
    {
        var state = new DxfOverlayState { ShowLines = false };

        Assert.IsFalse(state.ShouldDrawDirectionArrows);
    }
}
