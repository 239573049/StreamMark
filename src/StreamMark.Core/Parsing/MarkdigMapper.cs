using System.Collections.Immutable;
using System.Text;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using StreamMark.Core.Model;
using MdAlertBlock = Markdig.Extensions.Alerts.AlertBlock;
using MdBlock = Markdig.Syntax.Block;
using MdCodeBlock = Markdig.Syntax.CodeBlock;
using MdHeadingBlock = Markdig.Syntax.HeadingBlock;
using MdHtmlBlock = Markdig.Syntax.HtmlBlock;
using MdListBlock = Markdig.Syntax.ListBlock;
using MdListItemBlock = Markdig.Syntax.ListItemBlock;
using MdMathBlock = Markdig.Extensions.Mathematics.MathBlock;
using MdParagraphBlock = Markdig.Syntax.ParagraphBlock;
using MdQuoteBlock = Markdig.Syntax.QuoteBlock;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdThematicBreakBlock = Markdig.Syntax.ThematicBreakBlock;

namespace StreamMark.Core.Parsing;

/// <summary>
/// 把 Markdig 语法树转换为不可变的 StreamMark 块模型。转换后不再持有任何 Markdig 对象。
/// </summary>
internal static class MarkdigMapper
{
    /// <summary>
    /// 转换整篇文档。<paramref name="map"/> 把被解析文本中的位置映射回整篇源码中的位置。
    /// </summary>
    public static ImmutableArray<MarkdownBlock> MapDocument(MarkdownDocument document, MarkdownProfile profile, OffsetMap map)
    {
        var ctx = new MapContext(profile, map);
        var builder = ImmutableArray.CreateBuilder<MarkdownBlock>(document.Count);
        foreach (var block in document)
        {
            if (Map(block, ctx) is { } mapped)
            {
                builder.Add(mapped);
            }
        }
        return builder.ToImmutable();
    }

    private static MarkdownBlock? Map(MdBlock block, MapContext ctx)
    {
        // 插件映射优先
        foreach (var mapper in ctx.Profile.BlockMappers)
        {
            if (mapper(block, ctx) is { } custom)
            {
                return custom.Range == default ? custom with { Range = RangeOf(block, ctx) } : custom;
            }
        }
        MarkdownBlock? mapped = block switch
        {
            MdHeadingBlock heading => new Model.HeadingBlock(heading.Level, MapInlines(heading.Inline, ctx), heading.TryGetAttributes()?.Id),
            MdParagraphBlock paragraph => new Model.ParagraphBlock(MapInlines(paragraph.Inline, ctx)),
            MdMathBlock math => new Model.MathBlock(LinesToString(math), IsOpen: math.ClosingFencedCharCount == 0),
            FencedCodeBlock fenced => MapFencedCode(fenced),
            MdCodeBlock code => new Model.CodeBlock(null, null, LinesToString(code), IsFenced: false, IsOpen: false),
            MdAlertBlock alert => new Model.AlertBlock(alert.Kind.ToString().ToUpperInvariant(), MapChildren(alert, ctx)),
            MdQuoteBlock quote => new Model.QuoteBlock(MapChildren(quote, ctx)),
            MdListBlock list => MapList(list, ctx),
            MdTable table => MapTable(table, ctx),
            MdThematicBreakBlock => new Model.ThematicBreakBlock(),
            MdHtmlBlock html => new Model.HtmlBlock(LinesToString(html)),
            FootnoteGroup group => MapFootnotes(group, ctx),
            DefinitionList definitions => MapDefinitions(definitions, ctx),
            CustomContainer container => new CustomContainerBlock(container.Info, container.Arguments, MapChildren(container, ctx)),
            HtmlComponentBlock component => new ComponentBlock(component.Name.ToLowerInvariant(), component.Attributes, MapChildren(component, ctx)),
            // 链接引用定义只影响其它块的解析结果，本身不渲染
            LinkReferenceDefinitionGroup or LinkReferenceDefinition or BlankLineBlock => null,
            ContainerBlock container => new Model.QuoteBlock(MapChildren(container, ctx)),
            LeafBlock leaf => new Model.ParagraphBlock(MapInlines(leaf.Inline, ctx)),
            _ => null,
        };
        return mapped is null ? null : mapped with { Range = RangeOf(block, ctx) };
    }

