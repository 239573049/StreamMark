using System.Collections.Immutable;

namespace StreamMark.Core.Model;

/// <summary>行内样式标志，可叠加。</summary>
[Flags]
public enum InlineStyle : ushort
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Strikethrough = 1 << 2,
    Code = 1 << 3,
    Link = 1 << 4,
    Superscript = 1 << 5,
    Subscript = 1 << 6,
    Keyboard = 1 << 7,
    FootnoteReference = 1 << 8,
    /// <summary>未闭合（流式中由 remend 补全）的链接，渲染为占位样式且不可点击。</summary>
    IncompleteLink = 1 << 9,
}

/// <summary>扁平文本中的一段样式区间。<see cref="LinkIndex"/> 指向 <see cref="InlineContent.Links"/>，-1 表示无链接。</summary>
public readonly record struct InlineSpan(int Start, int Length, InlineStyle Style, int LinkIndex = -1)
{
    public int End => Start + Length;
}

public sealed record LinkTarget(string Url, string? Title, bool IsAutoLink);

public enum InlineObjectKind : byte
{
    Math,
    Image,
    TaskCheckbox,
}

/// <summary>
/// 嵌在文本中的对象（行内公式、图片、任务复选框）。在 <see cref="InlineContent.Text"/> 中以
/// U+FFFC 占位，<see cref="Offset"/> 为占位字符的位置。
/// </summary>
public sealed record InlineObject(int Offset, InlineObjectKind Kind, string Value, string? Alt = null, string? Title = null, bool Flag = false)
{
    /// <summary>对象占位字符。</summary>
    public const char Placeholder = '￼';
}

/// <summary>
/// 一个段落级文本单元：渲染层可以直接据此构造一个 TextLayout。
/// 文本是扁平字符串，样式以不重叠、按起点排序的区间给出；未被任何区间覆盖的文本为普通样式。
/// </summary>
public sealed record InlineContent(
    string Text,
    ImmutableArray<InlineSpan> Spans,
    ImmutableArray<LinkTarget> Links,
    ImmutableArray<InlineObject> Objects)
{
    public static readonly InlineContent Empty = new("", [], [], []);

    public bool IsEmpty => Text.Length == 0;

    internal void AddTo(ref Fingerprint fp)
    {
        fp.Add(Text);
        fp.Add(Spans.Length);
        foreach (var span in Spans)
        {
            fp.Add(span.Start);
            fp.Add(span.Length);
            fp.Add((int)span.Style);
            fp.Add(span.LinkIndex);
        }
        foreach (var link in Links)
        {
            fp.Add(link.Url);
            fp.Add(link.Title);
        }
        foreach (var obj in Objects)
        {
            fp.Add(obj.Offset);
            fp.Add((int)obj.Kind);
            fp.Add(obj.Value);
            fp.Add(obj.Alt);
            fp.Add(obj.Flag ? 1 : 0);
        }
    }
}
