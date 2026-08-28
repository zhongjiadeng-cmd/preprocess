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
