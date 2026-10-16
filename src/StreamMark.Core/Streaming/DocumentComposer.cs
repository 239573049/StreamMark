using System.Collections.Immutable;
using System.Text;
using Markdig;
using StreamMark.Core.Model;
using StreamMark.Core.Parsing;
using StreamMark.Core.Remend;
using RemendHealer = StreamMark.Core.Remend.Remend;

namespace StreamMark.Core.Streaming;

/// <summary>
/// 流式组装：切分器决定冻结边界，新冻结的片段解析一次后永不再动，只有尾部在每次更新时重新修补、重新解析。
/// 非线程安全，由 <see cref="StreamingDocument"/> 的后台工作线程独占使用。
/// </summary>
internal sealed class DocumentComposer
{
    /// <summary>
    /// 冻结片段和最终整篇解析使用的 remend 选项：只保留与位置无关的转义修正，
    /// 不做末尾补全（已完整的文本不应该被"补全"）。
    /// </summary>
    private static readonly RemendOptions SettledOptions = new()
    {
        Bold = false,
        BoldItalic = false,
        Italic = false,
        InlineCode = false,
        Strikethrough = false,
        Katex = false,
        InlineKatex = false,
        Links = false,
        Images = false,
        HtmlTags = false,
        SetextHeadings = false,
    };

    private readonly MarkdownProfile _profile;
    private readonly RemendOptions _tailOptions;
    private AppendOnlyList<MarkdownBlock> _frozen = new();
    private StreamSegmenter _segmenter;
    private int _generation;

    public DocumentComposer(MarkdownProfile? profile = null, RemendOptions? tailOptions = null)
    {
        _profile = profile ?? MarkdownProfile.Default;
        var options = tailOptions ?? RemendOptions.Default;
        _tailOptions = _profile.RemendHandlers.IsEmpty ? options : options with { Handlers = [.. options.Handlers ?? [], .. _profile.RemendHandlers] };
        _segmenter = new StreamSegmenter(_profile.BlockComponentNames);
    }

    public int FrozenEnd => _segmenter.FrozenEnd;

    public void Reset()
    {
        _frozen = new AppendOnlyList<MarkdownBlock>();
        _segmenter = new StreamSegmenter(_profile.BlockComponentNames);
        _generation++;
    }

    /// <summary>
    /// 处理新文本。<paramref name="text"/> 是源码区间 [<paramref name="from"/>, 末尾)，要求 from 不大于 <see cref="FrozenEnd"/>。
    /// </summary>
    public DocumentSnapshot Update(string text, int from, long version)
    {
        var oldFrozen = _segmenter.FrozenEnd;
        var newFrozen = _segmenter.Advance(text, from);
        if (newFrozen > oldFrozen)
        {
            var region = text.Substring(oldFrozen - from, newFrozen - oldFrozen);
            foreach (var block in Parse(region, oldFrozen, SettledOptions))
            {
                _frozen.Add(block);
            }
        }

        var tailStart = newFrozen - from;
        ImmutableArray<MarkdownBlock> tail;
        if (tailStart >= text.Length)
        {
            tail = [];
        }
        else if (_segmenter.TryGetOpenFence(PartialLastLine(text), out var fenceStart, out var contentStart, out var indent, out var info))
        {
            // 尾部只是一个尚未闭合的代码块：不经过 remend 和 Markdig，直接按行构造
            tail = [OpenCodeBlock(text, from, fenceStart, contentStart, indent, info)];
        }
        else
        {
            tail = Parse(text[tailStart..], newFrozen, _tailOptions);
        }
        return new DocumentSnapshot(_frozen, _frozen.Count, tail, version, _generation, from + text.Length, newFrozen, isComplete: false);
    }

    /// <summary>流结束：整篇重解析一次（修正跨片段的引用定义、脚注、列表松紧），开启新的一代。</summary>
    public DocumentSnapshot Finish(string fullText, long version)
    {
        _generation++;
        _frozen = new AppendOnlyList<MarkdownBlock>();
        foreach (var block in Parse(fullText, 0, SettledOptions))
        {
            _frozen.Add(block);
        }
        return new DocumentSnapshot(_frozen, _frozen.Count, [], version, _generation, fullText.Length, fullText.Length, isComplete: true);
    }

    private static ReadOnlySpan<char> PartialLastLine(string text)
    {
        var newline = text.LastIndexOf('\n');
        return text.AsSpan(newline + 1);
    }

    internal ImmutableArray<MarkdownBlock> Parse(string raw, int @base, RemendOptions options)
    {
        var healed = RemendHealer.Complete(raw, options);
        var document = Markdown.Parse(healed, _profile.Pipeline);
        return MarkdigMapper.MapDocument(document, _profile, OffsetMap.Create(@base, raw, healed));
    }

    private static Model.CodeBlock OpenCodeBlock(string text, int from, int fenceStart, int contentStart, int indent, string? info)
    {
        var body = contentStart - from < text.Length ? text.AsSpan(contentStart - from) : [];
        // 与 remend 一致：去掉末尾单个空格（流式中常见的分词残留），保证与解析路径结果相同
        if (body.EndsWith(" ") && !body.EndsWith("  "))
        {
            body = body[..^1];
        }
        if (body.EndsWith("\n"))
        {
            body = body[..^1];
        }
        var code = new StringBuilder(body.Length);
        var first = true;
        foreach (var range in body.Split('\n'))
        {
            var line = body[range];
            if (line.EndsWith("\r"))
            {
                line = line[..^1];
            }
            // 去掉不超过开头围栏缩进量的前导空格（CommonMark 规则）
            var strip = 0;
            while (strip < indent && strip < line.Length && line[strip] == ' ')
            {
                strip++;
            }
            if (!first)
            {
                code.Append('\n');
            }
            code.Append(line[strip..]);
            first = false;
        }
        if (body.IsEmpty)
        {
            code.Clear();
        }

        string? language = null;
        if (info is not null)
        {
            var space = info.AsSpan().IndexOfAny(' ', '\t');
            language = space < 0 ? info : info[..space];
        }
        var end = from + text.Length;
        return new Model.CodeBlock(language, info, code.ToString(), IsFenced: true, IsOpen: true)
        {
            Range = new SourceRange(fenceStart, end - fenceStart),
        };
    }
}
