using StreamMark.Avalonia.Rendering;
using StreamMark.Core.Streaming;

namespace StreamMark.Avalonia.Transcript;

/// <summary>会话中的一条内容。</summary>
public abstract class TranscriptEntry;

/// <summary>一条 Markdown 消息（静态或流式）。每个顶层块成为会话面板中的一行，可独立虚拟化。</summary>
public sealed class MarkdownEntry : TranscriptEntry
{
    public MarkdownEntry(StreamingDocument document, StreamingState? state = null)
    {
        Document = document;
        State = state;
    }

    /// <summary>流式消息：经节奏控制器平滑，带渐显与光标。</summary>
    public MarkdownEntry(MarkdownStream stream) : this(stream.Document, stream.State)
    {
    }

    /// <summary>静态消息。</summary>
    public static MarkdownEntry FromText(string markdown)
    {
        var document = new StreamingDocument();
        document.Load(markdown);
        return new MarkdownEntry(document);
    }

    public StreamingDocument Document { get; }

    public StreamingState? State { get; }

    /// <summary>宿主附加的数据（例如消息 ID）。</summary>
    public object? Tag { get; init; }
}

/// <summary>
/// 宿主提供的任意内容（用户消息气泡、工具调用卡片、推理过程折叠块等）。
/// <see cref="Content"/> 是控件时直接使用；否则通过数据模板构建。整条内容作为一行虚拟化。
/// </summary>
public sealed class ContentEntry(object content) : TranscriptEntry
{
    public object Content { get; } = content;
}
