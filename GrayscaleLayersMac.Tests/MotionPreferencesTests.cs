using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MotionPreferencesTests
{
    [TestMethod]
    public void NormalModeAllowsSpatialMotionAndStandardButtonFeedback()
    {
        using var _ = MotionPreferences.OverrideForTesting(false);
        var button = new Button();

        UiTheme.ApplySecondaryStyle(button);

        Assert.IsTrue(MotionPreferences.AnimateSpatialProperties);
        Assert.IsNotNull(button.Transitions);
        Assert.AreEqual(3, button.Transitions.Count);
        Assert.IsTrue(button.Transitions.OfType<BrushTransition>()
            .All(transition => transition.Duration == TimeSpan.FromMilliseconds(140)));
    }

    [TestMethod]
    public void ReducedMotionSuppressesSpatialMotionButKeepsShortColorFeedback()
    {
        using var _ = MotionPreferences.OverrideForTesting(true);
        var button = new Button();

        UiTheme.ApplySecondaryStyle(button);

        Assert.IsFalse(MotionPreferences.AnimateSpatialProperties);
        Assert.IsNotNull(button.Transitions);
        Assert.IsTrue(button.Transitions.All(transition => transition is BrushTransition));
        Assert.IsTrue(button.Transitions.OfType<BrushTransition>()
            .All(transition => transition.Duration == TimeSpan.FromMilliseconds(80)));
    }
}
