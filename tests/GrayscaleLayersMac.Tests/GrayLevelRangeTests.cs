using System.Collections.Generic;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class GrayLevelRangeTests
{
    [TestMethod]
    public void Validate_AcceptsFullRangeWithMaximumLayers()
    {
        Assert.IsTrue(GrayLevelRange.TryValidate(0, 255, 255, out var error), error);
    }

    [TestMethod]
    public void Validate_AcceptsNarrowRangeWhenItFitsLayers()
    {
        Assert.IsTrue(GrayLevelRange.TryValidate(100, 200, 5, out var error), error);
        Assert.IsTrue(GrayLevelRange.TryValidate(100, 105, 5, out error), error);
    }

    [TestMethod]
    public void Validate_RejectsInvertedOrDegenerateRange()
    {
        Assert.IsFalse(GrayLevelRange.TryValidate(120, 120, 4, out var error));
        StringAssert.Contains(error, "灰阶上限必须大于下限");
        Assert.IsFalse(GrayLevelRange.TryValidate(200, 100, 4, out _));
    }

    [TestMethod]
    public void Validate_RejectsOutOfBoundsLevels()
    {
        Assert.IsFalse(GrayLevelRange.TryValidate(-1, 200, 4, out _));
        Assert.IsFalse(GrayLevelRange.TryValidate(100, 256, 4, out _));
        Assert.IsFalse(GrayLevelRange.TryValidate(255, 255, 4, out _));
        Assert.IsFalse(GrayLevelRange.TryValidate(0, 0, 4, out _));
    }

    [TestMethod]
    public void Validate_RejectsRangeNarrowerThanLayerCount()
    {
        Assert.IsFalse(GrayLevelRange.TryValidate(100, 104, 5, out var error));
        StringAssert.Contains(error, "不足以分成 5 层");
    }

    [TestMethod]
    public void EnsureUpperAbove_KeepsAtLeastOneLevelOfHeadroom()
    {
        Assert.AreEqual(121, GrayLevelRange.EnsureUpperAbove(120, 121));
        Assert.AreEqual(121, GrayLevelRange.EnsureUpperAbove(120, 120));
        Assert.AreEqual(121, GrayLevelRange.EnsureUpperAbove(120, 80));
        Assert.AreEqual(255, GrayLevelRange.EnsureUpperAbove(255, 255));
    }

    [TestMethod]
    public void EnsureLowerBelow_KeepsAtLeastOneLevelOfHeadroom()
    {
        Assert.AreEqual(119, GrayLevelRange.EnsureLowerBelow(119, 120));
        Assert.AreEqual(119, GrayLevelRange.EnsureLowerBelow(120, 120));
        Assert.AreEqual(119, GrayLevelRange.EnsureLowerBelow(200, 120));
        Assert.AreEqual(0, GrayLevelRange.EnsureLowerBelow(0, 0));
    }

    [TestMethod]
    public void AppendArguments_WritesScriptFlags()
    {
        var arguments = new List<string> { "grayscale_layers.py" };
        GrayLevelRange.AppendArguments(arguments, 60, 210);
        CollectionAssert.AreEqual(
            new[] { "grayscale_layers.py", "--min-level", "60", "--max-level", "210" },
            arguments);
    }
}
