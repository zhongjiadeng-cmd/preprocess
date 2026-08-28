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
    }
}
