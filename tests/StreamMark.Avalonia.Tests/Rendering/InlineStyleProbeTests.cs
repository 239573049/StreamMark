using Avalonia.Controls;
using Avalonia.Media;
using StreamMark.Avalonia;
using StreamMark.Avalonia.Rendering;
using StreamMark.Core.Model;
using Xunit;

namespace StreamMark.Avalonia.Tests.Rendering;

public sealed class InlineStyleProbeTests
{
    [Fact]
    public async Task InlineStyles_Render()
    {
        await HeadlessApp.Run(async () =>
        {
            var view = new MarkdownView { Markdown = "Plain **bold** and *italic* and `code` and ~~strike~~ and [link](https://x.y).\n\n中文 **粗体** 和 *斜体* 和 `代码` 和 ~~删除~~。", Margin = new(16) };
            var window = new Window { Width = 700, Height = 160, Content = view, Background = Brushes.Black };
            window.Show();
            await HeadlessApp.Settle();
            view.ApplyLatest();
            await HeadlessApp.Settle();
            var p = Assert.IsType<ParagraphBlock>(view.Snapshot![0]);
            Assert.Contains(p.Content.Spans, s => s.Style == InlineStyle.Bold);
            HeadlessApp.Save(window, "probe-inline");

            // 回归：中文段落里的粗体必须用粗字重的回退字体（Avalonia 会复用上一段回退字体，且 700 会匹配到常规字重）
            var cjk = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<TextSegmentView>().Last();
            var runs = cjk.Layout!.TextLines.SelectMany(l => l.TextRuns).OfType<global::Avalonia.Media.TextFormatting.ShapedTextRun>().ToList();
            var bold = runs.Single(r => r.Text.Span.ToString().Contains("粗体"));
            var regular = runs.First(r => r.Text.Span.ToString().Contains("中文"));
            // 系统字体最粗只有中等字重（500）时也接受，只要比常规字重粗
            Assert.True((int)bold.GlyphRun.GlyphTypeface.Weight > (int)regular.GlyphRun.GlyphTypeface.Weight || bold.GlyphRun.GlyphTypeface.FontSimulations.HasFlag(global::Avalonia.Media.FontSimulations.Bold),
                $"bold CJK resolved to {bold.GlyphRun.GlyphTypeface.FamilyName}/{bold.GlyphRun.GlyphTypeface.Weight}");
            Assert.True((int)regular.GlyphRun.GlyphTypeface.Weight < 500);
            window.Close();
        });
    }
}
