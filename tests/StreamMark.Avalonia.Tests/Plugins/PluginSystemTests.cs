using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using StreamMark.Avalonia;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Plugins;
using StreamMark.Avalonia.Rendering;
using StreamMark.Avalonia.Transcript;
using StreamMark.Core.Model;
using StreamMark.Core.Parsing;
using StreamMark.RaTeX;
using Xunit;

namespace StreamMark.Avalonia.Tests.Plugins;

public sealed class PluginSystemTests
{
    /// <summary>测试插件：块组件 &lt;Callout&gt;、行内组件 &lt;badge/&gt;、代码块头部操作、diff 围栏视图。</summary>
    private sealed class TestPlugin : IMarkdownPlugin
    {
        public int ActionRuns;

        public string Name => "test";

        public void Configure(MarkdownPluginBuilder builder)
        {
            builder.AddBlockComponent("callout", _ => new CalloutView());
            builder.AddInlineComponent("badge", new BadgeRenderer());
            builder.AddBlockAction(new BlockActionDescriptor("apply", "Apply", MarkdownIcons.Check,
                block => block is CodeBlock { Language: "cs" }, _ =>
                {
                    ActionRuns++;
                    return Task.CompletedTask;
                }));
            builder.AddFenceView("diff", _ => new DiffStubView());
        }
    }

    private sealed class CalloutView : StackBlockView
    {
        protected override Thickness ContentPadding => new(12, 8, 12, 8);

        protected override void OnUpdate(RenderContext context, MarkdownBlock? previous, MarkdownBlock block, int topKey, ref int segment, bool contextChanged) =>
            UpdateChildren(context, ((ComponentBlock)block).Children, topKey, ref segment);

