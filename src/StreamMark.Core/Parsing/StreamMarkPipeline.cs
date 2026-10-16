using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;

namespace StreamMark.Core.Parsing;

/// <summary>
/// StreamMark 使用的 Markdig 管线（不可变、线程安全，全局共享一个实例）。
/// </summary>
/// <remarks>
/// 只启用删除线这一种强调扩展：上标 <c>^</c>、下标 <c>~</c>、插入 <c>++</c>、高亮 <c>==</c>
/// 在大模型输出的普通文本里误触发太多（如 "C++ 和 C++"、"a == b 且 c == d"、"约 ~5 到 ~10"）。
/// </remarks>
public static class StreamMarkPipeline
{
    public static MarkdownPipeline Default { get; } = Build();

    private static MarkdownPipeline Build() =>
        new MarkdownPipelineBuilder()
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
            .Use<BackslashMathExtension>()
            .Build();
}
