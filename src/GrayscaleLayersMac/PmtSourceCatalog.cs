using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace GrayscaleLayersMac;

public sealed record PmtSourceCandidate(
    string Directory,
    LaserPmtBaseMetadata Metadata);

public sealed record PmtSource(
    string Id,
    string Directory,
    string DisplayName,
    string Mark,
    uint ColorArgb,
    double NativeWidth,
    double NativeHeight,
    IReadOnlyDictionary<string, string> BaseParameters,
    string Fingerprint);

public sealed record PmtSourceImportError(string Directory, string Message);

public sealed record PmtSourceImportResult(
    PmtSourceCatalog Catalog,
    IReadOnlyList<PmtSourceImportError> Errors);

public sealed class PmtSourceCatalog
{
    private static readonly uint[] SourceColors =
    [
        0xFF0EA5E9,
        0xFFF97316,
        0xFF22C55E,
        0xFFA855F7,
        0xFFEAB308,
        0xFFEC4899,
        0xFF14B8A6,
        0xFF6366F1
    ];

    private readonly PmtSource[] _sources;

    public static PmtSourceCatalog Empty { get; } = new([], null);

    public IReadOnlyList<PmtSource> Sources { get; }
    public string? ActiveSourceId { get; }
    public PmtSource? ActiveSource => ActiveSourceId is null
        ? null
        : _sources.FirstOrDefault(source => source.Id == ActiveSourceId);

    private PmtSourceCatalog(PmtSource[] sources, string? activeSourceId)
    {
        _sources = sources.ToArray();
        Sources = Array.AsReadOnly(_sources);
        ActiveSourceId = activeSourceId;
    }

    public PmtSourceImportResult Import(IEnumerable<PmtSourceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var sources = _sources.ToList();
        var errors = new List<PmtSourceImportError>();
        var active = ActiveSourceId;

        foreach (var candidate in candidates)
        {
            if (candidate is null)
                continue;
            var requestedDirectory = candidate.Directory ?? string.Empty;
            try
            {
                var directory = NormalizeDirectory(requestedDirectory);
                var existing = sources.FirstOrDefault(source =>
                    string.Equals(source.Directory, directory, StringComparison.Ordinal));
                if (existing is not null)
                {
                    active = existing.Id;
                    continue;
                }

                ValidateMetadata(candidate.Metadata, directory);
                var fingerprint = ComputeFingerprint(directory);
                var index = sources.Count;
                var source = new PmtSource(
                    CreateSourceId(directory),
                    directory,
                    GetDisplayName(directory),
                    CreateMark(index),
                    SourceColors[index % SourceColors.Length],
                    candidate.Metadata.UnitWidth,
                    candidate.Metadata.UnitHeight,
                    CopyParameters(candidate.Metadata.Parameters),
                    fingerprint);
                sources.Add(source);
                active ??= source.Id;
            }
            catch (Exception error) when (
                error is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add(new PmtSourceImportError(
                    SafeFullPath(requestedDirectory),
                    error.Message));
            }
        }

        return new PmtSourceImportResult(
            new PmtSourceCatalog(sources.ToArray(), active),
            errors.AsReadOnly());
    }