        public override void Render(DrawingContext context) =>
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x7C, 0xF6)), new Pen(Brushes.MediumPurple, 1), new Rect(Bounds.Size).Deflate(0.5), 6, 6);
    }

    private sealed class BadgeRenderer : IInlineComponentRenderer
    {
        public DrawableTextRun CreateRun(InlineObject component, InlineComponentContext context) =>
            new ChipLikeRun(component.Attributes["text"] ?? "", context);

        public string ToPlainText(InlineObject component) => component.Attributes["text"] ?? "";
    }

    private sealed class ChipLikeRun(string text, InlineComponentContext context) : DrawableTextRun
    {
        private readonly TextLayout _layout = new(text, Typeface.Default, context.FontSize * 0.8, Brushes.White);

        public override TextRunProperties Properties => context.Properties;

        public override Size Size => new(_layout.WidthIncludingTrailingWhitespace + 12, _layout.Height + 2);

        public override double Baseline => Size.Height * 0.8;

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            drawingContext.DrawRectangle(Brushes.SeaGreen, null, new Rect(origin, Size), 8, 8);
            _layout.Draw(drawingContext, new Point(origin.X + 6, origin.Y + 1));
        }
    }

    private sealed class DiffStubView : BlockView
    {
        protected override void OnUpdate(RenderContext context, MarkdownBlock? previous, MarkdownBlock block, int topKey, ref int segment, bool contextChanged) =>
            segment++;

        protected override Size MeasureOverride(Size availableSize) => new(availableSize.Width, 40);

        public override void Render(DrawingContext context) => context.FillRectangle(Brushes.DarkRed, new Rect(Bounds.Size));
    }

    private const string Markdown = """
        Keys <kbd>Ctrl</kbd>+<kbd>K</kbd>, H<sub>2</sub>O, x<sup>2</sup>, <mark>marked</mark>, <u>under</u>, source<Citation id="1" /> and <cite href="https://example.com">a cited title</cite>. Status <badge text="beta"/>.

        <details>
        <summary>Collapsed details</summary>

        Hidden **content**.
        </details>

        <details open>
        <summary>Open details</summary>

        Visible content with a list:

        - one
        - two
        </details>

        <think>
        Model reasoning goes here.
        </think>

        <Callout>
        Custom **block** component from a plugin.
        </Callout>

        ```cs src/App.cs#L10-20
        var x = 1;
        ```

        ```diff
        -old
        +new
        ```
        """;

    [Fact]
    public async Task BuiltInAndPluginComponents_Render()
    {
        await HeadlessApp.Run(async () =>
        {
            var plugin = new TestPlugin();
            var services = new MarkdownServices { Plugins = [plugin, new RaTeXPlugin()] };
            var view = new MarkdownView { Margin = new(16) };
            MarkdownServices.SetServices(view, services);
            view.Markdown = Markdown;
            var window = new Window { Width = 720, Height = 900, Content = view, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)) };
            window.Show();
            await HeadlessApp.Settle();
            view.ApplyLatest();
            await HeadlessApp.Settle();

            var blocks = view.Snapshot!.Blocks.ToList();
            Assert.Contains(blocks, b => b is ComponentBlock { Name: "callout" });
            var children = view.GetVisualChildren().ToList();
            Assert.Contains(children, c => c is CalloutView);
            Assert.Contains(children, c => c is DiffStubView);
            var collapsibles = children.OfType<CollapsibleBlockView>().ToList();
            Assert.Equal(3, collapsibles.Count);
            Assert.False(collapsibles[0].IsOpen);
            Assert.True(collapsibles[1].IsOpen);

            // 头部操作：只出现在 cs 代码块上（复制、换行、下载、插件操作，加上隐藏的折叠栏按钮）
            var code = children.OfType<CodeBlockView>().Single();
            var actionButtons = code.GetVisualChildren().OfType<IconButton>().Count();
            Assert.Equal(5, actionButtons);

            // 复制：组件输出纯文本
            view.SelectionController.SelectAll();
            var text = view.SelectionController.GetSelectedText();
            Assert.Contains("Keys Ctrl+K", text);
            Assert.Contains("source[1]", text);
            Assert.Contains("Status beta", text);
            HeadlessApp.Save(window, "plugins-components");
            window.Close();
        });
    }

    [Fact]
    public async Task CompatibilityProperties_AndPlugins_RenderTheSameModel()
    {
        await HeadlessApp.Run(async () =>
        {
            var legacy = new MarkdownServices { Math = RaTeXMathRenderer.Shared };
            var plugin = new MarkdownServices { Plugins = [new RaTeXPlugin()] };
            Assert.Same(legacy.Registry.Math, plugin.Registry.Math);
            Assert.Equal(legacy.Profile.Components.Length, plugin.Profile.Components.Length);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task BlockState_SurvivesVirtualization()
    {
        await HeadlessApp.Run(async () =>
        {
            var sb = new StringBuilder("```text\nfirst code block\n```\n\n");
            for (var i = 0; i < 200; i++)
            {
                sb.Append("Filler paragraph ").Append(i).Append(".\n\n");
            }
            var view = new TranscriptView();
            var window = new Window { Width = 800, Height = 500, Content = view };
            window.Show();
            var entry = MarkdownEntry.FromText(sb.ToString());
            view.Entries.Add(entry);
            await entry.Document.WhenIdle();
            view.Panel.ApplyLatest();
            await HeadlessApp.Settle();
            // 新内容默认贴底：先完成首次布局，再回到顶部
            view.Offset = new(0, 0);
            await HeadlessApp.Settle();

            var code = view.Panel.Children.OfType<CodeBlockView>().Single();
            Assert.False(code.Wrap);
            // 用户切换"换行"：视图状态写回块状态存储
            code.Wrap = true;
            code.CurrentContext!.State.Set(code.TopKey, "code.wrap", true);

            // 滚到底部（代码块被回收），再滚回顶部（重新实例化）
            view.ScrollToEnd();
            await HeadlessApp.Settle();
            Assert.DoesNotContain(view.Panel.Children, c => c is CodeBlockView);
            view.Offset = new(0, 0);
            await HeadlessApp.Settle();
            var recreated = view.Panel.Children.OfType<CodeBlockView>().Single();
            Assert.NotSame(code, recreated);
            Assert.True(recreated.Wrap);
            window.Close();
        });
    }

    [Fact]
    public void FenceInfo_ParsesPathAndLines()
    {
        var info = CodeFenceInfo.Parse("cs src/App.cs#L10-20");
        Assert.Equal("cs", info.Language);
        Assert.Equal("src/App.cs", info.Path);
        Assert.Equal(10, info.StartLine);
        Assert.Equal(20, info.EndLine);
        Assert.Null(CodeFenceInfo.Parse("python").Path);
    }
}
