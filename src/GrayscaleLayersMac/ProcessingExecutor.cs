using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrayscaleLayersMac;

public sealed record ProcessingResult(ProcessingLayer[] Layers, string? MachineDirectory = null,
    string? PackageDirectory = null, double Width = 0, double Height = 0, string Info = "");
public sealed record ProcessingRunState(string Configuration, string Status, ProcessingResult? Result, string Error = "");

public interface IProcessingStepRunner
{
    Task<ProcessingResult> RunAsync(ProcessingNode node, ProcessingResult? input, string directory, CancellationToken token);
}

public sealed class ProcessingExecutor(IProcessingStepRunner runner)
{
    public Dictionary<string, ProcessingRunState> States { get; } = new();
    private readonly Dictionary<string, string> _fileHashes = new();
    private bool _running;
    public event Action? Changed;
    public static string Configuration(ProcessingDocument document, string id) => JsonSerializer.Serialize(
        new { document.OutputDirectory, Nodes = document.ExecutionOrder(id).Select(n => new
        {
            n.Id, n.Kind, n.Settings, n.Instances, n.WorkpieceWidth, n.WorkpieceHeight,
            Input = document.Connections.SingleOrDefault(c => c.TargetId == n.Id)
        }) });
    public string Status(ProcessingDocument document, string id) => States.TryGetValue(id, out var state)
        ? state.Configuration == Configuration(document, id) ? state.Status : "待重新运行"
        : "未运行";
    public void Clear() { States.Clear(); _fileHashes.Clear(); Changed?.Invoke(); }