    internal static SourceRange RangeOf(MdBlock block, MapContext ctx)
    {
        var span = block.Span;
        if (block is FencedCodeBlock { ClosingFencedCharCount: 0 } or MdMathBlock { ClosingFencedCharCount: 0 })
        {
            // 未闭合的围栏块一定延伸到被解析文本的末尾（Markdig 给出的 Span 只覆盖开头一行）
            var openStart = ctx.Map.ToSource(span.Start);
            return new SourceRange(openStart, ctx.Map.SourceEnd - openStart);
        }
        if (span.IsEmpty)
        {
            var at = ctx.Map.ToSource(Math.Max(span.Start, 0));
            return new SourceRange(at, 0);
        }
        var start = ctx.Map.ToSource(span.Start);
        return new SourceRange(start, ctx.Map.ToSource(span.End + 1) - start);
    }

    internal static ImmutableArray<MarkdownBlock> MapChildren(ContainerBlock container, MapContext ctx)
    {
        var builder = ImmutableArray.CreateBuilder<MarkdownBlock>(container.Count);
        foreach (var child in container)
        {
            if (Map(child, ctx) is { } mapped)
            {
                builder.Add(mapped);
            }
        }
        return builder.ToImmutable();
    }

    private static Model.CodeBlock MapFencedCode(FencedCodeBlock fenced)
    {
        var language = fenced.Info;
        if (string.IsNullOrWhiteSpace(language))
        {
            language = null;
        }
        var info = string.IsNullOrEmpty(fenced.Arguments) ? fenced.Info : $"{fenced.Info} {fenced.Arguments}";
        return new Model.CodeBlock(language, info, LinesToString(fenced), IsFenced: true, IsOpen: fenced.ClosingFencedCharCount == 0);
    }

    private static Model.ListBlock MapList(MdListBlock list, MapContext ctx)
    {
        var items = ImmutableArray.CreateBuilder<Model.ListItemBlock>(list.Count);
        foreach (var child in list)
        {
            if (child is not MdListItemBlock item)
            {
                continue;
            }
            var children = MapChildren(item, ctx);
            bool? task = null;
            // 任务复选框是首个段落的第一个行内元素：提升为列表项属性，并从段落文本中去掉
            if (children.Length > 0 && children[0] is Model.ParagraphBlock first && first.Content.Objects is [{ Kind: InlineObjectKind.TaskCheckbox, Offset: 0 } checkbox, ..])
            {
                task = checkbox.Flag;
                children = children.SetItem(0, first with { Content = StripLeadingObject(first.Content) });
            }
            items.Add(new Model.ListItemBlock(task, children) { Range = RangeOf(item, ctx) });
        }
        var start = 1;
        if (list.IsOrdered && list.OrderedStart is { } orderedStart && int.TryParse(orderedStart, out var parsed))
        {
            start = parsed;
        }
        return new Model.ListBlock(list.IsOrdered, start, list.IsOrdered ? list.OrderedDelimiter : list.BulletType, list.IsLoose, items.ToImmutable());
    }

    private static InlineContent StripLeadingObject(InlineContent content)
    {
        // 去掉占位字符以及其后的一个空格
        var cut = content.Text.Length > 1 && content.Text[1] == ' ' ? 2 : 1;
        var spans = ImmutableArray.CreateBuilder<InlineSpan>(content.Spans.Length);
        foreach (var span in content.Spans)
        {
            var start = Math.Max(span.Start - cut, 0);
            var end = span.End - cut;
            if (end > start)
            {
                spans.Add(span with { Start = start, Length = end - start });
            }
        }
        var objects = ImmutableArray.CreateBuilder<InlineObject>(content.Objects.Length - 1);
        for (var i = 1; i < content.Objects.Length; i++)
        {
            objects.Add(content.Objects[i] with { Offset = content.Objects[i].Offset - cut });
        }
        return new InlineContent(content.Text[cut..], spans.ToImmutable(), content.Links, objects.ToImmutable());
    }

