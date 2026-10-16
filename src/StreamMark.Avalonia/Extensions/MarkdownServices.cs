using Avalonia;
using StreamMark.Avalonia.Images;
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

    private static ImagePipeline? s_defaultImages;

    /// <summary>HTML 中作者指定的颜色如何处理（默认：按作者颜色，但与背景对比度不足时改用主题颜色）。</summary>
    public HtmlColorMode HtmlColors { get; init; } = HtmlColorMode.Readable;

    /// <summary>链接安全策略：允许的协议、确认要求。</summary>
    public LinkPolicy Links { get; init; } = LinkPolicy.Default;

    /// <summary>引用来源（<c>&lt;Citation id&gt;</c> 悬停卡片与点击），没有时只显示标签。</summary>
    public ISourceProvider? Sources { get; init; }

    /// <summary>显示右键菜单（复制、复制为 Markdown、全选、查找）。</summary>
    public bool ContextMenu { get; init; } = true;

    /// <summary>右键菜单文字（每次打开菜单时调用，可随界面语言变化）。</summary>
    public Func<Selection.MarkdownMenuCommand, string> MenuText { get; init; } = Selection.MarkdownContextMenu.DefaultText;

    /// <summary>代码块选项：行号、折叠、下载。</summary>
    public CodeBlockOptions CodeBlocks { get; init; } = CodeBlockOptions.Default;

    /// <summary>
    /// 图片管线（加载策略、内存 / 磁盘缓存）。默认是进程共享的仅内存缓存实例；
    /// 宿主通常自建一个带磁盘缓存目录的实例（并负责释放）。
    /// </summary>
    public ImagePipeline Images { get; init; } = s_defaultImages ??= new ImagePipeline();

    /// <summary>
    /// 相对图片地址（如 <c>images/a.png</c>）的基准目录或地址，可继承：设在会话视图 / 文档预览上，
    /// 通常为项目根目录。本地文件还需在 <see cref="ImagePolicy.AllowedFileRoots"/> 中放行。
    /// </summary>
    public static readonly AttachedProperty<string?> ImageBaseUriProperty =
        AvaloniaProperty.RegisterAttached<MarkdownServices, AvaloniaObject, string?>("ImageBaseUri", null, inherits: true);

    public static string? GetImageBaseUri(AvaloniaObject target) => target.GetValue(ImageBaseUriProperty);

    public static void SetImageBaseUri(AvaloniaObject target, string? value) => target.SetValue(ImageBaseUriProperty, value);

    /// <summary>把图片地址解析为绝对地址：相对路径按 <paramref name="baseUri"/> 解析，绝对文件路径转为 file: 地址。</summary>
    public static string ResolveImageUrl(string url, string? baseUri)
    {
        url = url.Trim();
        if (url.Length == 0 || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme.Length > 1)
        {
            return url;
        }
        if (Path.IsPathRooted(url))
        {
            return new Uri(Path.GetFullPath(url)).AbsoluteUri;
        }
        if (string.IsNullOrEmpty(baseUri))
        {
            return url;
        }
        var root = Uri.TryCreate(baseUri, UriKind.Absolute, out var b) && b.Scheme.Length > 1 ? b : new Uri(Path.GetFullPath(baseUri) + Path.DirectorySeparatorChar);
        if (!root.AbsoluteUri.EndsWith('/'))
        {
            root = new Uri(root.AbsoluteUri + "/");
        }
        return Uri.TryCreate(root, url, out var resolved) ? resolved.AbsoluteUri : url;
    }

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

/// <summary>HTML 颜色（<c>color</c>、<c>background</c>、<c>&lt;font color&gt;</c>）的处理方式。</summary>
public enum HtmlColorMode
{
    /// <summary>使用作者颜色；文字颜色与所在背景对比度不足时（例如暗色主题中的 #333）改用主题文字色。</summary>
    Readable,

    /// <summary>完全按作者颜色。</summary>
    Exact,

    /// <summary>忽略所有作者颜色，只用主题颜色。</summary>
    Ignore,
}
