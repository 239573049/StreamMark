using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Rendering;
using StreamMark.Core.Highlighting;
using StreamMark.Core.Model;
using StreamMark.Core.Parsing;

namespace StreamMark.Avalonia.Plugins;

/// <summary>
/// Markdown 插件：在 <see cref="Configure"/> 中注册语法（白名单组件、Markdig 扩展、映射）、渲染（块组件视图、
/// 行内组件、代码围栏视图）与交互（代码块头部操作）。插件只通过委托与工厂注册，NativeAOT 安全。
/// </summary>
public interface IMarkdownPlugin
{
    string Name { get; }

    void Configure(MarkdownPluginBuilder builder);
}

/// <summary>插件注册入口。</summary>
public sealed class MarkdownPluginBuilder
{
    internal MarkdownPluginBuilder(MarkdownProfileBuilder profile)
    {
        Profile = profile;
    }

    /// <summary>语法层（Core）：白名单组件、Markdig 扩展、自定义节点映射、remend 处理器。</summary>
    public MarkdownProfileBuilder Profile { get; }

    internal IMathRenderer? Math { get; private set; }
    internal ISyntaxHighlighter? Highlighter { get; private set; }
    internal List<IDiagramRenderer> Diagrams { get; } = [];
    internal Dictionary<string, Func<ComponentBlock, BlockView>> BlockComponents { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, IInlineComponentRenderer> InlineComponents { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, FenceViewRegistration> FenceViews { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal List<BlockActionDescriptor> BlockActions { get; } = [];

    public MarkdownPluginBuilder UseMath(IMathRenderer renderer)
    {
        Math = renderer;
        return this;
    }

    public MarkdownPluginBuilder UseHighlighter(ISyntaxHighlighter highlighter)
    {
        Highlighter = highlighter;
        return this;
    }

    /// <summary>图表渲染器（代码块闭合后渲染，可在图表 / 源码间切换）。</summary>
    public MarkdownPluginBuilder AddDiagramRenderer(IDiagramRenderer renderer)
    {
        Diagrams.Add(renderer);
        return this;
    }

    /// <summary>
    /// 块级组件视图。<paramref name="name"/> 对应白名单标签（若尚未登记，会自动登记为块级组件）。
    /// 工厂只负责创建；内容通过 <see cref="BlockView.Update"/> 下发（同名组件的新模型会复用同一视图）。
    /// </summary>
    public MarkdownPluginBuilder AddBlockComponent(string name, Func<ComponentBlock, BlockView> factory, bool registerTag = true)
    {
        BlockComponents[name] = factory;
        if (registerTag)
        {
            Profile.AddComponent(new ComponentSpec(name, ComponentKind.Block));
        }
        return this;
    }

    /// <summary>行内组件渲染器（自闭合组件绘制为嵌入对象，成对组件提供样式与点击）。</summary>
    public MarkdownPluginBuilder AddInlineComponent(string name, IInlineComponentRenderer renderer, ComponentKind kind = ComponentKind.SelfClosing, bool registerTag = true)
    {
        InlineComponents[name] = renderer;
        if (registerTag)
        {
            Profile.AddComponent(new ComponentSpec(name, kind));
        }
        return this;
    }

    /// <summary>按围栏语言替换代码块视图（如 diff）。默认只在代码块闭合后替换，流式中仍按普通代码显示。</summary>
    public MarkdownPluginBuilder AddFenceView(string language, Func<CodeBlock, BlockView> factory, bool whileStreaming = false)
    {
        FenceViews[language] = new FenceViewRegistration(factory, whileStreaming);
        return this;
    }

    /// <summary>代码块（及表格、图表）头部的操作按钮。</summary>
    public MarkdownPluginBuilder AddBlockAction(BlockActionDescriptor action)
    {
        BlockActions.Add(action);
        return this;
    }
}

internal sealed record FenceViewRegistration(Func<CodeBlock, BlockView> Factory, bool WhileStreaming);

/// <summary>行内组件渲染器。</summary>
public interface IInlineComponentRenderer
{
    /// <summary>为自闭合组件创建嵌入文本行的绘制对象；返回 null 则显示为空。</summary>
    DrawableTextRun? CreateRun(InlineObject component, InlineComponentContext context) => null;

    /// <summary>成对组件内部文字的样式（前景色、背景、下划线等），返回 null 表示保持继承样式。</summary>
    InlineComponentStyle? GetStyle(InlineComponent component, MarkdownStyle style) => null;

    /// <summary>复制为纯文本时的输出（自闭合组件），默认为空字符串。</summary>
    string ToPlainText(InlineObject component) => "";

    /// <summary>是否可点击（悬停显示手型，点击触发 <see cref="TextSegmentView.ComponentClickedEvent"/>）。</summary>
    bool IsClickable(ComponentAttributes attributes) => false;
}

/// <summary>成对行内组件的样式覆盖。</summary>
public sealed record InlineComponentStyle(IBrush? Foreground = null, IBrush? Background = null, bool Underline = false, FontWeight? Weight = null);

/// <summary>创建行内组件绘制对象时的上下文。</summary>
public sealed record InlineComponentContext(MarkdownStyle Style, double FontSize, IBrush Foreground, TextRunProperties Properties, double MaxWidth);

/// <summary>块头部操作。</summary>
public sealed record BlockActionDescriptor(
    string Id,
    string Label,
    Geometry Icon,
    Func<MarkdownBlock, bool> AppliesTo,
    Func<BlockActionContext, Task> Execute);

/// <summary>执行块操作时的上下文。</summary>
public sealed record BlockActionContext(MarkdownBlock Block, Control View, CodeFenceInfo? Fence);

/// <summary>代码围栏信息串解析结果：<c>```cs src/App.cs#L10-20</c>。</summary>
public sealed record CodeFenceInfo(string? Language, string? Path, int? StartLine, int? EndLine)
{
    public static CodeFenceInfo Parse(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return new CodeFenceInfo(null, null, null, null);
        }
        var parts = info.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var language = parts.Length > 0 ? parts[0] : null;
        string? path = null;
        int? start = null, end = null;
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Contains('=') || part.StartsWith('{'))
            {
                continue;
            }
            var hash = part.IndexOf("#L", StringComparison.Ordinal);
            if (hash >= 0)
            {
                var range = part[(hash + 2)..].Split('-', 'L');
                if (int.TryParse(range[0], out var s))
                {
                    start = s;
                    end = range.Length > 1 && int.TryParse(range[^1], out var e) ? e : s;
                }
                part = part[..hash];
            }
            if ((part.Contains('/') || part.Contains('.')) && path is null)
            {
                path = part;
            }
        }
        return new CodeFenceInfo(language, path, start, end);
    }
}