    private static Model.TableBlock MapTable(MdTable table, MapContext ctx)
    {
        var columns = ImmutableArray.CreateBuilder<TableAlignment>(table.ColumnDefinitions.Count);
        foreach (var column in table.ColumnDefinitions)
        {
            columns.Add(column.Alignment switch
            {
                TableColumnAlign.Left => TableAlignment.Left,
                TableColumnAlign.Center => TableAlignment.Center,
                TableColumnAlign.Right => TableAlignment.Right,
                _ => TableAlignment.None,
            });
        }
        var rows = ImmutableArray.CreateBuilder<Model.TableRow>(table.Count);
        foreach (var child in table)
        {
            if (child is not MdTableRow row)
            {
                continue;
            }
            var cells = ImmutableArray.CreateBuilder<InlineContent>(row.Count);
            foreach (var cellBlock in row)
            {
                var cell = (TableCell)cellBlock;
                cells.Add(cell.Count > 0 && cell[0] is LeafBlock leaf ? MapInlines(leaf.Inline, ctx) : InlineContent.Empty);
            }
            rows.Add(new Model.TableRow(row.IsHeader, cells.ToImmutable()));
        }
        return new Model.TableBlock(columns.ToImmutable(), rows.ToImmutable());
    }

    private static FootnotesBlock MapFootnotes(FootnoteGroup group, MapContext ctx)
    {
        var footnotes = ImmutableArray.CreateBuilder<FootnoteDefinition>(group.Count);
        foreach (var child in group)
        {
            if (child is Footnote footnote)
            {
                footnotes.Add(new FootnoteDefinition(footnote.Label ?? footnote.Order.ToString(), footnote.Order, MapChildren(footnote, ctx)));
            }
        }
        return new FootnotesBlock(footnotes.ToImmutable());
    }

    private static DefinitionListBlock MapDefinitions(DefinitionList list, MapContext ctx)
    {
        var items = ImmutableArray.CreateBuilder<Model.DefinitionItem>(list.Count);
        foreach (var child in list)
        {
            if (child is not Markdig.Extensions.DefinitionLists.DefinitionItem item)
            {
                continue;
            }
            var terms = ImmutableArray.CreateBuilder<InlineContent>();
            var definitions = ImmutableArray.CreateBuilder<MarkdownBlock>();
            foreach (var part in item)
            {
                if (part is DefinitionTerm term)
                {
                    terms.Add(MapInlines(term.Inline, ctx));
                }
                else if (Map(part, ctx) is { } mapped)
                {
                    definitions.Add(mapped);
                }
            }
            items.Add(new Model.DefinitionItem(terms.ToImmutable(), definitions.ToImmutable()));
        }
        return new DefinitionListBlock(items.ToImmutable());
    }

    private static string LinesToString(LeafBlock block)
    {
        var lines = block.Lines;
        if (lines.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            var slice = lines.Lines[i].Slice;
            sb.Append(slice.Text, slice.Start, slice.Length);
        }
        return sb.ToString();
    }

    internal static InlineContent MapInlines(ContainerInline? root, MapContext ctx)
    {
        if (root is null || root.FirstChild is null)
        {
            return InlineContent.Empty;
        }
        var builder = new InlineBuilder(ctx);
        builder.Walk(root, InlineStyle.None, -1, -1);
        return builder.Build();
    }