    public async Task<ProcessingResult> RunAsync(ProcessingDocument document, string target, CancellationToken token)
    {
        if (_running) throw new InvalidOperationException("已有节点正在运行。");
        _running = true;
        try
        {
            var snapshot = ProcessingDocument.FromJson(document.ToJson());
            if (string.IsNullOrWhiteSpace(snapshot.OutputDirectory)) throw new InvalidOperationException("请先设置工作流输出目录。");
            Directory.CreateDirectory(snapshot.OutputDirectory);
            ProcessingResult? last = null;
            var upstreamChanged = false;
            foreach (var node in snapshot.ExecutionOrder(target))
            {
                token.ThrowIfCancellationRequested();
                var config = Configuration(snapshot, node.Id);
                States.TryGetValue(node.Id, out var old);
                var canReuse = !upstreamChanged && old?.Status == "已完成" && old.Configuration == config && old.Result is not null;
                var currentHash = canReuse ? await HashFilesAsync(node, old!.Result!, token) : "";
                if (canReuse && currentHash.Length > 0 &&
                    _fileHashes.GetValueOrDefault(node.Id) == currentHash)
                { last = old!.Result; continue; }
                upstreamChanged = true;
                States[node.Id] = new(config, "运行中", old?.Result);
                Changed?.Invoke();
                try
                {
                    var edge = snapshot.Connections.SingleOrDefault(c => c.TargetId == node.Id);
                    if (node.Kind != ProcessingNodeKind.Texture && edge is null &&
                        !(node.Kind == ProcessingNodeKind.Machine && node.Setting("path").Length > 0))
                        throw new InvalidOperationException("缺少输入连接。");
                    ProcessingResult? input = edge is null ? null : States[edge.SourceId].Result;
                    // A process must not publish a current result from inputs changed mid-run.
                    var sourceNode = edge is null ? node : snapshot.Node(edge.SourceId);
                    var sourceResult = edge is null
                        ? node.Kind == ProcessingNodeKind.Texture
                            ? new ProcessingResult([new(node.Id, "texture", node.Setting("path"), 0)])
                            : null
                        : States[edge.SourceId].Result;
                    var inputHash = sourceResult is null ? null : await HashFilesAsync(sourceNode, sourceResult, token);
                    if (input is not null && edge is not null)
                        input = input with { Layers = ProcessingDocument.SelectLayers(input.Layers, edge.LayerIds) };
                    var path = Path.Combine(snapshot.OutputDirectory, $"{node.Kind}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(path);
                    last = await runner.RunAsync(node, input, path, token);
                    token.ThrowIfCancellationRequested();
                    if (sourceResult is not null && inputHash != await HashFilesAsync(sourceNode, sourceResult, token))
                        throw new InvalidOperationException("输入文件在运行期间发生变化，请重新运行。");
                    var hash = await HashFilesAsync(node, last, token);
                    if (string.IsNullOrEmpty(hash)) throw new InvalidDataException("生成结果缺失或为空。");
                    _fileHashes[node.Id] = hash;
                    States[node.Id] = new(config, "已完成", last);
                    Changed?.Invoke();
                }
                catch (Exception e)
                {
                    States[node.Id] = new(config, e is OperationCanceledException ? "已取消" : "失败", old?.Result, e.Message);
                    Changed?.Invoke();
                    throw;
                }
            }
            return last ?? throw new InvalidOperationException("没有可运行节点。");
        }
        finally { _running = false; }
    }
    private static async Task<string> HashFilesAsync(ProcessingNode node, ProcessingResult result, CancellationToken token)
    {
        try
        {
            var files = result.Layers.SelectMany(l => l.PreviewPath is null ? new[] { l.Path } : new[] { l.Path, l.PreviewPath }).ToList();
            foreach (var layer in result.Layers.Where(l => l.Path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)))
            {
                var sidecar = Path.ChangeExtension(layer.Path, ".blocks.json");
                if (File.Exists(sidecar)) files.Add(sidecar);
            }
            if (node.Kind == ProcessingNodeKind.Texture) files.Add(node.Setting("path"));
            foreach (var dir in new[] { result.MachineDirectory, result.PackageDirectory }.Where(d => d is not null))
                files.AddRange(Directory.EnumerateFiles(dir!, "*", SearchOption.AllDirectories));
            if (files.Count == 0) return "";
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var file in files.Distinct().Order(StringComparer.Ordinal))
            {
                if (!File.Exists(file) || new FileInfo(file).Length == 0) return "";
                hash.AppendData(Encoding.UTF8.GetBytes(file));
                await using var stream = File.OpenRead(file);
                hash.AppendData(await SHA256.HashDataAsync(stream, token));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (IOException) { return ""; }
    }
}

public sealed class PythonProcessingRunner(string python, string scripts, Action<string>? log = null) : IProcessingStepRunner
{
    public static readonly Dictionary<string, string> LaserOptions = new()
    {
        ["power"] = "power", ["frequency"] = "frequency", ["pulseWidthIdx"] = "pulse-width-idx",
        ["scanSpeed"] = "scan-speed", ["jump_vel"] = "jump-vel", ["jump_delay"] = "jump-delay",
        ["accScale"] = "acc-scale", ["cornerScale"] = "corner-scale", ["endScale"] = "end-scale",
        ["timeLag"] = "time-lag", ["laserOnShift"] = "laser-on-shift", ["delaseroff"] = "delaseroff", ["delaseron"] = "delaseron",
        ["scan_ahead"] = "scan-ahead", ["sky_writing"] = "sky-writing", ["layerFeedUm"] = "layer-step-um"
    };
    public static async Task<string> FindPythonAsync(CancellationToken token)
    {
        foreach (var candidate in new[] { "/opt/homebrew/bin/python3", "/usr/local/bin/python3", "python3" })
            try
            {
                await RunProcessAsync(candidate, ["-c", "import numpy, PIL"], token);
                return candidate;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        throw new InvalidOperationException("需要安装带 numpy 和 Pillow 的 Python 3。");
    }
    private Task<string> Script(string script, IEnumerable<string> args, CancellationToken token) =>
        RunProcessAsync(python, new[] { Path.Combine(scripts, script) }.Concat(args), token, log);
    public static async Task<string> RunProcessAsync(string executable, IEnumerable<string> args, CancellationToken token, Action<string>? log = null)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动加工进程。");
        // Drain both streams while running, including cancellation, to avoid pipe deadlocks.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await ProcessCancellation.WaitForExitOrTerminateAsync(process, token); }
        finally { await Task.WhenAll(stdout, stderr); }
        var text = await stdout;
        if (process.ExitCode != 0) throw new InvalidOperationException((await stderr).Trim() is { Length: > 0 } error ? error : $"加工进程退出：{process.ExitCode}");
        log?.Invoke(text.Length > 2000 ? text[^2000..] : text);
        return text;
    }
    public async Task<ProcessingResult> RunAsync(ProcessingNode node, ProcessingResult? input, string directory, CancellationToken token)
    {
        if (node.Kind == ProcessingNodeKind.Texture)
        {
            var path = node.Setting("path");
            if (!File.Exists(path)) throw new FileNotFoundException("纹理文件不存在，请重新导入。", path);
            var preview = Path.Combine(directory, "texture.png");
            var json = await RunProcessAsync(python, ["-c",
                "import sys,json; from PIL import Image; Image.MAX_IMAGE_PIXELS=120000000; im=Image.open(sys.argv[1]); dpi=im.info.get('dpi',(float(sys.argv[3]),)*2); w,h=im.size; im.thumbnail((640,640)); im.convert('RGB').save(sys.argv[2]); print(json.dumps({'width':w,'height':h,'dpi':float(dpi[0])}))",
                path, preview, node.Setting("dpi", "300")], token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var dpi = root.GetProperty("dpi").GetDouble();
            if (!double.IsFinite(dpi) || dpi <= 0) throw new InvalidDataException("纹理 DPI 无效。");
            var w = root.GetProperty("width").GetInt32(); var h = root.GetProperty("height").GetInt32();
            return new([new(node.Id + ":texture", Path.GetFileName(path), path, 0, preview)], Width: w / dpi * 25.4, Height: h / dpi * 25.4, Info: $"{w} × {h} px · {dpi:0.##} DPI");
        }
        if (node.Kind == ProcessingNodeKind.Grayscale)
        {
            var source = input?.Layers.SingleOrDefault() ?? throw new InvalidOperationException("灰度分层需要一张纹理。");
            var args = new List<string> { source.Path, directory };
            AddSettings(args, node, ["layers", "min-level", "max-level"]);
            if (node.Setting("below-is-white") == "true") args.Add("--below-is-white");
            await Script("grayscale_layers.py", args, token);
            var paths = Directory.GetFiles(directory, "layer_*.tiff");
            var layers = paths.Select(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var order = int.Parse(name.Split('_')[1], CultureInfo.InvariantCulture);
                // Threshold identity survives padding changes but cannot silently select a different threshold.
                return new ProcessingLayer(node.Id + ":" + name[(name.IndexOf("gray_", StringComparison.Ordinal))..], name, path, order);
            }).OrderBy(l => l.Order).ToArray();
            if (layers.Length != int.Parse(node.Setting("layers"), CultureInfo.InvariantCulture)) throw new InvalidDataException("分层输出数量不正确。");
            return new(layers, Width: input!.Width, Height: input.Height);
        }
        if (node.Kind == ProcessingNodeKind.Hatch)
        {
            if (input is null || input.Layers.Length == 0) throw new InvalidOperationException("Hatch 缺少纹理层。");
            var width = Number(node, "width"); var height = Number(node, "height");
            var step = Number(node, "angle-step", allowZero: true);
            var output = new List<ProcessingLayer>();
            var jobs = new List<List<string>>();
            foreach (var (layer, index) in input.Layers.Select((layer, index) => (layer, index)))
            {
                token.ThrowIfCancellationRequested();
                // Machine files use positive contiguous numeric layer names. The
                // stable graph layer ID and Order retain the source identity.
                var path = Path.Combine(directory, $"layer_{index + 1:D3}_hatch.dxf");
                var preview = Path.ChangeExtension(path, ".preview.png");
                var args = new List<string> { layer.Path, path, "--preview-output", preview,
                    "--angle", F((layer.Order == 0 ? step : (layer.Order - 1) * step) % 180),
                    "--seed", F(Number(node, "seed", true) + layer.Order * 7919),
                    "--min-block-area", F(width * height * Number(node, "min-area-percent") / 100),
                    "--max-block-area", F(width * height * Number(node, "max-area-percent") / 100) };
                AddSettings(args, node, ["width", "height", "spacing", "blocks", "boundary-blur", "boundary-correlation", "anchor", "tile-mode", "threshold"]);
                foreach (var flag in new[] { "bidirectional", "border" }) if (node.Setting(flag) == "true") args.Add("--" + flag);
                jobs.Add(args);
                output.Add(new(node.Id + ":" + layer.Id, layer.Name, path, layer.Order, preview));
            }
            if (jobs.Count == 1)
                await Script("texture_to_hatch_dxf.py", jobs[0], token);
            else
            {
                var hatchRequestPath = Path.Combine(directory, "hatch-request.json");
                await File.WriteAllTextAsync(hatchRequestPath, JsonSerializer.Serialize(jobs), new UTF8Encoding(false), token);
                await Script("texture_to_hatch_dxf.py", [hatchRequestPath, "--batch"], token);
            }
            return new(output.ToArray(), Width: width, Height: height);
        }
        if (node.Kind == ProcessingNodeKind.Machine)
        {
            if (input is null && node.Setting("path") is { Length: > 0 } imported)
            {
                var metadata = LaserPmtBaseMetadata.Parse(await Script("laser_pmt.py", ["--inspect-base", imported], token));
                return new([], imported, Width: metadata.UnitWidth, Height: metadata.UnitHeight);
            }
            if (input is null || input.Layers.Length == 0) throw new InvalidOperationException("Machine 缺少 DXF 层。");
            var machineInputDirectory = await StageMachineLayersAsync(input.Layers, directory, token);
            var args = new List<string> { machineInputDirectory, "machine", "--output-dir", directory,
                "--owner-token", Guid.NewGuid().ToString("N") };
            AddSettings(args, node, ["layer-step-um"]);
            foreach (var pair in LaserOptions.Where(p => p.Key != "layerFeedUm"))
            {
                var value = node.Setting(pair.Value);
                if (string.IsNullOrEmpty(value)) continue;
                if (pair.Key is "scan_ahead" or "sky_writing") args.Add((value == "true" ? "--" : "--no-") + pair.Value);
                else { args.Add("--" + pair.Value); args.Add(value); }
            }
            args.Add(node.Setting("block-center-positioning") == "true" ? "--block-center-positioning" : "--no-block-center-positioning");
            foreach (var layer in Directory.GetFiles(machineInputDirectory, "layer_*.dxf").Order(StringComparer.Ordinal)) { args.Add("--layer-dxf"); args.Add(layer); }
            await Script("dxf_to_machine_file.py", args, token);
            return new(input.Layers, Path.Combine(directory, "machine"), Width: input.Width, Height: input.Height);
        }
        if (input?.MachineDirectory is not { } machine) throw new InvalidOperationException("PMT 缺少 Machine 输入。");
        var meta = LaserPmtBaseMetadata.Parse(await Script("laser_pmt.py", ["--inspect-base", machine], token));
        var importedSource = PmtSourceCatalog.Empty.Import([new(machine, meta)]);
        if (importedSource.Errors.Count > 0) throw new InvalidDataException(importedSource.Errors[0].Message);
        var session = PmtDraftSession.Create(importedSource.Catalog,
            new(0, 0, node.WorkpieceWidth, node.WorkpieceHeight), Number(node, "spacing"), "PMT");
        var workflow = session.Snapshot.Workflow;
        var targets = node.Instances.Select(i => (LaserPmtWorkflowTarget)new LaserPmtTarget(i.Id, i.Number,
            new(i.X, i.Y, i.Width, i.Height), true)
        {
            SourceId = workflow.Sources[0].Id, NativeWidth = meta.UnitWidth, NativeHeight = meta.UnitHeight,
            IsSizeLocked = false, DirectParameterOverrides = new Dictionary<string, string>(i.Overrides)
        }).ToArray();
        if (targets.Length == 0) throw new InvalidOperationException("工件没有 PMT。");
        session.ApplyWorkflow(new(workflow.Sources, workflow.Workpiece, workflow.HatchSpacing, workflow.Viewport,
            workflow.BaseNodes, [], targets, [], 1, node.Instances.Max(i => i.Number) + 1, 1, workflow.Numbering));
        var request = PmtWorkflowRequestSerializer.Serialize(session.Snapshot, directory, Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(directory, "request.json");
        await File.WriteAllTextAsync(requestPath, request, new UTF8Encoding(false), token);
        await Script("laser_pmt.py", [requestPath], token);
        return new(input.Layers, machine, Path.Combine(directory, "PMT"), node.WorkpieceWidth, node.WorkpieceHeight);
    }
    private static void AddSettings(List<string> args, ProcessingNode node, string[] keys)
    {
        foreach (var key in keys) if (node.Setting(key) is { Length: > 0 } value) { args.Add("--" + key); args.Add(value); }
    }

    private static async Task<string> StageMachineLayersAsync(
        IReadOnlyList<ProcessingLayer> layers,
        string directory,
        CancellationToken token)
    {
        var staged = Path.Combine(directory, "selected-dxf");
        Directory.CreateDirectory(staged);
        foreach (var (layer, index) in layers.OrderBy(l => l.Order).Select((layer, index) => (layer, index)))
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(layer.Path))
                throw new FileNotFoundException("选中的 DXF 层不存在，请重新运行 Hatch。", layer.Path);
            var sourceName = Path.GetFileName(layer.Path);
            var destination = Path.Combine(staged, $"layer_{index + 1:D3}_from_{sourceName}");
            await using (var source = File.OpenRead(layer.Path))
            await using (var output = File.Create(destination))
                await source.CopyToAsync(output, token);
            var sidecar = Path.ChangeExtension(layer.Path, ".blocks.json");
            if (File.Exists(sidecar))
                File.Copy(sidecar, Path.ChangeExtension(destination, ".blocks.json"), true);
        }
        return staged;
    }
    public static double Number(ProcessingNode node, string key, bool allowZero = false)
    {
        if (!double.TryParse(node.Setting(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || (allowZero ? value < 0 : value <= 0)) throw new InvalidOperationException($"{key} 数值无效。");
        return value;
    }
    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);
}
