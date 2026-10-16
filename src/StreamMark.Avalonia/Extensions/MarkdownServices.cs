using Avalonia;
using StreamMark.Avalonia.Plugins;
using StreamMark.Core.Highlighting;
using StreamMark.Core.Parsing;

namespace StreamMark.Avalonia.Extensions;

/// <summary>
/// 渲染扩展的集合：公式、代码高亮、图表、图片。通过可继承的附加属性 <see cref="ServicesProperty"/>
/// 设置在窗口或任意祖先上，其下所有 Markdown 视图共用。
/// </summary>
public sealed class MarkdownServices
{
    public static MarkdownServices Default { get; } = new();

    private PluginRegistry? _registry;

    /// <summary>插件（公式、高亮、图表、组件等）。内置组件（details、think、kbd、引用等）总是启用。</summary>
    public IReadOnlyList<IMarkdownPlugin> Plugins { get; init; } = [];

    /// <summary>兼容写法：等同于注册一个只提供公式的插件。</summary>
    public IMathRenderer? Math { get; init; }

    /// <summary>兼容写法：等同于注册一个只提供代码高亮的插件。</summary>
    public ISyntaxHighlighter? Highlighter { get; init; }

    /// <summary>兼容写法：等同于注册只提供图表渲染的插件。</summary>
    public IReadOnlyList<IDiagramRenderer> Diagrams { get; init; } = [];

    public IImageLoader ImageLoader { get; init; } = new DataUriImageLoader();

    /// <summary>汇总后的插件注册表（首次访问时构建）。</summary>
    public PluginRegistry Registry => _registry ??= new PluginRegistry(this);

    /// <summary>与插件匹配的 Markdown 方言，创建文档时传入（<c>new StreamingDocument(services.Profile)</c>）。</summary>
    public MarkdownProfile Profile => Registry.Profile;

    public IDiagramRenderer? FindDiagramRenderer(string? language) => Registry.FindDiagramRenderer(language);

    public static readonly AttachedProperty<MarkdownServices> ServicesProperty =
        AvaloniaProperty.RegisterAttached<MarkdownServices, AvaloniaObject, MarkdownServices>("Services", Default, inherits: true);

    public static MarkdownServices GetServices(AvaloniaObject target) => target.GetValue(ServicesProperty);

    public static void SetServices(AvaloniaObject target, MarkdownServices value) => target.SetValue(ServicesProperty, value);
}
