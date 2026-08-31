namespace GrayscaleLayersMac;

/// <summary>
/// Validates selected pipeline inputs before the UI mutates any imported-layer state.
/// </summary>
internal static class PipelineImportPreparation
{
    public static async Task<PreparedPipelineImport> PrepareAsync(
        string[] tiffs,
        string[] dxfs,
        Func<string, CancellationToken, Task<TextureImageInspection>> inspectTiff,
        Func<string, DxfPreviewControl.PreparedDxfPreview> prepareDxf,
        IProgress<ImportProgressState> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tiffs);
        ArgumentNullException.ThrowIfNull(dxfs);
        ArgumentNullException.ThrowIfNull(inspectTiff);
        ArgumentNullException.ThrowIfNull(prepareDxf);
        ArgumentNullException.ThrowIfNull(progress);

        var tiffPaths = tiffs.ToArray();
        var dxfPaths = dxfs.ToArray();
        var total = checked(tiffPaths.Length + dxfPaths.Length);
        var inspections = new List<KeyValuePair<string, TextureImageInspection>>(tiffPaths.Length);
        var dxfPreviews = new List<DxfPreviewControl.PreparedDxfPreview>(dxfPaths.Length);

        for (var index = 0; index < tiffPaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = tiffPaths[index];
            progress.Report(ImportProgressState.ValidatingTiff(index + 1, total, file));

            try
            {
                var inspection = await inspectTiff(file, cancellationToken);
                inspections.Add(new KeyValuePair<string, TextureImageInspection>(file, inspection));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidDataException(
                    $"无法读取分层 TIFF {Path.GetFileName(file)}：{error.Message}", error);
            }
        }

        for (var index = 0; index < dxfPaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = dxfPaths[index];
            progress.Report(ImportProgressState.ValidatingDxf(tiffPaths.Length + index + 1, total, file));

            try
            {
                dxfPreviews.Add(prepareDxf(file));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidDataException(
                    $"无法读取 DXF {Path.GetFileName(file)}：{error.Message}", error);
            }
        }

        return new PreparedPipelineImport(inspections, dxfPreviews);
    }
}

internal sealed record PreparedPipelineImport(
    IReadOnlyList<KeyValuePair<string, TextureImageInspection>> TiffInspections,
    IReadOnlyList<DxfPreviewControl.PreparedDxfPreview> DxfPreviews)
{
    public IReadOnlyList<string> DxfPaths =>
        DxfPreviews.Select(preview => preview.Path).ToArray();

    public int TotalCount => TiffInspections.Count + DxfPreviews.Count;
}
