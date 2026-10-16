using System.Collections.Immutable;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Rendering;
using StreamMark.Core.Highlighting;
using StreamMark.Core.Model;
using StreamMark.Core.Parsing;

namespace StreamMark.Avalonia.Plugins;

/// <summary>汇总后的插件注册表（不可变）：渲染层据此分派；<see cref="Profile"/> 供文档解析使用。</summary>
public sealed class PluginRegistry
{
    private readonly Dictionary<string, Func<ComponentBlock, BlockView>> _blockComponents;
    private readonly Dictionary<string, IInlineComponentRenderer> _inlineComponents;
    private readonly Dictionary<string, FenceViewRegistration> _fenceViews;

    internal PluginRegistry(MarkdownServices services)
    {
        var builder = new MarkdownPluginBuilder(new MarkdownProfileBuilder());
        IEnumerable<IMarkdownPlugin> plugins = [BuiltInComponentsPlugin.Instance, .. services.Plugins];
        foreach (var plugin in plugins)
        {
            plugin.Configure(builder);
        }
        Math = builder.Math ?? services.Math;
        Highlighter = builder.Highlighter ?? services.Highlighter;
        Diagrams = [.. services.Diagrams, .. builder.Diagrams];
        _blockComponents = builder.BlockComponents;
        _inlineComponents = builder.InlineComponents;
        _fenceViews = builder.FenceViews;
        BlockActions = [.. builder.BlockActions];
        Profile = builder.Profile.Build();
    }

    /// <summary>与这些插件匹配的 Markdown 方言：宿主创建文档时应传入，否则插件注册的组件标签不会被解析。</summary>
    public MarkdownProfile Profile { get; }

    public IMathRenderer? Math { get; }

    public ISyntaxHighlighter? Highlighter { get; }

    public ImmutableArray<IDiagramRenderer> Diagrams { get; }

    public ImmutableArray<BlockActionDescriptor> BlockActions { get; }

    public IDiagramRenderer? FindDiagramRenderer(string? language)
    {
        foreach (var renderer in Diagrams)
        {
            if (renderer.CanRender(language))
            {
                return renderer;
            }
        }
        return null;
    }

    public bool TryGetBlockComponent(string name, out Func<ComponentBlock, BlockView> factory) => _blockComponents.TryGetValue(name, out factory!);

    public IInlineComponentRenderer? FindInlineComponent(string name) => _inlineComponents.GetValueOrDefault(name);

    internal FenceViewRegistration? FindFenceView(CodeBlock code) =>
        code.Language is { } language && _fenceViews.TryGetValue(language, out var registration) && (!code.IsOpen || registration.WhileStreaming)
            ? registration
            : null;
}
