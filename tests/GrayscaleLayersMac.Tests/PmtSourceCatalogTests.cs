using System;
using System.IO;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PmtSourceCatalogTests
{
    [TestMethod]
    public void Import_AcceptsValidSourcesAndReportsInvalidOnes()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.CreatePackage("group-a/shared");
        var second = workspace.CreatePackage("group-b/shared");
        var invalid = workspace.CreatePackage("invalid", includePatches: false);

        var result = PmtSourceCatalog.Empty.Import(
        [
            Candidate(first, 4, 2),
            Candidate(invalid, 8, 3),
            Candidate(second, 6, 5)
        ]);

        Assert.AreEqual(2, result.Catalog.Sources.Count);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual(Path.GetFullPath(invalid), result.Errors[0].Directory);
        Assert.AreNotEqual(result.Catalog.Sources[0].Id, result.Catalog.Sources[1].Id);
        Assert.AreNotEqual(result.Catalog.Sources[0].Mark, result.Catalog.Sources[1].Mark);
        Assert.AreEqual("shared", result.Catalog.Sources[0].DisplayName);
        Assert.IsNotNull(result.Catalog.ActiveSource);
    }

    [TestMethod]
    public void HasChanged_DetectsChangedPackageContent()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreatePackage("source");
        var result = PmtSourceCatalog.Empty.Import([Candidate(directory, 4, 2)]);
        var source = result.Catalog.Sources.Single();

        Assert.IsFalse(result.Catalog.HasChanged(source.Id));
        File.AppendAllText(Path.Combine(directory, "machine.json"), "changed");
        Assert.IsTrue(result.Catalog.HasChanged(source.Id));
    }

    [TestMethod]
    public void Relocate_PreservesStableIdentityAndVisualMark()
    {
        using var workspace = new TestWorkspace();
        var original = workspace.CreatePackage("original");
        var relocated = workspace.CreatePackage("relocated");
        var imported = PmtSourceCatalog.Empty.Import([Candidate(original, 4, 2)]).Catalog;
        var before = imported.Sources.Single();

        var updated = imported.Relocate(before.Id, Candidate(relocated, 9, 7));
        var after = updated.Sources.Single();

        Assert.AreEqual(before.Id, after.Id);
        Assert.AreEqual(before.Mark, after.Mark);
        Assert.AreEqual(before.ColorArgb, after.ColorArgb);
        Assert.AreEqual(Path.GetFullPath(relocated), after.Directory);
        Assert.AreEqual(9d, after.NativeWidth);
        Assert.AreEqual(7d, after.NativeHeight);
    }

    private static PmtSourceCandidate Candidate(string directory, double width, double height) =>
        new(directory, new LaserPmtBaseMetadata(
            Path.GetFullPath(directory),
            width,
            height,
            LaserPmtConfiguration.Parameters.ToDictionary(
                definition => definition.Name,
                definition => definition.IsBoolean ? "false" : "1",
                StringComparer.Ordinal)));

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), $"pmt-source-catalog-{Guid.NewGuid():N}");

        public string CreatePackage(string relative, bool includePatches = true)
        {
            var directory = Path.Combine(_root, relative);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "machine.json"), "{}");
            if (includePatches)
            {
                var patches = Path.Combine(directory, "patches");
                Directory.CreateDirectory(patches);
                File.WriteAllBytes(Path.Combine(patches, "0_0.npy"), [1, 2, 3]);
            }
            return directory;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