    /// <summary>深度优先遍历行内树，生成扁平文本 + 样式区间。</summary>
    private sealed class InlineBuilder(MapContext ctx) : InlineMapContext
    {
        private readonly StringBuilder _text = new();
        private readonly List<InlineComponent> _components = [];
        // 插件回调期间的当前样式状态
        private InlineStyle _style;
        private int _link = -1;
        private int _component = -1;
        private readonly List<InlineSpan> _spans = [];
        private readonly List<LinkTarget> _links = [];
        private readonly List<InlineObject> _objects = [];

        public void Walk(ContainerInline container, InlineStyle style, int link, int component)
        {
            for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
            {
                Visit(inline, style, link, component);
            }
        }

        private void Visit(Inline inline, InlineStyle style, int link, int component)
        {
            if (!ctx.Profile.InlineMappers.IsEmpty)
            {
                (_style, _link, _component) = (style, link, component);
                foreach (var mapper in ctx.Profile.InlineMappers)
                {
                    if (mapper(inline, this))
                    {
                        return;
                    }
                }
            }
            switch (inline)
            {
                case LiteralInline literal:
                    Append(literal.Content.AsSpan(), style, link, component);
                    break;
                case CodeInline code:
                    Append(code.Content, style | InlineStyle.Code, link, component);
                    break;
                case MathInline math:
                    var display = math is BackslashMathInline { IsDisplay: true } || (math is not BackslashMathInline && math.DelimiterCount >= 2);
                    AddObject(InlineObjectKind.Math, math.Content.ToString(), flag: display);
                    break;
                case TaskList task:
                    AddObject(InlineObjectKind.TaskCheckbox, "", flag: task.Checked);
                    break;
                case LineBreakInline lineBreak:
                    Append(lineBreak.IsHard ? "\n" : SoftBreak(), style, link, component);
                    break;
                case HtmlEntityInline entity:
                    Append(entity.Transcoded.AsSpan(), style, link, component);
                    break;
                case AutolinkInline autolink:
                    {
                        var url = autolink.IsEmail && !autolink.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? "mailto:" + autolink.Url : autolink.Url;
                        var index = AddLink(url, null, isAutoLink: true);
                        Append(autolink.Url, style | InlineStyle.Link, index, component);
                        break;
                    }
                case LinkInline { IsImage: true } image:
                    AddObject(InlineObjectKind.Image, image.Url ?? "", alt: PlainText(image), title: image.Title);
                    break;
                case LinkInline anchor:
                    {
                        var url = anchor.GetDynamicUrl?.Invoke() ?? anchor.Url ?? "";
                        if (url == Remend.Remend.IncompleteLinkUrl)
                        {
                            Walk(anchor, style | InlineStyle.IncompleteLink, -1, component);
                        }
                        else
                        {
                            Walk(anchor, style | InlineStyle.Link, AddLink(url, anchor.Title, anchor.IsAutoLink), component);
                        }
                        break;
                    }
                case EmphasisInline emphasis:
                    Walk(emphasis, style | EmphasisStyle(emphasis), link, component);
                    break;
                case FootnoteLink { IsBackLink: false } footnote:
                    {
                        var index = AddLink("#fn:" + footnote.Footnote.Label, null, isAutoLink: false);
                        Append(footnote.Footnote.Order.ToString(), style | InlineStyle.FootnoteReference | InlineStyle.Superscript, index, component);
                        break;
                    }
                case HtmlInline html:
                    VisitHtml(html.Tag, style, link, component);
                    break;
                case ComponentLeafInline leaf:
                    if (string.Equals(leaf.Name, "img", StringComparison.OrdinalIgnoreCase))
                    {
                        AddObject(InlineObjectKind.Image, leaf.Attributes["src"] ?? "", alt: leaf.Attributes["alt"], title: leaf.Attributes["title"], attributes: leaf.Attributes);
                    }
                    else
                    {
                        AddObject(InlineObjectKind.Component, leaf.Name.ToLowerInvariant(), attributes: leaf.Attributes);
                    }
                    break;
                case ComponentContainerInline pair:
                    if (ComponentSpec.StyleFor(pair.Name) is { } builtInStyle)
                    {
                        Walk(pair, style | builtInStyle, link, component);
                    }
                    else
                    {
                        _components.Add(new InlineComponent(pair.Name.ToLowerInvariant(), pair.Attributes));
                        Walk(pair, style, link, _components.Count - 1);
                    }
                    break;
                case ContainerInline nested:
                    Walk(nested, style, link, component);
                    break;
            }
        }

