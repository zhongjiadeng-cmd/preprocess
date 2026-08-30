namespace GrayscaleLayersMac;

/// <summary>
/// 已完整解码、但尚未显示到预览控件的灰度分层集合。
/// 调用 <see cref="TakeItems"/> 后，集合中各项的所有权转交给调用方；在此之前由本类负责释放。
/// </summary>
internal sealed class PreparedGrayscaleLayerSet : IDisposable
{
    private List<GrayscaleLayerPreviewItem>? _items;

    public PreparedGrayscaleLayerSet(IEnumerable<GrayscaleLayerPreviewItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToList();
        if (_items.Any(item => item is null))
            throw new ArgumentException("分层预览不能包含空项。", nameof(items));
    }

    /// <summary>移交所有项的所有权。每个已准备集合只能提交一次。</summary>
    public IReadOnlyList<GrayscaleLayerPreviewItem> TakeItems()
    {
        var items = _items ?? throw new InvalidOperationException("分层预览已经提交。");
        _items = null;
        return items;
    }

    /// <summary>未提交时释放所有项及其缩略图；已提交时不再拥有任何资源。</summary>
    public void Dispose()
    {
        if (_items is null)
            return;

        foreach (var item in _items)
            item.Dispose();
        _items = null;
    }
}
