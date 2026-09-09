using System;
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
    public void AvailableSelectedTextureRemainsVisibleOutsideTopView()
    {
        var state = new DxfOverlayState();
        state.SetTextureAvailable(true);
        state.IsTopView = false;

        Assert.IsTrue(state.ShouldDrawTexture);
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

    [TestMethod]
    public void TextureOpacityRejectsNonFiniteValues()
    {
        var state = new DxfOverlayState();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            state.TextureOpacity = double.NaN);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            state.TextureOpacity = double.PositiveInfinity);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            state.TextureOpacity = double.NegativeInfinity);
    }

    [TestMethod]
    public void RemovingTextureAvailabilityPreservesVisibilityPreference()
    {
        var state = new DxfOverlayState { ShowTexture = true };
        state.SetTextureAvailable(true);

        state.SetTextureAvailable(false);

        Assert.IsTrue(state.ShowTexture);
        Assert.IsFalse(state.ShouldDrawTexture);
    }

    [TestMethod]
    public void PipelinePreviewCanStartTopViewWithoutChangingStandaloneDefault()
    {
        var standalone = new DxfOverlayState();
        var pipeline = new DxfOverlayState(startInTopView: true);

        Assert.IsFalse(standalone.IsTopView);
        Assert.IsTrue(pipeline.IsTopView);

        pipeline.IsTopView = false;
        Assert.IsFalse(pipeline.IsTopView);
    }
}
