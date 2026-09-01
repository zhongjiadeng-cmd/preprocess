namespace GrayscaleLayersMac;

public sealed record PmtSaveResult(
    bool Success,
    string? OutputPath,
    string Error,
    long SavedRevision);

public interface IPmtPackageGenerator
{
    Task<string> GenerateAsync(string requestJson, CancellationToken cancellationToken);
}

public sealed class PmtSaveService(IPmtPackageGenerator generator)
{
    private readonly IPmtPackageGenerator _generator =
        generator ?? throw new ArgumentNullException(nameof(generator));

    public async Task<PmtSaveResult> SaveAsync(
        PmtDraftSession session,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var snapshot = session.Snapshot;
        try
        {
            foreach (var source in snapshot.Sources.Sources)
                if (snapshot.Sources.HasChanged(source.Id))
                    throw new InvalidOperationException(
                        $"原始加工文件“{source.DisplayName}”在导入后已变化，请重新导入后再保存。");
            var request = PmtWorkflowRequestSerializer.Serialize(
                snapshot,
                outputDirectory,
                Guid.NewGuid().ToString("N"));
            var outputPath = await _generator.GenerateAsync(request, cancellationToken);
            if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
                throw new InvalidDataException("PMT 生成器没有返回有效的加工文件目录。");
            session.MarkSaved(snapshot.CurrentRevision);
            return new PmtSaveResult(true, outputPath, string.Empty, snapshot.CurrentRevision);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is
            ArgumentException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new PmtSaveResult(false, null, error.Message, session.Snapshot.SavedRevision);
        }
    }
}

public sealed class PythonPmtPackageGenerator(
    string pythonPath,
    string scriptPath,
    Action<string>? log = null) : IPmtPackageGenerator
{
    private readonly string _pythonPath = pythonPath;
    private readonly string _scriptPath = scriptPath;
    private readonly Action<string>? _log = log;

    public async Task<string> GenerateAsync(string requestJson, CancellationToken cancellationToken)
    {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("找不到 laser_pmt.py。", _scriptPath);
        var requestPath = Path.Combine(Path.GetTempPath(), $"pmt-request-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(requestPath, requestJson, cancellationToken);
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _pythonPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(_scriptPath);
            info.ArgumentList.Add(requestPath);
            using var process = System.Diagnostics.Process.Start(info)
                ?? throw new InvalidOperationException("无法启动 PMT 生成进程。");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var standardError = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(standardError)
                        ? $"PMT 生成失败，退出代码：{process.ExitCode}"
                        : standardError.Trim());
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _log?.Invoke(line.TrimEnd());
            using var document = System.Text.Json.JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            return Path.Combine(
                root.GetProperty("output_dir").GetString()!,
                root.GetProperty("output_name").GetString()!);
        }
        finally
        {
            try
            {
                if (File.Exists(requestPath))
                    File.Delete(requestPath);
            }
            catch (IOException)
            {
                // A stale request file is harmless; never mask the generator result.
            }
        }
    }
}
