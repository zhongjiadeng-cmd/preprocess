using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class UiControlStyleTests
{
    [TestMethod]
    public void SecondaryButtonUsesApprovedControlMetrics()
    {
        var button = new Button();

        UiTheme.ApplySecondaryStyle(button);

        Assert.IsTrue(button.Classes.Contains("btn-secondary"));
        Assert.AreEqual(UiTheme.ControlHeight, button.MinHeight);
        Assert.AreEqual(UiTheme.ControlRadius, button.CornerRadius);
    }

    [TestMethod]
    public void PrimaryAndDangerButtonsHaveExplicitSemanticRoles()
    {
        var primary = new Button();
        var danger = new Button();

        UiTheme.ApplyPrimaryStyle(primary);
        UiTheme.ApplySecondaryStyle(danger);
        UiTheme.MarkDanger(danger);

        Assert.IsTrue(primary.Classes.Contains("accent"));
        Assert.AreEqual(UiTheme.PrimaryButtonHeight, primary.Height);
        Assert.IsTrue(danger.Classes.Contains("btn-secondary"));
        Assert.IsTrue(danger.Classes.Contains("danger"));
    }

    [TestMethod]
    public void QuietButtonUsesASeparateVisualRole()
    {
        var button = new Button();

        UiTheme.ApplyQuietStyle(button);

        Assert.IsTrue(button.Classes.Contains("btn-quiet"));
        Assert.IsFalse(button.Classes.Contains("btn-secondary"));
        Assert.AreEqual(UiTheme.ControlHeight, button.MinHeight);
    }

    [TestMethod]
    public void IconButtonHasTargetSizeAndAccessibleName()
    {
        var button = new Button();

        UiTheme.ApplyIconStyle(button, "清空缓存");

        Assert.IsTrue(button.Classes.Contains("btn-icon"));
        Assert.AreEqual(UiTheme.IconButtonSize, button.Width);
        Assert.AreEqual(UiTheme.IconButtonSize, button.Height);
        Assert.AreEqual("清空缓存", AutomationProperties.GetName(button));
    }

    [TestMethod]
    public void InputControlsUseTheSharedInputClassAndHeight()
    {
        Control[] controls = [new TextBox(), new NumericUpDown(), new ComboBox()];

        foreach (var control in controls)
        {
            UiTheme.ApplyInputStyle(control);
            Assert.IsTrue(control.Classes.Contains("input-control"));
            Assert.AreEqual(UiTheme.ControlHeight, control.MinHeight);
        }
    }

    [TestMethod]
    public void ReadOnlyAndErrorInputsExposeStateClasses()
    {
        var readOnly = new TextBox { IsReadOnly = true };
        var invalid = new NumericUpDown();

        UiTheme.ApplyInputStyle(readOnly);
        UiTheme.ApplyInputStyle(invalid);
        UiTheme.SetInputError(invalid, true);

        Assert.IsTrue(readOnly.Classes.Contains("input-readonly"));
        Assert.IsTrue(invalid.Classes.Contains("input-error"));

        UiTheme.SetInputError(invalid, false);
        Assert.IsFalse(invalid.Classes.Contains("input-error"));
    }

    [TestMethod]
    public void RadioExpanderAndSegmentedControlsKeepNativeSemantics()
    {
        var radio = new RadioButton();
        var expander = new Expander();
        var tab = new ToggleButton();

        UiTheme.ApplyAppearanceOptionStyle(radio);
        UiTheme.StyleExpander(expander);
        UiTheme.ApplyPreviewTabStyle(tab);

        Assert.IsTrue(radio.Classes.Contains("appearance-option"));
        Assert.AreEqual(UiTheme.ControlHeight, radio.MinHeight);
        Assert.IsTrue(expander.Classes.Contains("card-expander"));
        Assert.IsTrue(tab.Classes.Contains("preview-tab"));
    }

    [TestMethod]
    public void EmbeddedFlyoutSurfaceRequestsAChromeFreePresenter()
    {
        var flyout = new Flyout();

        UiTheme.RemoveFlyoutOuterChrome(flyout);

        Assert.IsTrue(
            flyout.FlyoutPresenterClasses.Contains(UiTheme.EmbeddedFlyoutPresenterClass));
    }

    [TestMethod]
    public void EmbeddedFlyoutPresenterStyleClearsEveryOuterChromeLayer()
    {
        var style = UiTheme.CreateGlobalStyles()
            .OfType<Style>()
            .Single(candidate => candidate.Selector?.ToString()
                .Contains(UiTheme.EmbeddedFlyoutPresenterClass) == true);
        var setters = style.Setters.OfType<Setter>().ToArray();

        Assert.AreSame(
            Brushes.Transparent,
            setters.Single(x => x.Property == TemplatedControl.BackgroundProperty).Value);
        Assert.AreSame(
            Brushes.Transparent,
            setters.Single(x => x.Property == TemplatedControl.BorderBrushProperty).Value);
        Assert.AreEqual(
            new Thickness(0),
            setters.Single(x => x.Property == TemplatedControl.BorderThicknessProperty).Value);
        Assert.AreEqual(
            new Thickness(0),
            setters.Single(x => x.Property == TemplatedControl.PaddingProperty).Value);
        Assert.AreEqual(
            new CornerRadius(0),
            setters.Single(x => x.Property == TemplatedControl.CornerRadiusProperty).Value);
    }

    [TestMethod]
    public void GlobalStylesContainAllApprovedVisualRoles()
    {
        var styles = UiTheme.CreateGlobalStyles();
        var selectors = styles.OfType<Style>()
            .Select(style => style.Selector?.ToString() ?? "")
            .ToArray();

        Assert.IsTrue(selectors.Any(selector => selector.Contains("btn-secondary")));
        Assert.IsTrue(selectors.Any(selector => selector.Contains("btn-quiet")));
        Assert.IsTrue(selectors.Any(selector => selector.Contains("btn-icon")));
        Assert.IsTrue(selectors.Any(selector => selector.Contains("input-control")));
        Assert.IsTrue(selectors.Any(selector => selector.Contains("appearance-option")));
        Assert.IsTrue(selectors.Any(selector =>
            selector.Contains(UiTheme.EmbeddedFlyoutPresenterClass)));
        Assert.IsTrue(selectors.Any(selector => selector.Contains("input-error")));
    }
}
