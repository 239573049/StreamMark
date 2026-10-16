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
            window.Close();
        });
    }
}