    public PmtSourceCatalog SelectActive(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!_sources.Any(source => source.Id == sourceId))
            throw new ArgumentException($"找不到 PMT 原始来源：{sourceId}", nameof(sourceId));
        return new PmtSourceCatalog(_sources, sourceId);
    }

    public PmtSourceCatalog Relocate(string sourceId, PmtSourceCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(candidate);
        var index = Array.FindIndex(_sources, source => source.Id == sourceId);
        if (index < 0)
            throw new ArgumentException($"找不到 PMT 原始来源：{sourceId}", nameof(sourceId));

        var directory = NormalizeDirectory(candidate.Directory);
        ValidateMetadata(candidate.Metadata, directory);
        var previous = _sources[index];
        var replacement = previous with
        {
            Directory = directory,
            DisplayName = GetDisplayName(directory),
            NativeWidth = candidate.Metadata.UnitWidth,
            NativeHeight = candidate.Metadata.UnitHeight,
            BaseParameters = CopyParameters(candidate.Metadata.Parameters),
            Fingerprint = ComputeFingerprint(directory)
        };
        var sources = _sources.ToArray();
        sources[index] = replacement;
        return new PmtSourceCatalog(sources, ActiveSourceId);
    }

    public bool HasChanged(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var source = _sources.FirstOrDefault(item => item.Id == sourceId)
            ?? throw new ArgumentException($"找不到 PMT 原始来源：{sourceId}", nameof(sourceId));
        try
        {
            return !string.Equals(
                source.Fingerprint,
                ComputeFingerprint(source.Directory),
                StringComparison.Ordinal);
        }
        catch (Exception error) when (
            error is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return true;
        }
    }

    private static void ValidateMetadata(LaserPmtBaseMetadata metadata, string directory)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!double.IsFinite(metadata.UnitWidth) || metadata.UnitWidth <= 0 ||
            !double.IsFinite(metadata.UnitHeight) || metadata.UnitHeight <= 0)
            throw new InvalidDataException("原始加工文件的设计宽高必须是正的有限数值。");
        if (metadata.Parameters.Count != LaserPmtConfiguration.Parameters.Count)
            throw new InvalidDataException("原始加工文件的基础参数不完整。");
        if (!string.Equals(
                Path.GetFullPath(metadata.Identity),
                directory,
                StringComparison.Ordinal))
            throw new InvalidDataException("原始加工元数据与所选目录不匹配。");
    }

    private static string ComputeFingerprint(string directory)
    {
        var machinePath = Path.Combine(directory, "machine.json");
        ValidateRegularNonEmptyFile(machinePath, "machine.json");
        var patchesDirectory = Path.Combine(directory, "patches");
        if (!System.IO.Directory.Exists(patchesDirectory))
            throw new InvalidDataException("原始加工目录缺少 patches 文件夹。");
        var patchPaths = System.IO.Directory
            .EnumerateFiles(patchesDirectory, "*_0.npy", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (patchPaths.Length == 0)
            throw new InvalidDataException("原始加工目录没有可用的 patch 文件。");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, machinePath, "machine.json");
        foreach (var patchPath in patchPaths)
        {
            ValidateRegularNonEmptyFile(patchPath, "patch");
            AppendFile(hash, patchPath, $"patches/{Path.GetFileName(patchPath)}");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendFile(IncrementalHash hash, string path, string relativeName)
    {
        var nameBytes = Encoding.UTF8.GetBytes(relativeName);
        hash.AppendData(BitConverter.GetBytes(nameBytes.Length));
        hash.AppendData(nameBytes);
        using var stream = File.OpenRead(path);
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.AppendData(buffer, 0, count);
    }

    private static void ValidateRegularNonEmptyFile(string path, string label)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists || file.Length <= 0 ||
            (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException($"原始加工目录缺少有效的 {label} 文件：{path}");
    }

    private static string NormalizeDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullPath = Path.GetFullPath(directory);
        if (!System.IO.Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"原始加工目录不存在：{fullPath}");
        return fullPath;
    }

    private static string SafeFullPath(string directory)
    {
        try
        {
            return string.IsNullOrWhiteSpace(directory) ? directory : Path.GetFullPath(directory);
        }
        catch (Exception)
        {
            return directory;
        }
    }

    private static string CreateSourceId(string directory)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(directory));
        return $"source-{Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant()}";
    }

    private static string CreateMark(int index) => index < 26
        ? ((char)('A' + index)).ToString()
        : $"{(char)('A' + (index % 26))}{index / 26}";

    private static string GetDisplayName(string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static IReadOnlyDictionary<string, string> CopyParameters(
        IReadOnlyDictionary<string, string> parameters) =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(parameters, StringComparer.Ordinal));
}
