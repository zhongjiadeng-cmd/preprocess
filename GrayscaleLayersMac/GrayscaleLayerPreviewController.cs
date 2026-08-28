namespace GrayscaleLayersMac;

public sealed class GrayscaleLayerPreviewController : IDisposable
{
    private readonly List<GrayscaleLayerPreviewItem> _items = [];

    public IReadOnlyList<GrayscaleLayerPreviewItem> Items => _items;
    public GrayscaleLayerPreviewItem? SelectedItem { get; private set; }
    public string? Error { get; private set; }

    public IReadOnlyList<GrayscaleLayerPreviewItem> Refresh(string directory)
    {
        Clear();
        Error = null;
        if (!Directory.Exists(directory))
            return _items;

        try
        {
            var paths = Directory.EnumerateFiles(directory, "layer_*.tiff")
                .Where(IsRegularNonEmptyFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var index = 0; index < paths.Length; index++)
            {
                var item = new GrayscaleLayerPreviewItem(paths[index], index + 1);
                _items.Add(item);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = ex.Message;
            return _items;
        }

        if (_items.Count > 0)
            SelectedItem = _items[0];
        return _items;
    }

    public bool Select(int index)
    {
        if (index < 0 || index >= _items.Count)
            return false;
        SelectedItem = _items[index];
        return true;
    }

    public void Clear()
    {
        foreach (var item in _items)
            item.Dispose();
        _items.Clear();
        SelectedItem = null;
    }

    public void Dispose() => Clear();

    private static bool IsRegularNonEmptyFile(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        return file.Exists && file.Length > 0 &&
            (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }
}
