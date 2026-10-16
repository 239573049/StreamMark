namespace StreamMark.Avalonia.Plugins;

/// <summary>
/// 块级界面状态（展开 / 折叠、换行、图表与源码切换、滚动位置等），按 (顶层块 key, 状态名) 保存。
/// 每个文档一份，独立于视图：视图被虚拟化回收后再出现、或样式变化重建视图时，状态得以保留。
/// 源码只追加，块 key（源码起点）稳定。只在界面线程访问。
/// </summary>
public sealed class BlockStateStore
{
    private readonly Dictionary<(int Key, string Name), object> _values = [];

    public T Get<T>(int blockKey, string name, T fallback) =>
        _values.TryGetValue((blockKey, name), out var value) && value is T typed ? typed : fallback;

    public void Set<T>(int blockKey, string name, T value) where T : notnull => _values[(blockKey, name)] = value;

    public void Clear() => _values.Clear();
}
