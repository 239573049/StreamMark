using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using StreamMark.Avalonia.Extensions;

namespace StreamMark.Avalonia.Rendering;

/// <summary>渐显与光标共用的单调时钟。</summary>
public static class StreamClock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();

    public static TimeSpan Now => Stopwatch.GetElapsedTime(Origin);
}

/// <summary>行内公式：按基线对齐嵌入文本行。</summary>
internal sealed class MathRun(MathLayout layout, double fontSize, IBrush foreground, TextRunProperties properties) : DrawableTextRun
{
    // 公式两侧留一点间距，避免紧贴相邻文字
    private readonly double _padding = fontSize * 0.08;

    public override TextRunProperties Properties { get; } = properties;

    public override Size Size => new(layout.Width * fontSize + _padding * 2, (layout.Ascent + layout.Descent) * fontSize);

    public override double Baseline => layout.Ascent * fontSize;

    public override void Draw(DrawingContext drawingContext, Point origin) =>
        layout.Draw(drawingContext, new Point(origin.X + _padding, origin.Y), fontSize, foreground);
}

/// <summary>以独立排版的文字绘制的对象（公式无法排版时显示源码、图片的替代文字等）。</summary>
internal sealed class TextObjectRun : DrawableTextRun
{
    private readonly TextLayout _layout;
    private readonly IBrush? _background;
    private readonly double _padding;

    public TextObjectRun(string text, TextRunProperties properties, IBrush? background, double padding = 0)
    {
        Properties = properties;
        _background = background;
        _padding = padding;
        _layout = new TextLayout(text, properties.Typeface, properties.FontRenderingEmSize, properties.ForegroundBrush);
    }

    public override TextRunProperties Properties { get; }

    public override Size Size => new(_layout.WidthIncludingTrailingWhitespace + _padding * 2, _layout.Height);

    public override double Baseline => _layout.TextLines.Count > 0 ? _layout.TextLines[0].Baseline : _layout.Height;

    public override void Draw(DrawingContext drawingContext, Point origin)
    {
        if (_background is not null)
        {
            drawingContext.DrawRectangle(_background, null, new Rect(origin, Size), 3, 3);
        }
        _layout.Draw(drawingContext, new Point(origin.X + _padding, origin.Y));
    }
}

/// <summary>行内图片：加载完成前显示占位框。</summary>
internal sealed class ImageRun : DrawableTextRun
{
    private readonly IImage? _image;
    private readonly Size _size;
    private readonly TextObjectRun? _placeholder;
    private readonly IBrush _border;

    public ImageRun(IImage? image, string alt, double maxWidth, TextRunProperties properties, IBrush border, IBrush placeholderBackground)
    {
        Properties = properties;
        _image = image;
        _border = border;
        if (image is not null)
        {
            var natural = image.Size;
            var scale = 1.0;
            if (double.IsFinite(maxWidth) && natural.Width > maxWidth && maxWidth > 0)
            {
                scale = maxWidth / natural.Width;
            }
            _size = new Size(Math.Max(1, natural.Width * scale), Math.Max(1, natural.Height * scale));
        }
        else
        {
            _placeholder = new TextObjectRun(string.IsNullOrWhiteSpace(alt) ? "🖼" : "🖼 " + alt, properties, placeholderBackground, 6);
            _size = _placeholder.Size;
        }
    }

    public override TextRunProperties Properties { get; }

    public override Size Size => _size;

    public override double Baseline => _placeholder?.Baseline ?? _size.Height;

    public override void Draw(DrawingContext drawingContext, Point origin)
    {
        if (_image is not null)
        {
            drawingContext.DrawImage(_image, new Rect(_image.Size), new Rect(origin, _size));
            return;
        }
        _placeholder!.Draw(drawingContext, origin);
        drawingContext.DrawRectangle(null, new Pen(_border, 1), new Rect(origin, _size).Deflate(0.5), 3, 3);
    }
}

/// <summary>任务列表复选框（只读）。</summary>
internal sealed class CheckboxRun(bool isChecked, double fontSize, IBrush foreground, IBrush accent, TextRunProperties properties) : DrawableTextRun
{
    private readonly double _box = Math.Round(fontSize * 0.95);

    public override TextRunProperties Properties { get; } = properties;

    public override Size Size => new(_box + fontSize * 0.45, _box);

    public override double Baseline => _box * 0.85;

    public override void Draw(DrawingContext drawingContext, Point origin)
    {
        var rect = new Rect(origin.X, origin.Y, _box, _box).Deflate(1);
        if (isChecked)
        {
            drawingContext.DrawRectangle(accent, null, rect, 3, 3);
            var pen = new Pen(Brushes.White, Math.Max(1.5, _box / 9), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(rect.X + rect.Width * 0.24, rect.Y + rect.Height * 0.52), false);
                g.LineTo(new Point(rect.X + rect.Width * 0.43, rect.Y + rect.Height * 0.70));
                g.LineTo(new Point(rect.X + rect.Width * 0.77, rect.Y + rect.Height * 0.32));
                g.EndFigure(false);
            }
            drawingContext.DrawGeometry(null, pen, geometry);
        }
        else
        {
            drawingContext.DrawRectangle(null, new Pen(foreground, 1.2), rect, 3, 3);
        }
    }
}
