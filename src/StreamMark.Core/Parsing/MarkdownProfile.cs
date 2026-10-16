using System.Collections.Immutable;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using StreamMark.Core.Model;
using StreamMark.Core.Remend;

namespace StreamMark.Core.Parsing;

/// <summary>
/// 一套 Markdown 方言：Markdig 管线、白名单组件、插件映射与 remend 处理器。不可变、线程安全，
/// 由 <see cref="MarkdownProfileBuilder"/> 构建；同一宿主的所有文档通常共用一个实例。
/// </summary>
public sealed class MarkdownProfile
{
    internal MarkdownProfile(MarkdownPipeline pipeline, ImmutableArray<ComponentSpec> components,
        ImmutableArray<Func<Block, MapContext, MarkdownBlock?>> blockMappers,
        ImmutableArray<Func<Inline, InlineMapContext, bool>> inlineMappers,
        ImmutableArray<RemendHandler> remendHandlers)
    {
        Pipeline = pipeline;
        Components = components;
        Table = new ComponentTable(components);
        BlockMappers = blockMappers;
        InlineMappers = inlineMappers;
        RemendHandlers = remendHandlers;
    }

    /// <summary>默认方言：GFM、数学、脚注、提示块、自定义容器等，加上内置白名单组件。</summary>
    public static MarkdownProfile Default { get; } = new MarkdownProfileBuilder().Build();

    public MarkdownPipeline Pipeline { get; }

    public ImmutableArray<ComponentSpec> Components { get; }

    internal ComponentTable Table { get; }

    internal ImmutableArray<Func<Block, MapContext, MarkdownBlock?>> BlockMappers { get; }

    internal ImmutableArray<Func<Inline, InlineMapContext, bool>> InlineMappers { get; }

    /// <summary>插件追加的 remend 处理器（流式尾部使用）。</summary>
    public ImmutableArray<RemendHandler> RemendHandlers { get; }

    /// <summary>块级组件名（小写），切分器据此不在容器内部冻结。</summary>
    internal ImmutableArray<string> BlockComponentNames => Table.BlockNames;
}

/// <summary>构建 <see cref="MarkdownProfile"/>。插件在这里注册语法扩展、组件与映射。</summary>
public sealed class MarkdownProfileBuilder
{
    private readonly List<IMarkdownExtension> _extensions = [];
    private readonly List<ComponentSpec> _components = [.. ComponentSpec.BuiltIn];
    private readonly List<Func<Block, MapContext, MarkdownBlock?>> _blockMappers = [];
    private readonly List<Func<Inline, InlineMapContext, bool>> _inlineMappers = [];
    private readonly List<RemendHandler> _remendHandlers = [];

    /// <summary>追加 Markdig 扩展（自定义块 / 行内解析器）。</summary>
    public MarkdownProfileBuilder AddMarkdigExtension(IMarkdownExtension extension)
    {
        _extensions.Add(extension);
        return this;
    }

    /// <summary>登记白名单 HTML 组件；同名时后登记的覆盖先登记的。</summary>
    public MarkdownProfileBuilder AddComponent(ComponentSpec spec)
    {
        _components.RemoveAll(c => string.Equals(c.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
        _components.Add(spec);
        return this;
    }

    /// <summary>为自定义 Markdig 块节点提供模型映射；返回 null 表示交给后续映射器 / 内置映射。</summary>
    public MarkdownProfileBuilder MapBlock<TNode>(Func<TNode, MapContext, MarkdownBlock?> map) where TNode : Block
    {
        _blockMappers.Add((block, context) => block is TNode node ? map(node, context) : null);
        return this;
    }

    /// <summary>为自定义 Markdig 行内节点提供映射：通过 <see cref="InlineMapContext"/> 输出文本或对象，返回 true 表示已处理。</summary>
    public MarkdownProfileBuilder MapInline<TNode>(Func<TNode, InlineMapContext, bool> map) where TNode : Inline
    {
        _inlineMappers.Add((inline, context) => inline is TNode node && map(node, context));
        return this;
    }

    /// <summary>追加 remend 处理器（作用于流式尾部，优先级规则见 <see cref="Remend.Remend"/>）。</summary>
    public MarkdownProfileBuilder AddRemendHandler(RemendHandler handler)
    {
        _remendHandlers.Add(handler);
        return this;
    }

    public MarkdownProfile Build()
    {
        var components = _components.ToImmutableArray();
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables(new PipeTableOptions { UseGfmRules = true })
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseTaskLists()
            .UseAutoLinks()
            .UseFootnotes()
            .UseAlertBlocks()
            .UseDefinitionLists()
            .UseCustomContainers()
            .UseMathematics()
            .UseCjkFriendlyEmphasis()
            .UseEmojiAndSmiley(enableSmileys: false)
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .Use<BackslashMathExtension>();
        builder.Extensions.Add(new HtmlComponentExtension(new ComponentTable(components)));
        foreach (var extension in _extensions)
        {
            builder.Extensions.Add(extension);
        }

        return new MarkdownProfile(builder.Build(), components, [.. _blockMappers], [.. _inlineMappers], [.. _remendHandlers]);
    }
}

/// <summary>块映射上下文：插件映射自定义块时用来映射子块、行内内容和源码区间。</summary>
public sealed class MapContext
{
    internal MapContext(MarkdownProfile profile, OffsetMap map)
    {
        Profile = profile;
        Map = map;
    }

    public MarkdownProfile Profile { get; }

    internal OffsetMap Map { get; }

    public ImmutableArray<MarkdownBlock> MapChildren(ContainerBlock container) => MarkdigMapper.MapChildren(container, this);

    public InlineContent MapInlines(ContainerInline? inline) => MarkdigMapper.MapInlines(inline, this);

    public SourceRange RangeOf(Block block) => MarkdigMapper.RangeOf(block, this);
}

/// <summary>行内映射上下文：插件向当前行内内容输出文本、对象，或继续映射子节点。</summary>
public abstract class InlineMapContext
{
    /// <summary>当前继承的样式。</summary>
    public abstract InlineStyle Style { get; }

    public abstract void AppendText(ReadOnlySpan<char> text, InlineStyle extraStyle = InlineStyle.None);

    public abstract void AppendObject(InlineObjectKind kind, string value, ComponentAttributes? attributes = null, string? alt = null, bool flag = false);

    /// <summary>以附加样式映射一个容器节点的子节点。</summary>
    public abstract void Walk(ContainerInline container, InlineStyle extraStyle = InlineStyle.None);
}
