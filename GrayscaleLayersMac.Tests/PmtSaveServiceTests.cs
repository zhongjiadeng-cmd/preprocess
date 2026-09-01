using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PmtSaveServiceTests
{
    [TestMethod]
    public async Task MarksExactlyTheGeneratedRevisionAsSaved()
    {
        using var fixture = new Fixture();
        var generator = new FakeGenerator(fixture.Output, succeed: true);
        var service = new PmtSaveService(generator);
        var session = fixture.CreateSession();
        session.CommitMatrix(1, 1);
        var savingRevision = session.Snapshot.CurrentRevision;

        var result = await service.SaveAsync(session, fixture.Root);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(savingRevision, session.Snapshot.SavedRevision);
        StringAssert.Contains(generator.Request!, "\"request_version\": 3");
    }

    [TestMethod]
    public async Task FailedGenerationLeavesDraftDirty()
    {
        using var fixture = new Fixture();
        var service = new PmtSaveService(new FakeGenerator(fixture.Output, succeed: false));
        var session = fixture.CreateSession();
        session.CommitMatrix(1, 1);

        var result = await service.SaveAsync(session, fixture.Root);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(session.Snapshot.IsDirty);
        Assert.AreEqual(0L, session.Snapshot.SavedRevision);
    }

    private sealed class FakeGenerator(string output, bool succeed) : IPmtPackageGenerator
    {
        public string? Request { get; private set; }

        public Task<string> GenerateAsync(string requestJson, CancellationToken cancellationToken)
        {
            Request = requestJson;
            if (!succeed)
                throw new InvalidOperationException("fake failure");
            Directory.CreateDirectory(output);
            return Task.FromResult(output);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"pmt-save-{Guid.NewGuid():N}");
        public string Output => Path.Combine(Root, "saved-pmt");
        private readonly PmtSourceCatalog _catalog;

        public Fixture()
        {
            var source = Path.Combine(Root, "source");
            Directory.CreateDirectory(Path.Combine(source, "patches"));
            File.WriteAllText(Path.Combine(source, "machine.json"), "{}");
            File.WriteAllBytes(Path.Combine(source, "patches", "0_0.npy"), [1, 2, 3]);
            var metadata = new LaserPmtBaseMetadata(
                source,
                20,
                10,
                LaserPmtConfiguration.Parameters.ToDictionary(
                    definition => definition.Name,
                    definition => definition.IsBoolean ? "false" : "1",
                    StringComparer.Ordinal));
            _catalog = PmtSourceCatalog.Empty.Import([new PmtSourceCandidate(source, metadata)]).Catalog;
        }

        public PmtDraftSession CreateSession() => PmtDraftSession.Create(
            _catalog,
            new LaserPmtWorkflowBounds(0, 0, 100, 80),
            0.1,
            "saved-pmt");

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
