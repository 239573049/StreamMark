using StreamMark.Core.Model;
using StreamMark.Core.Streaming;

namespace StreamMark.Avalonia.Selection;

/// <summary>目录中的一项：标题所在位置（可直接交给 <c>ScrollToPoint</c>）、级别、文字与锚点 id。</summary>
public sealed record OutlineItem(TextPoint Target, int Level, string Text, string? Id);

/// <summary>基于块模型的导航：目录、文内锚点（<c>#标题id</c>、脚注 <c>#fn:标签</c>）解析为选区坐标。</summary>
public static class DocumentNavigation
{
    /// <summary>文档中的标题（含 HTML 盒子、引用、列表等容器内的标题），按阅读顺序。</summary>
    public static List<OutlineItem> Outline(DocumentSnapshot snapshot, int entry)
    {
        var items = new List<OutlineItem>();
        foreach (var top in snapshot.Blocks)
        {
            foreach (var heading in Descendants(top).OfType<HeadingBlock>())
            {
                if (SegmentOf(top, heading) is { } segment)
                {
                    items.Add(new OutlineItem(new TextPoint(entry, top.Key, segment, 0), heading.Level, BlockText.PlainText(heading.Content), heading.Id));
                }
            }
        }
        return items;
    }

    /// <summary>
    /// 把文内链接解析为目标位置：<c>#fn:标签</c> 指向脚注定义，<c>#id</c> 指向 id 相同（或文字转成的锚点相同）的标题。
    /// 找不到时返回 null（交给宿主处理）。
    /// </summary>
    public static TextPoint? ResolveAnchor(DocumentSnapshot snapshot, int entry, string url)
    {
        if (!url.StartsWith('#') || url.Length < 2)
        {
            return null;
        }
        var anchor = Uri.UnescapeDataString(url[1..]);
        if (anchor.StartsWith("fn:", StringComparison.Ordinal))
        {
            var label = anchor[3..];
            foreach (var top in snapshot.Blocks)
            {
                if (top is FootnotesBlock footnotes)
                {
                    foreach (var footnote in footnotes.Footnotes)
                    {
                        // Markdig 的脚注标签带 "^"（链接为 #fn:^note），也接受不带的写法
                        if ((footnote.Label == label || footnote.Label == "^" + label || "^" + footnote.Label == label) && footnote.Children.Length > 0 && SegmentOf(top, footnote.Children[0]) is { } segment)
                        {
                            return new TextPoint(entry, top.Key, segment, 0);
                        }
                    }
                }
            }
            return null;
        }
        var outline = Outline(snapshot, entry);
        var match = outline.FirstOrDefault(o => string.Equals(o.Id, anchor, StringComparison.OrdinalIgnoreCase))
                    ?? outline.FirstOrDefault(o => string.Equals(Slug(o.Text), Slug(anchor), StringComparison.OrdinalIgnoreCase));
        return match?.Target;
    }

    /// <summary>GitHub 风格的锚点：小写，空白变连字符，去掉标点（保留字母、数字、CJK、连字符与下划线）。</summary>
    public static string Slug(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append('-');
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<MarkdownBlock> Descendants(MarkdownBlock block)
    {
        yield return block;
        IEnumerable<MarkdownBlock> children = block switch
        {
            HtmlBoxBlock box => box.Children,
            QuoteBlock quote => quote.Children,
            AlertBlock alert => alert.Children,
            ComponentBlock component => component.Children,
            CustomContainerBlock container => container.Children,
            ListBlock list => list.Items.SelectMany(i => i.Children),
            _ => [],
        };
        foreach (var child in children)
        {
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    // 目标块的第一个片段在顶层块片段列表中的序号（按内容引用匹配，与视图分配片段编号的顺序一致）
    private static int? SegmentOf(MarkdownBlock top, MarkdownBlock target)
    {
        var targetSegments = BlockText.Segments(target);
        if (targetSegments.Count == 0)
        {
            return null;
        }
        var first = targetSegments[0];
        var segments = BlockText.Segments(top);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (first.Content is not null ? ReferenceEquals(segment.Content, first.Content) : ReferenceEquals(segment.Raw, first.Raw))
            {
                return i;
            }
        }
        return null;
    }
}
