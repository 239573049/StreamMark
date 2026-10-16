using Avalonia;
using Avalonia.Media;

namespace StreamMark.Avalonia.Extensions;

/// <summary>图表渲染的环境信息。</summary>
public sealed record DiagramRenderContext(bool IsDark, Color Foreground, Color Background, Color Accent, string? FontFamily, double FontSize);

/// <summary>
/// 代码块图表渲染器（如 Mermaid）。代码块闭合后才会调用；<see cref="RenderAsync"/> 可在后台线程执行。
/// </summary>
public interface IDiagramRenderer
{
    /// <summary>是否处理这个围栏语言（如 "mermaid"）。</summary>
    bool CanRender(string? language);

    /// <summary>渲染图表。失败时返回 null 并给出错误信息（界面将回退为显示源码）。</summary>
    Task<DiagramResult?> RenderAsync(string language, string code, DiagramRenderContext context, CancellationToken cancellationToken);
}

/// <summary>渲染好的图表，可按任意尺寸绘制。</summary>
public abstract class DiagramResult : IDisposable
{
    /// <summary>图表的自然尺寸（设备无关像素）。</summary>
    public abstract Size Size { get; }

    /// <summary>错误信息（部分渲染时可用）。</summary>
    public virtual string? Warning => null;

    /// <summary>绘制到目标矩形（保持宽高比由调用方负责）。必须在界面线程调用。</summary>
    public abstract void Draw(DrawingContext context, Rect destination);

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
