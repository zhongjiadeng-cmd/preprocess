namespace GrayscaleLayersMac;

/// <summary>
/// 纹理界面的图层序列：索引 0 是源纹理，1..N 是灰度分层结果。
///
/// 纹理和分层本质是同一张图的不同视图，所以它们共用一条序列而不是两个标签页——
/// 用户在同一块画布上按上下层切换即可对照，缩放与位置由控件决定是否保留。
/// </summary>
public sealed class GrayscaleLayerPreviewController : IDisposable
{
    private readonly bool _reserveSourceSlot;
    private readonly List<GrayscaleLayerPreviewItem> _layers = [];
    private readonly List<GrayscaleLayerPreviewItem> _items = [];
    private GrayscaleLayerPreviewItem? _placeholder;
    private GrayscaleLayerPreviewItem? _source;

    /// <param name="reserveSourceSlot">
    /// 为 true 时索引 0 恒留给源纹理（未导入时用占位项撑住，保证层号不跳变）。
    /// 不做分层、只预览输入图的场景传 false。
    /// </param>
    public GrayscaleLayerPreviewController(bool reserveSourceSlot = true)
    {
        _reserveSourceSlot = reserveSourceSlot;
        if (reserveSourceSlot)
            _placeholder = GrayscaleLayerPreviewItem.SourcePlaceholder();
    }

    /// <summary>源纹理槽位索引；既没有源纹理也没预留时为 -1。</summary>
    public int SourceSlotIndex => _source is not null || _reserveSourceSlot ? 0 : -1;

    /// <summary>第一个分层项的索引；没有分层时为 -1。</summary>
    public int FirstLayerIndex => _layers.Count == 0 ? -1 : LayerIndexOffset;

    public bool HasLayers => _layers.Count > 0;

    public IReadOnlyList<GrayscaleLayerPreviewItem> Items => _items;
    public GrayscaleLayerPreviewItem? SourceItem => _source;
    public GrayscaleLayerPreviewItem? SelectedItem { get; private set; }
    public string? Error { get; private set; }

    public int SelectedIndex =>
        SelectedItem is null ? -1 : _items.IndexOf(SelectedItem);

    // 源纹理（或它的占位）占掉索引 0 时，分层从 1 开始编号。
    private int LayerIndexOffset => _source is not null || _reserveSourceSlot ? 1 : 0;

    /// <summary>设置第 0 层源纹理；传 null 表示清空（回到占位）。所有权移交给本控制器。</summary>
    public void SetSource(GrayscaleLayerPreviewItem? source)
    {
        if (ReferenceEquals(_source, source))
            return;

        // 用户正停在第 0 层（或还没选过任何层）时，新纹理应当立刻顶上。
        var focusSourceSlot = SelectedItem is null || SelectedItem.IsSourceTexture;
        var previous = _source;
        _source = source;
        Rebuild();
        if (focusSourceSlot)
            Select(0);
        previous?.Dispose();
    }

    public IReadOnlyList<GrayscaleLayerPreviewItem> Refresh(string directory)
    {
        string[] paths;
        try
        {
            paths = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "layer_*.tiff")
                    .Where(IsRegularNonEmptyFile)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ClearLayers();
            Error = ex.Message;
            return RebuildAndSelect();
        }

        return RefreshFiles(paths);
    }

    /// <summary>
    /// 直接按给定的文件列表重建分层序列，第 0 层源纹理保持不变。
    /// 用于"导入时按文件类型路由"：手动选中的 TIFF 不必都叫 layer_*.tiff，
    /// 这里按路径排序结果作为层序。
    /// </summary>
    public IReadOnlyList<GrayscaleLayerPreviewItem> RefreshFiles(IEnumerable<string> paths)
    {
        ClearLayers();
        Error = null;
        foreach (var path in paths
                     .Where(IsRegularNonEmptyFile)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            _layers.Add(new GrayscaleLayerPreviewItem(path, _layers.Count + LayerIndexOffset));
        }

        return RebuildAndSelect();
    }

    /// <summary>
    /// 用已完整准备好的分层原子替换当前分层。源纹理槽位保持不变，且集合所有权移交给控制器。
    /// </summary>
    public void ReplaceLayers(IEnumerable<GrayscaleLayerPreviewItem> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        // 必须在清理旧项前完成枚举和校验：准备集合有问题时，可见预览保持原状。
        var replacement = layers.ToList();
        if (replacement.Any(layer => layer is null))
            throw new ArgumentException("分层预览不能包含空项。", nameof(layers));
        if (replacement.Any(layer => layer.IsSourceTexture))
            throw new ArgumentException("分层预览不能包含源纹理项。", nameof(layers));

        ClearLayers();
        _layers.AddRange(replacement);
        Error = null;
        Rebuild();
        if (FirstLayerIndex >= 0)
            Select(FirstLayerIndex);
        else if (SelectedItem is null && _source is not null)
            Select(0);
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
        ClearLayers();
        var source = _source;
        _source = null;
        _items.Clear();
        SelectedItem = null;
        Rebuild();   // 预留槽位时补回占位，界面结构不因清空而散架
        source?.Dispose();
    }

    public void Dispose()
    {
        Clear();
        var placeholder = _placeholder;
        _placeholder = null;
        placeholder?.Dispose();
    }

    private IReadOnlyList<GrayscaleLayerPreviewItem> RebuildAndSelect()
    {
        Rebuild();
        if (FirstLayerIndex >= 0)
            Select(FirstLayerIndex);
        else if (SelectedItem is null && _source is not null)
            Select(0);
        return _items;
    }

    private void Rebuild()
    {
        var previousIndex = SelectedIndex;
        _items.Clear();
        if (_source is not null)
            _items.Add(_source);
        else if (_reserveSourceSlot)
            _items.Add(_placeholder!);
        _items.AddRange(_layers);

        // 源纹理的出现或消失会平移所有分层的层号，这里统一重排。
        var offset = LayerIndexOffset;
        _source?.Reindex(0);
        for (var index = 0; index < _layers.Count; index++)
            _layers[index].Reindex(index + offset);

        SelectedItem = previousIndex >= 0 && previousIndex < _items.Count
            ? _items[previousIndex]
            : null;
    }

    private void ClearLayers()
    {
        foreach (var layer in _layers)
            layer.Dispose();
        _layers.Clear();
    }

    private static bool IsRegularNonEmptyFile(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        return file.Exists && file.Length > 0 &&
            (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }
}
