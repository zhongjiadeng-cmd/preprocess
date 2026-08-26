using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class SharedPreviewSelectionTests
{
    [TestMethod]
    public void AutomaticAndManualSwitchingPreservesBothContents()
    {
        var state = new SharedPreviewSelection();
        state.BeginTextureImport();
        state.CompleteTextureImport();
        Assert.AreEqual(SharedPreviewKind.Texture, state.Current);

        state.CompleteDxfLoad();
        Assert.AreEqual(SharedPreviewKind.Dxf, state.Current);

        state.Select(SharedPreviewKind.Texture);
        Assert.IsTrue(state.HasTexture && state.HasDxf);
    }

    [TestMethod]
    public void FailedTextureRemainsSelectedWithoutDiscardingDxf()
    {
        var state = new SharedPreviewSelection();
        state.CompleteDxfLoad();
        state.BeginTextureImport();
        state.FailTextureImport();

        Assert.AreEqual(SharedPreviewKind.Texture, state.Current);
        Assert.IsFalse(state.HasTexture);
        Assert.IsTrue(state.HasDxf);
    }
}
