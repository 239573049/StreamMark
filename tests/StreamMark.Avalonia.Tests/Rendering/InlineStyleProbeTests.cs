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
            var boldFace = bold.GlyphRun.GlyphTypeface;
            var regularFace = regular.GlyphRun.GlyphTypeface;
            var heavier = (int)boldFace.Weight > (int)regularFace.Weight || boldFace.FontSimulations.HasFlag(global::Avalonia.Media.FontSimulations.Bold);
            if (!heavier)
            {
                // 只有系统确实能加载更粗的字重时才算失败。CI 镜像里的苹方在 FamilyTypefaces 里列出 600，
                // 实际按 600 加载得到的却是 Medium，所以按列出的最粗字重真正加载一次，看拿到的真实字面（测试里探测一次可以接受）
                var family = new global::Avalonia.Media.FontFamily(boldFace.FamilyName);
                var available = family.FamilyTypefaces.Select(f => (int)f.Weight).DefaultIfEmpty(0).Max();
                var probe = new Typeface(family, FontStyle.Normal, (FontWeight)available).GlyphTypeface;
                var loadable = probe.FontSimulations == FontSimulations.None ? (int)probe.Weight : 0;
                Assert.True(loadable <= (int)regularFace.Weight,
                    $"bold CJK resolved to {boldFace.FamilyName}/{boldFace.Weight}, regular {regularFace.FamilyName}/{regularFace.Weight}, heaviest loadable {loadable} (listed {available})");
            }
            Assert.True((int)regular.GlyphRun.GlyphTypeface.Weight < 600);
            window.Close();
        });
    }

    [Fact]
    public async Task FallbackRuns_UseRealFaces_NotSyntheticCopies()
    {
        await HeadlessApp.Run(async () =>
        {
            // 回归：回退字体缺少粗体 / 斜体字面时（Apple Color Emoji、苹方 700），不能让 Avalonia 合成字形——
            // 合成会把整份字体文件复制进内存（Apple Color Emoji 约 600MB）
            var view = new MarkdownView { Markdown = "### 🤝 贡献\n\n普通 😀 **粗体 😀 中文** *斜体 😀 中文* ***粗斜 😀***", Margin = new(16) };
            var window = new Window { Width = 700, Height = 200, Content = view };
            window.Show();
            await HeadlessApp.Settle();
            view.ApplyLatest();
            await HeadlessApp.Settle();
            var runs = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<TextSegmentView>()
                .SelectMany(s => s.Layout!.TextLines).SelectMany(l => l.TextRuns).OfType<global::Avalonia.Media.TextFormatting.ShapedTextRun>().ToList();
            Assert.NotEmpty(runs);
            var primary = new Typeface(global::Avalonia.Controls.Documents.TextElement.GetFontFamily(view)).GlyphTypeface.FamilyName;
            foreach (var run in runs)
            {
                var face = run.GlyphRun.GlyphTypeface;
                // 只查回退字体：主字体自己的斜体 / 粗体按常规方式合成（CI 的 Linux 镜像只有 DejaVu Sans，
                // 中文画成方块、😀 由 DejaVu 自带，都用主字体）
                if (face.FamilyName == primary)
                {
                    continue;
                }
                Assert.True(face.FontSimulations == FontSimulations.None,
                    $"'{run.Text.Span}' uses synthetic {face.FamilyName}/{face.Weight}/{face.Style} ({face.FontSimulations})");
            }
            window.Close();
        });
    }
}
