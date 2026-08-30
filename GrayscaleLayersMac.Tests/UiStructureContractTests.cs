using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class UiStructureContractTests
{
    private static readonly string MainWindowSource = File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), "GrayscaleLayersMac", "MainWindow.cs"));

    [TestMethod]
    public void OriginalInspectorSectionsRemainInOrder()
    {
        var grayscale = MainWindowSource.IndexOf(
            "MakeInspectorSection(\n                    \"灰度分层\"",
            StringComparison.Ordinal);
        var hatch = MainWindowSource.IndexOf(
            "MakeInspectorSection(\n                    \"Hatch 与 DXF\"",
            StringComparison.Ordinal);
        var voronoi = MainWindowSource.IndexOf(
            "MakeVoronoiPanel(",
            StringComparison.Ordinal);
        var machine = MainWindowSource.IndexOf(
            "MakeInspectorSection(\n                    \"机器加工文件\"",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, grayscale);
        Assert.IsGreaterThan(grayscale, hatch);
        Assert.IsGreaterThan(hatch, voronoi);
        Assert.IsGreaterThan(voronoi, machine);
    }

    [TestMethod]
    public void ExistingPreviewLogAndRunEntryPointsRemainPresent()
    {
        StringAssert.Contains(MainWindowSource, "MakeSharedPreviewPanel(");
        StringAssert.Contains(MainWindowSource, "Content = \"纹理\"");
        StringAssert.Contains(MainWindowSource, "Content = \"DXF\"");
        StringAssert.Contains(MainWindowSource, "_pipelineLogBox");
        StringAssert.Contains(MainWindowSource, "private readonly SplitButton _pipelineRunSplitButton");
        StringAssert.Contains(MainWindowSource, "Content = \"全部执行\"");
    }

    [TestMethod]
    public void HeaderOwnsTheCompactToolGroupWithoutChangingItsActions()
    {
        var inspectorStart = MainWindowSource.IndexOf("var pipelineInspector = new StackPanel", StringComparison.Ordinal);
        var inspectorEnd = MainWindowSource.IndexOf("var pipelinePreviewPanel", inspectorStart, StringComparison.Ordinal);
        var inspector = MainWindowSource[inspectorStart..inspectorEnd];
        var toolsStart = MainWindowSource.IndexOf("var headerTools = new Border", StringComparison.Ordinal);
        var toolsEnd = MainWindowSource.IndexOf("var appHeader = new Border", toolsStart, StringComparison.Ordinal);
        var tools = MainWindowSource[toolsStart..toolsEnd];

        Assert.DoesNotContain("_pipelineImportButton", inspector);
        StringAssert.Contains(tools, "_pipelineImportButton");
        StringAssert.Contains(tools, "_pipelineClearButton");
        StringAssert.Contains(tools, "_appearanceButton");
        StringAssert.Contains(MainWindowSource, "UiTheme.ApplyQuietStyle(_pipelineImportButton)");
        StringAssert.Contains(MainWindowSource, "UiTheme.ApplyIconStyle(_pipelineClearButton, \"清空缓存\")");
        StringAssert.Contains(MainWindowSource, "UiTheme.ApplyQuietStyle(_appearanceButton)");
    }

    [TestMethod]
    public void ExistingParameterFieldsUseSharedStylesWithoutAParallelForm()
    {
        StringAssert.Contains(MainWindowSource, "ApplyPipelineInputStyles();");
        StringAssert.Contains(MainWindowSource, "UiTheme.ApplyInputStyle(box);");
        StringAssert.Contains(MainWindowSource, "_pipelineAnchorBox");
        StringAssert.Contains(MainWindowSource, "AttachPathTooltip(_pipelineInputBox);");
        Assert.DoesNotContain("StyledPipelineForm", MainWindowSource);
    }

    [TestMethod]
    public void AppUsesPlatformFontFallbackInsteadOfInterOnlyDefault()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "GrayscaleLayersMac", "Program.cs"));
        var app = File.ReadAllText(Path.Combine(root, "GrayscaleLayersMac", "App.cs"));
        var project = File.ReadAllText(Path.Combine(
            root, "GrayscaleLayersMac", "GrayscaleLayersMac.csproj"));

        Assert.DoesNotContain("WithInterFont", program);
        Assert.DoesNotContain("Avalonia.Fonts.Inter", project);
        StringAssert.Contains(MainWindowSource, "FontFamily = UiTheme.UiFont;");
        StringAssert.Contains(
            app,
            "Resources[\"ContentControlThemeFontFamily\"] = UiTheme.UiFont;");
    }

    [TestMethod]
    public void RedesignDoesNotIntroduceAlternateWorkflowNavigation()
    {
        Assert.DoesNotContain("PipelineStepNavigator", MainWindowSource);
        Assert.DoesNotContain("InspectorCategoryTabs", MainWindowSource);
        Assert.DoesNotContain("Content = \"选择纹理图\"", MainWindowSource);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GrayscaleLayersMac.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, "GrayscaleLayersMac")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位测试仓库根目录。");
    }
}
