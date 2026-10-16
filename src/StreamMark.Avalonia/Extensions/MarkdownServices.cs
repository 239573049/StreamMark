using Avalonia;
using StreamMark.Core.Highlighting;

namespace StreamMark.Avalonia.Extensions;

/// <summary>
/// 渲染扩展的集合：公式、代码高亮、图表、图片。通过可继承的附加属性 <see cref="ServicesProperty"/>
/// 设置在窗口或任意祖先上，其下所有 Markdown 视图共用。
/// </summary>
public sealed class MarkdownServices
{
    public static MarkdownServices Default { get; } = new();

    public IMathRenderer? Math { get; init; }

    public ISyntaxHighlighter? Highlighter { get; init; }

    public IReadOnlyList<IDiagramRenderer> Diagrams { get; init; } = [];

    public IImageLoader ImageLoader { get; init; } = new DataUriImageLoader();

    public IDiagramRenderer? FindDiagramRenderer(string? language)
    {
        foreach (var renderer in Diagrams)
        {
            if (renderer.CanRender(language))
            {
                return renderer;
            }
        }
        return null;
    }

    public static readonly AttachedProperty<MarkdownServices> ServicesProperty =
        AvaloniaProperty.RegisterAttached<MarkdownServices, AvaloniaObject, MarkdownServices>("Services", Default, inherits: true);

    public static MarkdownServices GetServices(AvaloniaObject target) => target.GetValue(ServicesProperty);

    public static void SetServices(AvaloniaObject target, MarkdownServices value) => target.SetValue(ServicesProperty, value);
}
