using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using StreamMark.Core.Model;

namespace StreamMark.Core.Parsing;

/// <summary>
/// 白名单 HTML 组件：块级容器（内部按 Markdown 解析，可跨空行、可嵌套）、成对行内组件、自闭合行内组件。
/// 不在白名单中的标签保持 Markdig 默认行为。
/// </summary>
internal sealed class HtmlComponentExtension(ComponentTable table) : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<HtmlComponentBlockParser>())
        {
            pipeline.BlockParsers.InsertBefore<HtmlBlockParser>(new HtmlComponentBlockParser(table));
        }
        if (!pipeline.InlineParsers.Contains<HtmlComponentInlineParser>())
        {
            pipeline.InlineParsers.InsertBefore<AutolinkInlineParser>(new HtmlComponentInlineParser(table));
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

/// <summary>块级组件容器。</summary>
internal sealed class HtmlComponentBlock(BlockParser parser, string name, ComponentAttributes attributes) : ContainerBlock(parser)
{
    public string Name { get; } = name;

    public ComponentAttributes Attributes { get; } = attributes;

    /// <summary>本行内容之后即结束（结束标签与内容同在一行）。</summary>
    public bool CloseAfterLine { get; set; }

    /// <summary>已关闭（Markdig 的 IsOpen 在续行判断时并不可靠，自行记录）。</summary>
    public bool IsClosedBlock { get; set; }
}

/// <summary>自闭合的行内组件。</summary>
internal sealed class ComponentLeafInline(string name, ComponentAttributes attributes) : LeafInline
{
    public string Name { get; } = name;

    public ComponentAttributes Attributes { get; } = attributes;
}

/// <summary>成对的行内组件：后续行内元素成为其子节点，遇到结束标签时闭合。</summary>
internal sealed class ComponentContainerInline(string name, ComponentAttributes attributes) : ContainerInline
{
    public string Name { get; } = name;

    public ComponentAttributes Attributes { get; } = attributes;
}

internal sealed class HtmlComponentBlockParser : BlockParser
{
    private readonly ComponentTable _table;

    public HtmlComponentBlockParser(ComponentTable table)
    {
        _table = table;
        OpeningCharacters = ['<'];
    }

    // Markdig 对每个候选解析器都会询问（即使当前块不是段落），只在段落中限制：块级开始标签可以打断段落
    public override bool CanInterrupt(BlockProcessor processor, Block block) =>
        block is not Markdig.Syntax.ParagraphBlock || ReadTagAtLine(processor, out var tag, out var spec) && spec.Kind == ComponentKind.Block && !tag.IsClosing;

    public override bool Close(BlockProcessor processor, Block block)
    {
        if (block is HtmlComponentBlock component)
        {
            component.IsClosedBlock = true;
        }
        return true;
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent || !ReadTagAtLine(processor, out var tag, out var spec))
        {
            return BlockState.None;
        }
        if (spec.Kind != ComponentKind.Block)
        {
            // 以行内组件开头的行（如单独一行的 <Citation id="1" />）：交给段落解析器，
            // 否则 CommonMark 会把它当成第 7 类 HTML 块按原文显示
            return processor.Parsers.Find<ParagraphBlockParser>()?.TryOpen(processor) ?? BlockState.None;
        }
        if (tag.IsClosing)
        {
            return BlockState.None;
        }

        var block = new HtmlComponentBlock(this, spec.Name, tag.Attributes)
        {
            Column = processor.Column,
            Line = processor.LineIndex,
            Span = new SourceSpan(processor.Start, processor.Line.End),
        };
        for (var i = 0; i < tag.Length; i++)
        {
            processor.NextChar();
        }
        TrimTrailingClose(processor, block);
        processor.NewBlocks.Push(block);
        return BlockState.Continue;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        var component = (HtmlComponentBlock)block;
        if (component.CloseAfterLine)
        {
            return BlockState.Break;
        }
        // 同名的内层容器仍开着时，结束标签属于内层
        if (HasOpenNested(component))
        {
            component.UpdateSpanEnd(processor.Line.End);
            return BlockState.Continue;
        }
        var line = processor.Line;
        var text = line.AsSpan().TrimStart();
        if (HtmlTagReader.TryRead(text, out var tag) && tag.IsClosing && string.Equals(tag.Name, component.Name, StringComparison.OrdinalIgnoreCase)
            && text[tag.Length..].IsWhiteSpace())
        {
            component.UpdateSpanEnd(line.End);
            return BlockState.BreakDiscard;
        }
        TrimTrailingClose(processor, component);
        component.UpdateSpanEnd(line.End);
        return BlockState.Continue;
    }

    // 内容与结束标签在同一行（"…</details>"）：截掉结束标签，本行处理完后关闭
    private static void TrimTrailingClose(BlockProcessor processor, HtmlComponentBlock block)
    {
        var line = processor.Line.AsSpan().TrimEnd();
        var closing = $"</{block.Name}>";
        if (line.Length > 0 && line.EndsWith(closing, StringComparison.OrdinalIgnoreCase))
        {
            processor.Line.End = processor.Line.Start + line.Length - closing.Length - 1;
            block.CloseAfterLine = true;
        }
    }

    private static bool HasOpenNested(HtmlComponentBlock block)
    {
        var current = block.LastChild;
        while (current is not null)
        {
            if (current is HtmlComponentBlock { IsClosedBlock: false } nested && string.Equals(nested.Name, block.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = current is ContainerBlock container ? container.LastChild : null;
        }
        return false;
    }

    private bool ReadTagAtLine(BlockProcessor processor, out HtmlTagReader.Tag tag, out ComponentSpec spec)
    {
        spec = null!;
        if (!HtmlTagReader.TryRead(processor.Line.AsSpan(), out tag))
        {
            return false;
        }
        return _table.TryGet(tag.Name, out spec);
    }
}

internal sealed class HtmlComponentInlineParser : InlineParser
{
    private readonly ComponentTable _table;

    public HtmlComponentInlineParser(ComponentTable table)
    {
        _table = table;
        OpeningCharacters = ['<'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (!HtmlTagReader.TryRead(slice.AsSpan(), out var tag) || !_table.TryGet(tag.Name, out var spec) || spec.Kind == ComponentKind.Block)
        {
            return false;
        }
        var start = slice.Start;
        var end = start + tag.Length - 1;

        if (tag.IsClosing)
        {
            if (spec.Kind != ComponentKind.InlinePair || FindOpen(processor, spec.Name) is not { } open)
            {
                return false;
            }
            open.IsClosed = true;
            open.Span = new SourceSpan(open.Span.Start, processor.GetSourcePosition(end));
            slice.Start = end + 1;
            return true;
        }

        var position = processor.GetSourcePosition(start, out var line, out var column);
        if (spec.Kind == ComponentKind.SelfClosing || tag.IsSelfClosing)
        {
            processor.Inline = new ComponentLeafInline(spec.Name, tag.Attributes)
            {
                Span = new SourceSpan(position, processor.GetSourcePosition(end)),
                Line = line,
                Column = column,
            };
        }
        else
        {
            processor.Inline = new ComponentContainerInline(spec.Name, tag.Attributes)
            {
                Span = new SourceSpan(position, processor.GetSourcePosition(end)),
                Line = line,
                Column = column,
                IsClosed = false,
            };
        }
        slice.Start = end + 1;
        return true;
    }

    // 沿"最后一个未闭合容器"链向下，找最内层同名的未闭合组件
    private static ComponentContainerInline? FindOpen(InlineProcessor processor, string name)
    {
        ComponentContainerInline? found = null;
        ContainerInline? container = processor.Block?.Inline;
        while (container?.LastChild is ContainerInline { IsClosed: false } child)
        {
            if (child is ComponentContainerInline component && string.Equals(component.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = component;
            }
            container = child;
        }
        return found;
    }
}
