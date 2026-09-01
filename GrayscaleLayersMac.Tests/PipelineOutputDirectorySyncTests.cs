using System.IO;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PipelineOutputDirectorySyncTests
{
    private static readonly string OutDirectory = Path.Combine(Path.GetTempPath(), "out");
    private static readonly string DxfDirectory = Path.Combine(Path.GetTempPath(), "dxf");

    [TestMethod]
    public void ShouldFollow_EmptyDxfDirectoryFollowsLayerDirectory()
    {
        Assert.IsTrue(PipelineOutputDirectorySync.ShouldFollowLayerDirectory(null, null));
        Assert.IsTrue(PipelineOutputDirectorySync.ShouldFollowLayerDirectory("   ", null));
        Assert.IsTrue(PipelineOutputDirectorySync.ShouldFollowLayerDirectory(null, OutDirectory));
    }

    [TestMethod]
    public void ShouldFollow_DxfDirectoryMatchingLastSyncedPathFollows()
    {
        Assert.IsTrue(
            PipelineOutputDirectorySync.ShouldFollowLayerDirectory(OutDirectory, OutDirectory));
    }

    [TestMethod]
    public void ShouldFollow_NeverSyncedDxfDirectoryStaysUntouched()
    {
        Assert.IsFalse(PipelineOutputDirectorySync.ShouldFollowLayerDirectory(DxfDirectory, null));
    }

    [TestMethod]
    public void ShouldFollow_UserEditedDxfDirectoryStaysUntouched()
    {
        Assert.IsFalse(
            PipelineOutputDirectorySync.ShouldFollowLayerDirectory(DxfDirectory, OutDirectory));
    }

    [TestMethod]
    public void PathsEqual_IgnoresTrailingSeparatorAndCase()
    {
        var withSeparator = OutDirectory + Path.DirectorySeparatorChar;
        Assert.IsTrue(PipelineOutputDirectorySync.PathsEqual(withSeparator, OutDirectory));
        Assert.IsTrue(
            PipelineOutputDirectorySync.PathsEqual(
                OutDirectory,
                OutDirectory.ToUpperInvariant()));
    }

    [TestMethod]
    public void PathsEqual_ResolvesRelativeSegments()
    {
        var nested = Path.Combine(OutDirectory, "nested", "..");
        Assert.IsTrue(PipelineOutputDirectorySync.PathsEqual(nested, OutDirectory));
    }

    [TestMethod]
    public void PathsEqual_DifferentDirectoriesAreNotEqual()
    {
        Assert.IsFalse(PipelineOutputDirectorySync.PathsEqual(OutDirectory, DxfDirectory));
    }

    [TestMethod]
    public void PathsEqual_EmptyPathNeverMatches()
    {
        Assert.IsFalse(PipelineOutputDirectorySync.PathsEqual(null, null));
        Assert.IsFalse(PipelineOutputDirectorySync.PathsEqual("  ", "  "));
        Assert.IsFalse(PipelineOutputDirectorySync.PathsEqual(OutDirectory, null));
    }
}
