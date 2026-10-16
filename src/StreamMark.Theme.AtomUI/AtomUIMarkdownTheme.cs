using Avalonia.Styling;
using AtomUI.Theme.Resources;
using StreamMark.Avalonia;

namespace StreamMark.Theme.AtomUI;

/// <summary>
/// AtomUI 主题适配：把 StreamMark 的 <c>Md.*</c> 资源指向 AtomUI 设计 token。在 <c>StreamMarkTheme</c> 之后加入应用样式。
/// </summary>
/// <remarks>
/// 资源值是 <see cref="MarkdownResourceAlias"/>，由 StreamMark 从 Markdown 控件本身解析，
/// 因此能找到 AtomUI 注册在窗口 / 主题作用域内的 token，并在切换明暗主题、主色时跟随。
/// 字体与字号不在这里映射：未设置 <c>Md.FontFamily</c> / <c>Md.FontSize</c> 时 StreamMark 沿用控件继承的字体。
/// </remarks>
public class AtomUIMarkdownTheme : Styles
{
    public AtomUIMarkdownTheme()
    {
        Map(MarkdownResourceKeys.Foreground, SharedTokenKind.ColorText);
        Map(MarkdownResourceKeys.SecondaryForeground, SharedTokenKind.ColorTextSecondary);
        Map(MarkdownResourceKeys.LinkForeground, SharedTokenKind.ColorLink);
        Map(MarkdownResourceKeys.SelectionBackground, SharedTokenKind.ColorPrimaryBgHover);
        Map(MarkdownResourceKeys.CodeBackground, SharedTokenKind.ColorFillQuaternary);
        Map(MarkdownResourceKeys.CodeBorderBrush, SharedTokenKind.ColorBorderSecondary);
        Map(MarkdownResourceKeys.InlineCodeBackground, SharedTokenKind.ColorFillTertiary);
        Map(MarkdownResourceKeys.InlineCodeForeground, SharedTokenKind.ColorText);
        Map(MarkdownResourceKeys.ListMarkerForeground, SharedTokenKind.ColorTextSecondary);
        Map(MarkdownResourceKeys.QuoteBorderBrush, SharedTokenKind.ColorBorder);
        Map(MarkdownResourceKeys.TableBorderBrush, SharedTokenKind.ColorBorderSecondary);
        Map(MarkdownResourceKeys.TableHeaderBackground, SharedTokenKind.ColorFillQuaternary);
        Map(MarkdownResourceKeys.HoverBackground, SharedTokenKind.ColorFillSecondary);
    }

    private void Map(string key, SharedTokenKind token) => Resources[key] = new MarkdownResourceAlias(token);
}
