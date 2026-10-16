using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Plugins;
using StreamMark.Avalonia.Selection;

namespace StreamMark.Avalonia.Rendering;

/// <summary>
/// 一个文档的渲染上下文：样式、扩展服务、选区、流式状态。同一文档的所有块视图共享一个实例；
/// 样式或服务变化时宿主替换为新实例并通知视图刷新。
/// </summary>
public sealed class RenderContext
{
    public RenderContext(MarkdownStyle style, MarkdownServices services, SelectionModel selection, int entry, StreamingState? streaming, BlockStateStore? state = null)
    {
        State = state ?? new BlockStateStore();
        Style = style;
        Services = services;
        Selection = selection;
        Entry = entry;
        Streaming = streaming;
    }

    public MarkdownStyle Style { get; }

    public MarkdownServices Services { get; }

    /// <summary>插件注册表（等同于 <c>Services.Registry</c>）。</summary>
    public PluginRegistry Plugins => Services.Registry;

    /// <summary>文档的块级界面状态（跨视图回收与重建保留）。</summary>
    public BlockStateStore State { get; }

    public SelectionModel Selection { get; }

    /// <summary>文档在宿主中的序号（选区坐标的第一维）。</summary>
    public int Entry { get; }

    /// <summary>流式状态（渐显、光标），静态文档为 null。</summary>
    public StreamingState? Streaming { get; }

    public RenderContext With(MarkdownStyle? style = null, MarkdownServices? services = null, int? entry = null) =>
        new(style ?? Style, services ?? Services, Selection, entry ?? Entry, Streaming, State);
}