        // 行内 HTML：只解释 <br>，其余标签按原文显示（可见、安全）
        private void VisitHtml(string tag, InlineStyle style, int link, int component)
        {
            if (tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
            {
                Append("\n", style, link, component);
                return;
            }
            Append(tag, style, link, component);
        }

        private static InlineStyle EmphasisStyle(EmphasisInline emphasis) => emphasis.DelimiterChar switch
        {
            '~' => InlineStyle.Strikethrough,
            _ when emphasis.DelimiterCount >= 2 => InlineStyle.Bold,
            _ => InlineStyle.Italic,
        };

        // 软换行：两侧都是 CJK 字符时不插入空格，否则按 CommonMark 渲染为空格
        private string SoftBreak()
        {
            return _text.Length > 0 && IsWide(_text[^1]) ? "" : " ";
        }

        private static bool IsWide(char c) =>
            c is >= '⺀' and <= '鿿' or >= '가' and <= '힯' or >= '豈' and <= '﫿' or >= '＀' and <= '￯';

        private static string PlainText(ContainerInline container)
        {
            var sb = new StringBuilder();
            foreach (var inline in container.Descendants<LiteralInline>())
            {
                sb.Append(inline.Content.AsSpan());
            }
            return sb.ToString();
        }

        private int AddLink(string url, string? title, bool isAutoLink)
        {
            _links.Add(new LinkTarget(url, string.IsNullOrEmpty(title) ? null : title, isAutoLink));
            return _links.Count - 1;
        }

        private void AddObject(InlineObjectKind kind, string value, string? alt = null, string? title = null, bool flag = false, ComponentAttributes? attributes = null)
        {
            _objects.Add(new InlineObject(_text.Length, kind, value, alt, string.IsNullOrEmpty(title) ? null : title, flag)
            {
                Attributes = attributes ?? ComponentAttributes.Empty,
            });
            _text.Append(InlineObject.Placeholder);
        }

        private void Append(ReadOnlySpan<char> text, InlineStyle style, int link, int component)
        {
            if (text.IsEmpty)
            {
                return;
            }
            var start = _text.Length;
            _text.Append(text);
            if (style == InlineStyle.None && link < 0 && component < 0)
            {
                return;
            }
            // 与前一个相邻且样式相同的区间合并
            if (_spans.Count > 0 && _spans[^1] is var last && last.End == start && last.Style == style && last.LinkIndex == link && last.ComponentIndex == component)
            {
                _spans[^1] = last with { Length = last.Length + text.Length };
                return;
            }
            _spans.Add(new InlineSpan(start, text.Length, style, link, component));
        }

        public InlineContent Build() => new(_text.ToString(), [.. _spans], [.. _links], [.. _objects]) { Components = [.. _components] };

        // ---- InlineMapContext（插件） ----

        public override InlineStyle Style => _style;

        public override void AppendText(ReadOnlySpan<char> text, InlineStyle extraStyle = InlineStyle.None) =>
            Append(text, _style | extraStyle, _link, _component);

        public override void AppendObject(InlineObjectKind kind, string value, ComponentAttributes? attributes = null, string? alt = null, bool flag = false) =>
            AddObject(kind, value, alt, flag: flag, attributes: attributes);

        public override void Walk(ContainerInline container, InlineStyle extraStyle = InlineStyle.None)
        {
            var (style, link, component) = (_style, _link, _component);
            Walk(container, style | extraStyle, link, component);
            (_style, _link, _component) = (style, link, component);
        }
    }
}
