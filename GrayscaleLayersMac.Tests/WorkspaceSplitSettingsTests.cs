using System;
using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class WorkspaceSplitSettingsTests
{
    [TestMethod]
    public void LoadPreviewRatio_MissingFileUsesDefault()
    {
        WithSettings(settings =>
            Assert.AreEqual(
                WorkspaceSplitSettings.DefaultPreviewRatio,
                settings.LoadPreviewRatio()));
    }

    [TestMethod]
    public void SaveAndLoadPreviewRatio_RoundTrips()
    {
        WithSettings(settings =>
        {
            Assert.IsTrue(settings.TrySavePreviewRatio(0.63));
            Assert.AreEqual(0.63, settings.LoadPreviewRatio(), 0.000001);
        });
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("{\"Version\":2,\"PreviewRatio\":0.6}")]
    [DataRow("{\"Version\":1,\"PreviewRatio\":0.01}")]
    [DataRow("{\"Version\":1,\"PreviewRatio\":0.99}")]
    public void LoadPreviewRatio_InvalidSettingsUseDefault(string json)
    {
        WithSettings((settings, path) =>
        {
            File.WriteAllText(path, json);
            Assert.AreEqual(
                WorkspaceSplitSettings.DefaultPreviewRatio,
                settings.LoadPreviewRatio());
        });
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(0.01)]
    [DataRow(0.99)]
    public void SavePreviewRatio_InvalidValueIsRejected(double ratio)
    {
        WithSettings((settings, path) =>
        {
            Assert.IsFalse(settings.TrySavePreviewRatio(ratio));
            Assert.IsFalse(File.Exists(path));
        });
    }

    [TestMethod]
    public void LoadLogCollapsed_MissingFileIsExpanded()
    {
        WithSettings(settings => Assert.IsFalse(settings.LoadLogCollapsed("pipeline")));
    }

    [TestMethod]
    public void SaveAndLoadLogCollapsed_RoundTrips()
    {
        WithSettings(settings =>
        {
            Assert.IsTrue(settings.TrySaveLogCollapsed("pipeline", true));
            Assert.IsTrue(settings.LoadLogCollapsed("pipeline"));

            Assert.IsTrue(settings.TrySaveLogCollapsed("pipeline", false));
            Assert.IsFalse(settings.LoadLogCollapsed("pipeline"));
        });
    }

    [TestMethod]
    public void LogCollapsed_IsRememberedPerPanel()
    {
        WithSettings(settings =>
        {
            settings.TrySaveLogCollapsed("layer", false);
            settings.TrySaveLogCollapsed("hatch", true);
            settings.TrySaveLogCollapsed("pipeline", true);

            Assert.IsFalse(settings.LoadLogCollapsed("layer"));
            Assert.IsTrue(settings.LoadLogCollapsed("hatch"));
            Assert.IsTrue(settings.LoadLogCollapsed("pipeline"));
            Assert.IsFalse(settings.LoadLogCollapsed("unknown"));
        });
    }

    [TestMethod]
    public void SaveLogCollapsed_PreservesPreviewRatio()
    {
        WithSettings(settings =>
        {
            settings.TrySavePreviewRatio(0.63);
            settings.TrySaveLogCollapsed("hatch", true);

            Assert.AreEqual(0.63, settings.LoadPreviewRatio(), 0.000001);
            Assert.IsTrue(settings.LoadLogCollapsed("hatch"));
        });
    }

    [TestMethod]
    public void SavePreviewRatio_PreservesLogCollapsed()
    {
        WithSettings(settings =>
        {
            settings.TrySaveLogCollapsed("pipeline", true);
            settings.TrySavePreviewRatio(0.41);

            Assert.IsTrue(settings.LoadLogCollapsed("pipeline"));
            Assert.AreEqual(0.41, settings.LoadPreviewRatio(), 0.000001);
        });
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("{\"Version\":2,\"PreviewRatio\":0.6,\"LogCollapsed\":{\"a\":true}}")]
    [DataRow("{\"Version\":1,\"PreviewRatio\":0.01,\"LogCollapsed\":{\"a\":true}}")]
    public void LoadLogCollapsed_InvalidSettingsFallBackToExpanded(string json)
    {
        WithSettings((settings, path) =>
        {
            File.WriteAllText(path, json);
            Assert.IsFalse(settings.LoadLogCollapsed("a"));
        });
    }

    [TestMethod]
    public void LoadLogCollapsed_LegacyFileWithoutTheFieldIsExpanded()
    {
        WithSettings((settings, path) =>
        {
            File.WriteAllText(path, "{\"Version\":1,\"PreviewRatio\":0.58}");

            Assert.IsFalse(settings.LoadLogCollapsed("layer"));
            Assert.AreEqual(
                WorkspaceSplitSettings.DefaultPreviewRatio,
                settings.LoadPreviewRatio());
        });
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void SaveLogCollapsed_BlankKeyIsRejected(string? key)
    {
        WithSettings((settings, path) =>
        {
            Assert.IsFalse(settings.TrySaveLogCollapsed(key!, true));
            Assert.IsFalse(File.Exists(path));
        });
    }

    [TestMethod]
    public void LoadThumbnailCollapsed_MissingFileIsExpanded() => WithSettings(settings =>
        Assert.IsFalse(settings.LoadThumbnailCollapsed()));

    [TestMethod]
    public void SaveAndLoadThumbnailCollapsed_RoundTrips() => WithSettings(settings =>
    {
        Assert.IsTrue(settings.TrySaveThumbnailCollapsed(true));
        Assert.IsTrue(settings.LoadThumbnailCollapsed());

        Assert.IsTrue(settings.TrySaveThumbnailCollapsed(false));
        Assert.IsFalse(settings.LoadThumbnailCollapsed());
    });

    [TestMethod]
    public void SaveThumbnailCollapsed_PreservesLogCollapsed() => WithSettings((settings, path) =>
    {
        Assert.IsTrue(settings.TrySaveLogCollapsed("layer", true));
        Assert.IsTrue(settings.TrySaveThumbnailCollapsed(true));

        var reloaded = new WorkspaceSplitSettings(path);
        Assert.IsTrue(reloaded.LoadLogCollapsed("layer"));
        Assert.IsTrue(reloaded.LoadThumbnailCollapsed());
    });

    [TestMethod]
    public void SaveThumbnailCollapsed_PreservesPreviewRatio() => WithSettings((settings, path) =>
    {
        Assert.IsTrue(settings.TrySavePreviewRatio(0.7));
        Assert.IsTrue(settings.TrySaveThumbnailCollapsed(true));

        var reloaded = new WorkspaceSplitSettings(path);
        Assert.AreEqual(0.7, reloaded.LoadPreviewRatio(), 1e-9);
        Assert.IsTrue(reloaded.LoadThumbnailCollapsed());
    });

    [TestMethod]
    public void LoadThumbnailCollapsed_LegacyFileWithoutTheFieldIsExpanded() => WithSettings(
        (settings, path) =>
        {
            File.WriteAllText(path,
                "{\"Version\":1,\"PreviewRatio\":0.6,\"LogCollapsed\":{\"layer\":true}}");
            var reloaded = new WorkspaceSplitSettings(path);

            Assert.IsTrue(reloaded.LoadLogCollapsed("layer"));
            Assert.IsFalse(reloaded.LoadThumbnailCollapsed(),
                "旧版本文件无 ThumbnailCollapsed 字段时应按展开处理");
        });

    [TestMethod]
    public void LoadThumbnailCollapsed_InvalidJsonFallsBackToExpanded() => WithSettings(
        (settings, path) =>
        {
            File.WriteAllText(path, "{ not valid json");
            var reloaded = new WorkspaceSplitSettings(path);

            Assert.IsFalse(reloaded.LoadThumbnailCollapsed());
        });

    private static void WithSettings(Action<WorkspaceSplitSettings> action) =>
        WithSettings((settings, _) => action(settings));

    private static void WithSettings(Action<WorkspaceSplitSettings, string> action)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"workspace-split-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            action(new WorkspaceSplitSettings(path), path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
