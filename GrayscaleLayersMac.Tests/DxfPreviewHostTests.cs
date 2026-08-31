using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

/// <summary>
/// DXF 预览宿主的选层行为。宿主只做「呈现 + 选层」，真正读文件由 LoadLayer 注入，
/// 所以这里用一个记账用的假加载器就能覆盖导航语义。
/// </summary>
[TestClass]
public sealed class DxfPreviewHostTests
{
    [TestMethod]
    public void SelectIndexLoadsTheLayerOnce()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        var loaded = new List<DxfLayerPreviewItem>();
        host.LoadLayer = item =>
        {
            loaded.Add(item);
            return true;
        };
        var items = MakeItems(3);
        host.SetItems(items);

        Assert.IsTrue(host.SelectIndex(1));

        Assert.AreEqual(1, host.SelectedIndex);
        CollectionAssert.AreEqual(new[] { items[1] }, loaded);
    }

    [TestMethod]
    public void SelectIndexIgnoresOutOfRangeAndRepeatedSelection()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        var loads = 0;
        host.LoadLayer = _ =>
        {
            loads++;
            return true;
        };
        host.SetItems(MakeItems(2));

        Assert.IsFalse(host.SelectIndex(-1));
        Assert.IsFalse(host.SelectIndex(2));
        Assert.AreEqual(0, loads);

        Assert.IsTrue(host.SelectIndex(0));
        Assert.IsFalse(host.SelectIndex(0), "重复选中同一层不应再读一次文件");
        Assert.AreEqual(1, loads);
    }

    [TestMethod]
    public void FailedLoadKeepsThePreviousSelection()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        var items = MakeItems(3);
        host.LoadLayer = item => item != items[2];
        host.SetItems(items);
        host.SelectIndex(0);

        Assert.IsFalse(host.SelectIndex(2), "加载失败的层不应被选中");
        Assert.AreEqual(0, host.SelectedIndex,
            "侧栏高亮要留在还能看的那一层上");
    }

    [TestMethod]
    public void AdoptingLoadedSelectionDoesNotInvokeLayerLoader()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        host.LoadLayer = _ => throw new InvalidOperationException(
            "已在画布安装的层不应再次调用 loader。");
        var items = MakeItems(1);

        host.ReplaceItemsWithLoadedSelection(items, 0);

        Assert.AreEqual(0, host.SelectedIndex);
        Assert.AreSame(items[0], host.Items[0]);
    }

    [TestMethod]
    public void ReplacingTheSameMutableListTwiceAdoptsTheNewFirstLayer()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        host.LoadLayer = _ => throw new InvalidOperationException(
            "已安装的 replacement 不应再次调用 loader。");
        var items = MakeItems(1, "first").ToList();
        host.ReplaceItemsWithLoadedSelection(items, 0);
        var replacement = MakeItems(1, "second")[0];
        items.Clear();
        items.Add(replacement);

        host.ReplaceItemsWithLoadedSelection(items, 0);

        Assert.AreEqual(0, host.SelectedIndex);
        Assert.AreSame(replacement, host.Items[0]);
    }

    [TestMethod]
    public void SetItemsKeepsThePreviouslySelectedItem()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        host.LoadLayer = _ => true;
        var first = MakeItems(3);
        host.SetItems(first);
        host.SelectIndex(2);

        var second = new[] { first[0], first[2], first[1] };
        host.SetItems(second);

        Assert.AreEqual(1, host.SelectedIndex, "原来选中的那一层在新列表里仍应保持选中");
        Assert.AreSame(first[2], host.Items[host.SelectedIndex]);
    }

    [TestMethod]
    public void SetItemsDropsSelectionWhenTheItemIsGone()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        host.LoadLayer = _ => true;
        host.SetItems(MakeItems(3, "first"));
        host.SelectIndex(1);

        host.SetItems(MakeItems(2, "second"));

        Assert.AreEqual(-1, host.SelectedIndex, "整批换层（重新生成）后没有可对应的层，应回到未选中");
    }

    [TestMethod]
    public void SetItemsMatchesLayersByValueSoEquivalentRelaysKeepSelection()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        host.LoadLayer = _ => true;
        host.SetItems(MakeItems(3, "run"));
        host.SelectIndex(2);

        // 重新构造出来的等价层（同名同路径）应被认作同一层，选中态得以延续。
        host.SetItems(MakeItems(3, "run"));

        Assert.AreEqual(2, host.SelectedIndex);
    }

    [TestMethod]
    public void ClearSelectionKeepsTheList()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        host.LoadLayer = _ => true;
        host.SetItems(MakeItems(3));
        host.SelectIndex(1);

        host.ClearSelection();

        Assert.AreEqual(-1, host.SelectedIndex);
        Assert.AreEqual(3, host.Items.Count, "清空缓存只清选中态，列表本身由调用方清");
    }

    [TestMethod]
    public void SelectItemRejectsUnknownItems()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        host.LoadLayer = _ => true;
        var items = MakeItems(2);
        host.SetItems(items);

        Assert.IsFalse(host.SelectItem(DxfLayerPreviewItem.Imported("/tmp/other.dxf")));
        Assert.IsTrue(host.SelectItem(items[1]));
    }

    [TestMethod]
    public void DefaultHostKeepsTheViewAcrossLayers()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());

        Assert.IsTrue(host.KeepView, "逐层对照是默认用法，切层保持视图应默认开启");
    }

    [TestMethod]
    public void EmptyHostHidesTheRailHandle()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());

        Assert.IsFalse(host.IsRailCollapsed, "没有层可列时把手不显示，也就谈不上收起");

        host.SetItems(MakeItems(3));
        host.SetRailCollapsed(true);
        Assert.IsTrue(host.IsRailCollapsed);
    }

    [TestMethod]
    public void ToolbarSmallActionsUseNamedFluentIconButtons()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());
        var buttons = host.ViewportTools.GetLogicalDescendants()
            .Concat(host.ContextTools.GetLogicalDescendants())
            .OfType<Button>()
            .ToArray();
        var names = buttons.Select(AutomationProperties.GetName).Where(name => name is not null).ToArray();

        CollectionAssert.IsSubsetOf(
            new[] { "上一层", "下一层", "缩小", "放大", "适应窗口", "实际尺寸" },
            names!);
        Assert.IsTrue(buttons
            .Where(button => names.Contains(AutomationProperties.GetName(button)))
            .All(button => UiIcons.IsFluentIconControl(button.Content)));
    }

    [TestMethod]
    public void ViewportAndContextToolsAreSeparatedByPurpose()
    {
        using var preview = new DxfPreviewControl();
        var host = new DxfPreviewHost(preview, new TextBlock());

        var viewportNames = host.ViewportTools.GetLogicalDescendants()
            .OfType<Button>()
            .Select(AutomationProperties.GetName)
            .Where(name => name is not null)
            .ToArray();
        var contextNames = host.ContextTools.GetLogicalDescendants()
            .OfType<Button>()
            .Select(AutomationProperties.GetName)
            .Where(name => name is not null)
            .ToArray();

        CollectionAssert.IsSubsetOf(
            new[] { "缩小", "放大", "适应窗口", "实际尺寸" },
            viewportNames!);
        CollectionAssert.IsSubsetOf(
            new[] { "上一层", "下一层" },
            contextNames!);
        Assert.DoesNotContain("上一层", viewportNames!);
        Assert.DoesNotContain("缩小", contextNames!);
    }

    private static DxfLayerPreviewItem[] MakeItems(int count, string tag = "layer") =>
        Enumerable.Range(0, count)
            .Select(index => new DxfLayerPreviewItem(
                $"{tag} 第 {index + 1:D2} 层",
                $"/tmp/{tag}_{index:D2}.dxf",
                null,
                null))
            .ToArray();
}
