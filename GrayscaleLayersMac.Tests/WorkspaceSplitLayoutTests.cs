using Avalonia;
using Avalonia.Controls;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class WorkspaceSplitLayoutTests
{
    [TestMethod]
    public void AssembleWorkspaceGrid_ParentsSplitterDirectlyToResizableGrid()
    {
        var previewColumn = new ColumnDefinition(new GridLength(0.58, GridUnitType.Star));
        var inspectorColumn = new ColumnDefinition(new GridLength(0.42, GridUnitType.Star));
        var splitter = new GridSplitter();

        var workspace = MainWindow.AssembleWorkspaceGrid(
            previewColumn,
            inspectorColumn,
            new Border(),
            new Border(),
            splitter,
            new Border());

        Assert.AreSame(workspace, splitter.Parent);
        Assert.AreEqual(1, Grid.GetColumn(splitter));
        Assert.AreEqual(2, Grid.GetRowSpan(splitter));
        Assert.AreEqual(3, workspace.ColumnDefinitions.Count);
        Assert.AreSame(previewColumn, workspace.ColumnDefinitions[0]);
        Assert.AreSame(inspectorColumn, workspace.ColumnDefinitions[2]);
        Assert.AreEqual(WorkspacePanelLayout.SplitterWidth, workspace.ColumnDefinitions[1].Width.Value);
    }

    [TestMethod]
    public void WorkspaceColumns_KeepBothPanelsReachableAtExtremeRatiosAndAfterWindowResize()
    {
        foreach (var previewRatio in new[] { .01, .5, .99 })
        {
            var workspace = new Grid
            {
                ColumnDefinitions = WorkspacePanelLayout.Columns(320, 300, previewRatio),
                Children = { new Border() }
            };
            foreach (var width in new[] { 1920d, 1080d })
            {
                workspace.Measure(new Size(width, 700));
                workspace.Arrange(new Rect(0, 0, width, 700));

                var columns = workspace.ColumnDefinitions;
                Assert.IsTrue(columns[0].ActualWidth >= 320, $"工作区过窄：{previewRatio}, {width}");
                Assert.IsTrue(columns[2].ActualWidth >= 300, $"参数区过窄：{previewRatio}, {width}");
                Assert.AreEqual(width, columns[0].ActualWidth + columns[1].ActualWidth + columns[2].ActualWidth, .01);
            }
        }
    }
}
