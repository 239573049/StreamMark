using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace StreamMark.Avalonia.Rendering;

/// <summary>
/// 全屏图片查看：显示在窗口的覆盖层上，初始按窗口大小适配；滚轮缩放（以指针为中心）、拖动平移、
/// 双击在适配与 100% 之间切换，Esc 或点击背景关闭。不依赖宿主主题模板。
/// </summary>
public sealed class ImageViewer : Control
{
    private readonly IImage _image;
    private readonly TextLayout? _caption;
    private double _scale = double.NaN;
    private Vector _offset;
    private Point? _dragStart;
    private Vector _dragOrigin;
    private bool _moved;

    private ImageViewer(IImage image, string? caption)
    {
        _image = image;
        Focusable = true;
        ClipToBounds = true;
        if (!string.IsNullOrWhiteSpace(caption))
        {
            _caption = new TextLayout(caption, Typeface.Default, 13, Brushes.White);
        }
    }

    /// <summary>在 <paramref name="anchor"/> 所在窗口的覆盖层上打开查看器；没有覆盖层时返回 null。</summary>
    public static ImageViewer? Show(Visual anchor, IImage image, string? caption = null)
    {
        if (OverlayLayer.GetOverlayLayer(anchor) is not { } layer)
        {
            return null;
        }
        var viewer = new ImageViewer(image, caption)
        {
            Width = layer.Bounds.Width,
            Height = layer.Bounds.Height,
        };
        layer.Children.Add(viewer);
        viewer.Focus();
        return viewer;
    }

    public void Close()
    {
        if (Parent is OverlayLayer layer)
        {
            layer.Children.Remove(this);
        }
    }

    private double FitScale
    {
        get
        {
            var size = _image.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return 1;
            }
            return Math.Min(1, Math.Min((Bounds.Width - 80) / size.Width, (Bounds.Height - 100) / size.Height));
        }
    }

    private Rect ImageRect
    {
        get
        {
            if (double.IsNaN(_scale))
            {
                _scale = FitScale;
            }
            var size = _image.Size * _scale;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2) + _offset;
            return new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height);
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0xD9, 0, 0, 0)), new Rect(Bounds.Size));
        context.DrawImage(_image, new Rect(_image.Size), ImageRect);
        if (_caption is not null)
        {
            _caption.Draw(context, new Point((Bounds.Width - _caption.WidthIncludingTrailingWhitespace) / 2, Bounds.Height - _caption.Height - 24));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var before = ImageRect;
        var pointer = e.GetPosition(this);
        var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        var scale = Math.Clamp(_scale * factor, FitScale * 0.25, 8);
        // 以指针位置为中心缩放
        var relative = new Vector((pointer.X - before.Center.X) / _scale, (pointer.Y - before.Center.Y) / _scale);
        _offset += relative * _scale - relative * scale;
        _scale = scale;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount == 2)
        {
            _scale = Math.Abs(_scale - FitScale) < 0.001 ? 1 : FitScale;
            _offset = default;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        _dragOrigin = _offset;
        _moved = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is { } start)
        {
            var delta = e.GetPosition(this) - start;
            _moved |= Math.Abs(delta.X) + Math.Abs(delta.Y) > 3;
            _offset = _dragOrigin + delta;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasDrag = _moved;
        _dragStart = null;
        e.Pointer.Capture(null);
        // 点击图片以外的背景关闭
        if (!wasDrag && !ImageRect.Contains(e.GetPosition(this)))
        {
            Close();
        }
        e.Handled = true;
    }
}
