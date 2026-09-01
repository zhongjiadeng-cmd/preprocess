using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PmtWorkflowRequestSerializerTests
{
    [TestMethod]
    public void SerializesMultiSourceTargetsAndScalingAsVersionThree()
    {
        using var fixture = PmtSourceTestFixture.CreateTwoSources();
        var session = PmtDraftSession.Create(
            fixture.Catalog,
            new LaserPmtWorkflowBounds(0, 0, 200, 120),
            0.1,
            "batch-a");
        session.CommitMatrix(1, 2);
        var secondSource = fixture.Catalog.Sources[1];
        var targetId = session.Snapshot.Workflow.Targets.OfType<LaserPmtTarget>().Last().Id;
        var workflow = LaserPmtWorkflowEditor.AssignPmtSource(
            session.Snapshot.Workflow, [targetId], secondSource.Id);
        workflow = LaserPmtWorkflowEditor.SetPmtSizeLock(workflow, targetId, false, restoreNativeSize: false);
        workflow = LaserPmtWorkflowEditor.ResizePmt(workflow, targetId, 30, 12);
        session.ApplyWorkflow(workflow);

        var json = PmtWorkflowRequestSerializer.Serialize(session.Snapshot, fixture.Root, "owner-token");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(3, root.GetProperty("request_version").GetInt32());
        Assert.AreEqual(2, root.GetProperty("sources").GetArrayLength());
        var compiled = root.GetProperty("workflow").GetProperty("compiled_targets")[1];
        Assert.AreEqual(secondSource.Id, compiled.GetProperty("source_id").GetString());
        Assert.AreEqual(30d / secondSource.NativeWidth, compiled.GetProperty("scale_x").GetDouble(), 1e-9);
        Assert.AreEqual(12d / secondSource.NativeHeight, compiled.GetProperty("scale_y").GetDouble(), 1e-9);
    }

    [TestMethod]
    public void RefusesToSaveTransientMatrixPreview()
    {
        using var fixture = PmtSourceTestFixture.CreateTwoSources();
        var session = PmtDraftSession.Create(
            fixture.Catalog,
            new LaserPmtWorkflowBounds(0, 0, 200, 120),
            0.1,
            "batch-a");
        session.PreviewMatrix(2, 2);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PmtWorkflowRequestSerializer.Serialize(session.Snapshot, fixture.Root, "owner-token"));
    }

    private sealed class PmtSourceTestFixture : IDisposable
    {
        public string Root { get; }
        public PmtSourceCatalog Catalog { get; }

        private PmtSourceTestFixture(string root, PmtSourceCatalog catalog)
        {
            Root = root;
            Catalog = catalog;
        }

        public static PmtSourceTestFixture CreateTwoSources()
        {
            var root = Path.Combine(Path.GetTempPath(), $"pmt-request-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var first = CreatePackage(root, "source-a");
            var second = CreatePackage(root, "source-b");
            var catalog = PmtSourceCatalog.Empty.Import(
            [
                Candidate(first, 20, 10),
                Candidate(second, 12, 6)
            ]).Catalog;
            return new PmtSourceTestFixture(root, catalog);
        }

        private static string CreatePackage(string root, string name)
        {
            var directory = Path.Combine(root, name);
            Directory.CreateDirectory(Path.Combine(directory, "patches"));
            File.WriteAllText(Path.Combine(directory, "machine.json"), "{}");
            File.WriteAllBytes(Path.Combine(directory, "patches", "0_0.npy"), [1, 2, 3]);
            return directory;
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

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
