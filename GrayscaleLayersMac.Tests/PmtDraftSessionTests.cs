using System;
using System.IO;
using System.Linq;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PmtDraftSessionTests
{
    [TestMethod]
    public void MatrixPreviewIsTransientAndClickCommitCreatesDirtyRowMajorMatrix()
    {
        using var fixture = new SourceFixture();
        var session = fixture.CreateSession();

        session.PreviewMatrix(2, 3);
        Assert.IsFalse(session.Snapshot.IsDirty);
        Assert.AreEqual(6, session.Snapshot.DisplayWorkflow.Targets.Count);
        Assert.AreEqual(0, session.Snapshot.Workflow.Targets.Count);

        session.CommitMatrix(2, 3);
        Assert.IsTrue(session.Snapshot.IsDirty);
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 5, 6 },
            session.Snapshot.Workflow.Targets.OfType<LaserPmtTarget>()
                .Select(target => target.Number).ToArray());
    }

    [TestMethod]
    public void DeleteLeavesNumberHoleUntilExplicitRenumber()
    {
        using var fixture = new SourceFixture();
        var session = fixture.CreateSession();
        session.CommitMatrix(1, 3);
        var middle = session.Snapshot.Workflow.Targets.OfType<LaserPmtTarget>()
            .Single(target => target.Number == 2);

        session.SelectSingle(middle.Id);
        session.DeleteSelected();
        CollectionAssert.AreEqual(
            new[] { 1, 3 },
            session.Snapshot.Workflow.Targets.OfType<LaserPmtTarget>()
                .Select(target => target.Number).ToArray());

        session.RenumberByPosition();
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            session.Snapshot.Workflow.Targets.OfType<LaserPmtTarget>()
                .Select(target => target.Number).ToArray());
    }

    [TestMethod]
    public void ArrowSelectsSpatialNeighbourAndNudgeMovesSelection()
    {
        using var fixture = new SourceFixture();
        var session = fixture.CreateSession();
        session.CommitMatrix(2, 2);
        var ordered = session.Snapshot.Workflow.Targets.OfType<LaserPmtTarget>()
            .OrderBy(target => target.Number).ToArray();
        session.SelectSingle(ordered[0].Id);

        session.SelectInDirection(PmtNavigationDirection.Right);
        Assert.AreEqual(ordered[1].Id, session.Snapshot.PrimaryTargetId);
        var before = ordered[1].Bounds.Left;
        session.NudgeSelected(PmtNavigationDirection.Right, 0.1);
        var after = session.Snapshot.Workflow.Targets.OfType<LaserPmtTarget>()
            .Single(target => target.Id == ordered[1].Id).Bounds.Left;
        Assert.AreEqual(before + 0.1, after, 1e-9);
    }

    [TestMethod]
    public void SavingOlderRevisionDoesNotClearLaterEdits()
    {
        using var fixture = new SourceFixture();
        var session = fixture.CreateSession();
        session.CommitMatrix(1, 1);
        var savingRevision = session.Snapshot.CurrentRevision;
        session.SetOutputName("later-name");

        session.MarkSaved(savingRevision);

        Assert.IsTrue(session.Snapshot.IsDirty);
        Assert.AreEqual(savingRevision, session.Snapshot.SavedRevision);
        Assert.IsGreaterThan(savingRevision, session.Snapshot.CurrentRevision);
    }

    private sealed class SourceFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), $"pmt-draft-{Guid.NewGuid():N}");

        public PmtDraftSession CreateSession()
        {
            Directory.CreateDirectory(Path.Combine(_root, "patches"));
            File.WriteAllText(Path.Combine(_root, "machine.json"), "{}");
            File.WriteAllBytes(Path.Combine(_root, "patches", "0_0.npy"), [1]);
            var metadata = new LaserPmtBaseMetadata(
                Path.GetFullPath(_root),
                10,
                5,
                LaserPmtConfiguration.Parameters.ToDictionary(
                    definition => definition.Name,
                    definition => definition.IsBoolean ? "false" : "1",
                    StringComparer.Ordinal));
            var catalog = PmtSourceCatalog.Empty.Import([new(_root, metadata)]).Catalog;
            return PmtDraftSession.Create(catalog, new(0, 0, 100, 80), 0.1);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
