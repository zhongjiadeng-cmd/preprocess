using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class ProcessingWorkflowTests
{
    [TestMethod]
    public void ConnectionsCarryIndependentStableSelectionsAndRejectWrongTypes()
    {
        var session = new ProcessingSession();
        var source = session.Add(ProcessingNodeKind.Grayscale, 0, 0);
        var first = session.Add(ProcessingNodeKind.Hatch, 300, 0);
        var second = session.Add(ProcessingNodeKind.Hatch, 300, 300);
        session.Connect(source.Id, first.Id, ["b", "a"]);
        session.Connect(source.Id, second.Id, null);
        var layers = new[] { new ProcessingLayer("a", "A", "a.tif", 1), new ProcessingLayer("b", "B", "b.tif", 2), new ProcessingLayer("c", "C", "c.tif", 3) };
        CollectionAssert.AreEqual(new[] { "a", "b" }, ProcessingDocument.SelectLayers(layers, session.Document.Connections[0].LayerIds).Select(l => l.Id).ToArray());
        Assert.AreEqual(3, ProcessingDocument.SelectLayers(layers, session.Document.Connections[1].LayerIds).Length);
        Assert.Throws<InvalidOperationException>(() => ProcessingDocument.SelectLayers(layers, ["gone"]));
        Assert.Throws<InvalidOperationException>(() => session.Connect(source.Id, first.Id, null));
        Assert.Throws<InvalidDataException>(() => session.Connect(first.Id, source.Id, null));
        Assert.AreEqual(2, session.Document.Connections.Length);
    }

    [TestMethod]
    public void HatchCreatesVisibleMachineAndSinglePmtAsOneUndoableEdit()
    {
        var session = new ProcessingSession(); var hatch = session.Add(ProcessingNodeKind.Hatch, 0, 0);
        var pmt = session.CreatePmt(hatch.Id, ["layer-2"], 12, 15);
        Assert.AreEqual(3, session.Document.Nodes.Length);
        Assert.AreEqual(2, session.Document.Connections.Length);
        Assert.AreEqual(12d, pmt.Instances[0].Width);
        CollectionAssert.AreEqual(new[] { "layer-2" }, session.Document.Connections[0].LayerIds!);
        session.Undo(); Assert.AreEqual(1, session.Document.Nodes.Length);
        session.Redo(); Assert.AreEqual(3, session.Document.Nodes.Length);
    }

    [TestMethod]
    public void MatrixInheritsSelectedTemplateAndPreservesOtherWorkpieces()
    {
        var session = new ProcessingSession(); var hatch = session.Add(ProcessingNodeKind.Hatch, 0, 0);
        var pmt = session.CreatePmt(hatch.Id, ["layer-2"], 12, 15);
        session.UpdateNode(pmt with { Instances = [pmt.Instances[0] with { Overrides = new() { ["power"] = "25" } }] });
        var matrix = session.Matrix(pmt.Id, pmt.Instances[0].Id, 2, 3, 2, 45, 40);
        Assert.AreEqual(6, matrix.Instances.Length);
        Assert.AreEqual(1, session.Document.Node(pmt.Id).Instances.Length);
        Assert.IsTrue(matrix.Instances.All(i => i.Width == 12 && i.Height == 15 && i.Overrides["power"] == "25"));
        Assert.AreEqual(session.Document.Connections.Single(c => c.TargetId == pmt.Id).SourceId,
            session.Document.Connections.Single(c => c.TargetId == matrix.Id).SourceId);
        Assert.AreEqual(7, session.Document.Nodes.SelectMany(n => n.Instances).Select(i => i.Number).Distinct().Count());
        session.Undo(); Assert.IsFalse(session.Document.Nodes.Any(n => n.Id == matrix.Id));
        session.Redo(); Assert.AreEqual(6, session.Document.Node(matrix.Id).Instances.Length);
        Assert.Throws<InvalidOperationException>(() => session.Matrix(pmt.Id, pmt.Instances[0].Id, 2, 3, 2, 10, 10));
    }

    [TestMethod]
    public void PasteRemapsInternalLinksAndKeepsExternalMachineReference()
    {
        var session = new ProcessingSession(); var hatch = session.Add(ProcessingNodeKind.Hatch, 0, 0);
        var pmt = session.CreatePmt(hatch.Id, ["layer-2"], 12, 15);
        session.Select(pmt.Id); var text = session.Copy(); session.Paste(text);
        var clone = session.Document.Node(session.Selection.Single());
        Assert.AreNotEqual(pmt.Id, clone.Id);
        Assert.AreNotEqual(pmt.Instances[0].Id, clone.Instances[0].Id);
        Assert.AreNotEqual(pmt.Instances[0].Number, clone.Instances[0].Number);
        Assert.AreEqual(session.Document.Connections.Single(c => c.TargetId == pmt.Id).SourceId,
            session.Document.Connections.Single(c => c.TargetId == clone.Id).SourceId);
        session.Selection.Clear(); session.Selection.UnionWith(session.Document.Nodes.Select(n => n.Id));
        var previous = session.Document.Nodes.Length; session.Copy(); session.Paste();
        Assert.AreEqual(previous * 2, session.Document.Nodes.Length); session.Document.Validate();
    }

    [TestMethod]
    public void UndoThenEditDoesNotReuseAllocatedPmtNumbers()
    {
        var session = new ProcessingSession(); var first = session.Add(ProcessingNodeKind.Pmt, 0, 0);
        session.Undo(); var second = session.Add(ProcessingNodeKind.Pmt, 0, 0);
        Assert.IsTrue(second.Instances[0].Number > first.Instances[0].Number);
        Assert.IsFalse(session.CanRedo);
    }

    [TestMethod]
    public void ProjectRoundTripPreservesConnectionsSelectionAndViewport()
    {
        var session = new ProcessingSession(); var hatch = session.Add(ProcessingNodeKind.Hatch, -120, 33);
        session.CreatePmt(hatch.Id, ["x"], 12, 15); session.View(.75, -42, 200);
        var restored = ProcessingDocument.FromJson(session.Document.ToJson());
        Assert.AreEqual(session.Document.ToJson(), restored.ToJson());
        Assert.Throws<InvalidDataException>(() => ProcessingDocument.FromJson((restored with { Version = 99 }).ToJson()));
        Assert.Throws<InvalidDataException>(() => ProcessingDocument.FromJson((restored with { NextPmtNumber = 1 }).ToJson()));
    }

    [TestMethod]
    public void MovingNodesDoesNotInvalidateMachiningButEditingLayersDoes()
    {
        var session = new ProcessingSession(); var hatch = session.Add(ProcessingNodeKind.Hatch, 0, 0);
        var pmt = session.CreatePmt(hatch.Id, ["x"], 12, 15);
        var before = ProcessingExecutor.Configuration(session.Document, pmt.Id);
        session.UpdateNode(hatch with { X = 200, Y = 100 });
        Assert.AreEqual(before, ProcessingExecutor.Configuration(session.Document, pmt.Id));
        session.UpdateNode(session.Document.Node(pmt.Id) with { CanvasWidth = 420, CanvasHeight = 460 });
        Assert.AreEqual(before, ProcessingExecutor.Configuration(session.Document, pmt.Id));
        var restored = ProcessingDocument.FromJson(session.Document.ToJson()).Node(pmt.Id);
        Assert.AreEqual(420d, restored.CanvasWidth);
        Assert.AreEqual(460d, restored.CanvasHeight);
        session.UpdateNode(session.Document.Node(hatch.Id) with { Settings = new(hatch.Settings) { ["spacing"] = ".03" } });
        Assert.AreNotEqual(before, ProcessingExecutor.Configuration(session.Document, pmt.Id));
    }

    [TestMethod]
    public async Task ExecutorCachesVerifiedResultsAndRebuildsWhenSourceChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pmt-executor-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "input.tif"); await File.WriteAllTextAsync(path, "original");
            var session = new ProcessingSession(); var texture = session.Add(ProcessingNodeKind.Texture, 0, 0, path);
            var hatch = session.Add(ProcessingNodeKind.Hatch, 300, 0); session.Connect(texture.Id, hatch.Id, null);
            session.Edit(d => d with { OutputDirectory = dir });
            var runner = new FakeRunner(); var executor = new ProcessingExecutor(runner);
            await executor.RunAsync(session.Document, hatch.Id, CancellationToken.None); Assert.AreEqual(2, runner.Count);
            await executor.RunAsync(session.Document, hatch.Id, CancellationToken.None); Assert.AreEqual(2, runner.Count);
            await File.WriteAllTextAsync(path, "modified");
            await executor.RunAsync(session.Document, hatch.Id, CancellationToken.None); Assert.AreEqual(4, runner.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public async Task ExecutorRejectsMissingSelectedLayerWithoutRunningDownstream()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pmt-layer-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "input.tif"); await File.WriteAllTextAsync(path, "texture");
            var session = new ProcessingSession(); var texture = session.Add(ProcessingNodeKind.Texture, 0, 0, path);
            var hatch = session.Add(ProcessingNodeKind.Hatch, 300, 0); session.Connect(texture.Id, hatch.Id, ["missing"]);
            session.Edit(d => d with { OutputDirectory = dir });
            var runner = new FakeRunner(); var executor = new ProcessingExecutor(runner);
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RunAsync(session.Document, hatch.Id, CancellationToken.None));
            Assert.AreEqual(1, runner.Count); Assert.AreEqual("失败", executor.Status(session.Document, hatch.Id));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class FakeRunner : IProcessingStepRunner
    {
        public int Count;
        public async Task<ProcessingResult> RunAsync(ProcessingNode node, ProcessingResult? input, string directory, CancellationToken token)
        {
            Count++;
            if (node.Kind == ProcessingNodeKind.Texture) return new([new("source-layer", "texture", node.Setting("path"), 0)]);
            var output = Path.Combine(directory, "result.txt"); await File.WriteAllTextAsync(output, "hatch", token);
            return new([new("hatch-layer", "hatch", output, 0)]);
        }
    }

    [TestMethod]
    [TestCategory("ProcessingIntegration")]
    public async Task RealPythonPipelineExportsSelectedLayerAndMatrixPackage()
    {
        var python = Environment.GetEnvironmentVariable("PMT_WORKFLOW_PYTHON");
        if (string.IsNullOrWhiteSpace(python)) { Assert.Inconclusive("Set PMT_WORKFLOW_PYTHON to run the real processing integration test."); return; }
        var dir = Path.Combine(Path.GetTempPath(), "pmt-real-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "input.tiff");
            await PythonProcessingRunner.RunProcessAsync(python, ["-c", "from PIL import Image; import sys; im=Image.new('L',(20,20),255); im.paste(0,(3,3,17,17)); im.save(sys.argv[1],dpi=(254,254))", path], CancellationToken.None);
            var s = new ProcessingSession(); var texture = s.Add(ProcessingNodeKind.Texture, 0, 0, path);
            var gray = s.Add(ProcessingNodeKind.Grayscale, 300, 0); s.UpdateNode(gray with { Settings = new(gray.Settings) { ["layers"] = "3" } }); s.Connect(texture.Id, gray.Id, null);
            var hatch = s.Add(ProcessingNodeKind.Hatch, 600, 0);
            s.UpdateNode(hatch with { Settings = new(hatch.Settings) { ["width"] = "2", ["height"] = "2", ["spacing"] = "0.1", ["blocks"] = "4", ["min-area-percent"] = "20", ["max-area-percent"] = "30", ["boundary-blur"] = "0.02", ["tile-mode"] = "repeat" } });
            s.Connect(gray.Id, hatch.Id, null); s.Edit(d => d with { OutputDirectory = dir });
            var executor = new ProcessingExecutor(new PythonProcessingRunner(python, AppContext.BaseDirectory));
            var hatches = await executor.RunAsync(s.Document, hatch.Id, CancellationToken.None);
            Assert.AreEqual(3, hatches.Layers.Length);
            Assert.IsTrue(hatches.Layers.All(l => File.Exists(Path.ChangeExtension(l.Path, ".blocks.json"))));
            var pmt = s.CreatePmt(hatch.Id, [hatches.Layers[0].Id, hatches.Layers[2].Id], 2, 2);
            s.UpdateNode(pmt with { Instances = [pmt.Instances[0] with { Overrides = new() { ["power"] = "23" } }] });
            var matrix = s.Matrix(pmt.Id, pmt.Instances[0].Id, 2, 2, 1, 8, 8);
            var single = await executor.RunAsync(s.Document, pmt.Id, CancellationToken.None);
            var result = await executor.RunAsync(s.Document, matrix.Id, CancellationToken.None);
            Assert.AreEqual(2, result.Layers.Length);
            Assert.IsTrue(File.Exists(Path.Combine(result.PackageDirectory!, "allmachine.json")));
            Assert.AreEqual(5, Directory.GetFiles(result.PackageDirectory!, "*machine.json").Length);
            Assert.AreEqual(2, Directory.GetFiles(single.PackageDirectory!, "*machine.json").Length);
            foreach (var file in Directory.GetFiles(result.PackageDirectory!, "*machine.json").Where(p => !p.EndsWith("allmachine.json", StringComparison.Ordinal)))
            {
                using var package = JsonDocument.Parse(await File.ReadAllTextAsync(file));
                Assert.AreEqual(23, package.RootElement.GetProperty("laser_params")[0].GetProperty("power").GetInt32());
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
