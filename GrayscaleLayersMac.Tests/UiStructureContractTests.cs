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
    public void TopImportEntriesShareOneOverlayAnchoredToTheHeaderImportButton()
    {
        Assert.AreEqual(
            1,
            CountOccurrences(
                MainWindowSource,
                "new ImportProgressOverlay(_pipelineImportButton)"));
        StringAssert.Contains(
            MainWindowSource,
            "_pipelineImportProgress = new ImportProgressOverlay(_pipelineImportButton);");
        StringAssert.Contains(MainWindowSource, "var root = new Grid");
        StringAssert.Contains(MainWindowSource, "_pipelineImportProgress.Root");
    }

    [TestMethod]
    public void TopImportMethodsDoNotCreateASeparateProgressWindow()
    {
        var directoryImport = MethodSource(
            "private async Task ImportPipelineDirectoryAsync()",
            "private async Task ImportPipelineFilesAsync()");
        var fileImport = MethodSource(
            "private async Task ImportPipelineFilesAsync()",
            "internal static async Task<bool> RunPreparedImportAsync(");

        Assert.DoesNotContain("ProcessingProgressWindow", directoryImport);
        Assert.DoesNotContain("ProcessingProgressWindow", fileImport);
    }

    [TestMethod]
    public void DxfCommitPreparationIsDetachedAndPublicationEndsOnDxf()
    {
        var prepare = MethodSource(
            "private Action CreatePipelineDxfCommit(",
            "private void CommitPipelineDxfImports(");
        foreach (var liveField in new[]
        {
            "_pipelineDxfHost",
            "_pipelineDxfPreview",
            "_pipelineSharedPreview",
            "_pipelineDxfFiles"
        })
            Assert.DoesNotContain(liveField, prepare);

        var publish = MethodSource(
            "private void CommitPipelineDxfImports(",
            "private static string? DirectoryOf(");
        StringAssert.Contains(publish, "InstallPreparedFile(");
        StringAssert.Contains(publish, "ReplaceItemsWithLoadedSelection(");
        StringAssert.Contains(
            publish,
            "SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Dxf)");

        var coordinator = MethodSource(
            "internal static async Task<bool> RunPreparedImportAsync(",
            "private PreparedImportFlowActions CreatePreparedImportFlowActions()");
        Assert.IsGreaterThan(
            coordinator.IndexOf("actions.CommitTiffs", StringComparison.Ordinal),
            coordinator.IndexOf("commitDxfs?.Invoke", StringComparison.Ordinal));
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
    public void CustomPreviewCanvasesUseTheCjkCapableUiTypeface()
    {
        var root = FindRepositoryRoot();
        foreach (var file in new[]
        {
            "DxfPreviewControl.cs",
            "DxfLayerRailCanvas.cs",
            "GrayscaleLayerPreviewCanvas.cs",
            "GrayscaleLayerThumbnailCanvas.cs"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, "GrayscaleLayersMac", file));
            Assert.DoesNotContain("Typeface.Default", source, file);
            StringAssert.Contains(source, "UiTheme.UiTypeface", file);
        }

        var host = File.ReadAllText(Path.Combine(
            root, "GrayscaleLayersMac", "DxfPreviewHost.cs"));
        StringAssert.Contains(host, "status.FontFamily = UiTheme.UiFont;");
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

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string MethodSource(string startMarker, string endMarker)
    {
        var start = MainWindowSource.IndexOf(startMarker, StringComparison.Ordinal);
        var end = MainWindowSource.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, startMarker);
        Assert.IsGreaterThan(start, end, endMarker);
        return MainWindowSource[start..end];
    }
}
