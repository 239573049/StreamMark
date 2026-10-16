namespace StreamMark.Core.Parsing;

/// <summary>
/// 把 remend 修补后文本中的位置映射回原始源码位置。
/// remend 只会在文本中插入少量字符（转义反斜杠、零宽空格），以及改写/截断末尾，
/// 所以用双指针贪心对齐即可：相同字符同时前进，多出的字符记为插入点；
/// 第一次无法对齐的位置之后（末尾被改写的部分）全部映射到原始文本末尾附近。
/// </summary>
internal readonly struct OffsetMap
{
    private readonly int _base;
    private readonly int _rawLength;
    private readonly int[]? _insertions;
    private readonly int _divergence;

    private OffsetMap(int @base, int rawLength, int[]? insertions, int divergence)
    {
        _base = @base;
        _rawLength = rawLength;
        _insertions = insertions;
        _divergence = divergence;
    }

    /// <summary>原文未经修补时的恒等映射。</summary>
    public static OffsetMap Identity(int @base, int length) => new(@base, length, null, int.MaxValue);

    public static OffsetMap Create(int @base, string raw, string healed)
    {
        if (ReferenceEquals(raw, healed) || raw == healed)
        {
            return Identity(@base, raw.Length);
        }
        List<int>? insertions = null;
        int i = 0, j = 0;
        while (i < raw.Length && j < healed.Length)
        {
            if (raw[i] == healed[j])
            {
                i++;
                j++;
            }
            else if (healed[j] is '\\' or '​')
            {
                (insertions ??= []).Add(j);
                j++;
            }
            else
            {
                break;
            }
        }
        var divergence = i < raw.Length && j < healed.Length ? j : int.MaxValue;
        return new OffsetMap(@base, raw.Length, insertions?.ToArray(), divergence);
    }

    /// <summary>被解析文本对应的原始源码末尾（绝对位置）。</summary>
    public int SourceEnd => _base + _rawLength;

    /// <summary>修补后文本中的位置 → 整篇源码中的绝对位置。</summary>
    public int ToSource(int healedIndex)
    {
        var inserted = 0;
        if (_insertions is { } insertions)
        {
            var index = Array.BinarySearch(insertions, healedIndex);
            inserted = index >= 0 ? index : ~index;
        }
        var raw = healedIndex - inserted;
        if (healedIndex > _divergence)
        {
            raw = Math.Min(raw, _rawLength);
        }
        return _base + Math.Clamp(raw, 0, _rawLength);
    }
}
