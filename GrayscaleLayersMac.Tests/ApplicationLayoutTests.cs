using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class ApplicationLayoutTests
{
    [TestMethod]
    public void DevelopmentLayoutUsesBaseDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "grayscale-layout", "publish");

        var actual = ApplicationLayout.GetScriptsDirectory(baseDirectory);

        Assert.AreEqual(Path.GetFullPath(baseDirectory), actual);
    }

    [TestMethod]
    public void AppBundleUsesResourcesScriptsDirectory()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(), "灰度图分层工具.app", "Contents", "MacOS");
        var expected = Path.Combine(
            Path.GetTempPath(), "灰度图分层工具.app", "Contents", "Resources", "scripts");

        var actual = ApplicationLayout.GetScriptsDirectory(baseDirectory + Path.DirectorySeparatorChar);

        Assert.AreEqual(Path.GetFullPath(expected), actual);
    }

    [TestMethod]
    public void UnbundledDirectoryNamedMacOSDoesNotUseSiblingResources()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "plain", "Contents", "MacOS");

        var actual = ApplicationLayout.GetScriptsDirectory(baseDirectory);

        Assert.AreEqual(Path.GetFullPath(baseDirectory), actual);
    }

    [TestMethod]
    public void ScriptPathUsesResolvedDirectory()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(), "灰度图分层工具.app", "Contents", "MacOS");

        var actual = ApplicationLayout.GetScriptPath(baseDirectory, "grayscale_layers.py");

        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(
                baseDirectory, "..", "Resources", "scripts", "grayscale_layers.py")),
            actual);
    }
}
