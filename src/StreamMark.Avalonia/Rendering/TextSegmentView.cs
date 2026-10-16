using System.Text;
using Avalonia.Media.Fonts.Tables.Cmap;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using StreamMark.Avalonia.Selection;
using StreamMark.Core.Model;

namespace StreamMark.Avalonia.Rendering;

/// <summary>
/// 一个文本片段（段落、标题、表格单元格等）：用一个 <see cref="TextLayout"/> 自绘，
/// 行内样式通过自定义 <see cref="ITextSource"/> 提供，公式 / 图片 / 复选框以 <see cref="DrawableTextRun"/> 嵌入。
/// 选区高亮只绘制矩形，不触发重新排版。
/// </summary>
public sealed class TextSegmentView : Control, ISelectableSegment
{
    private InlineContent _content = InlineContent.Empty;
    private RenderContext? _context;
    private double _fontSize = 14;
    private FontWeight _fontWeight = FontWeight.Normal;
    private IBrush? _foreground;
    private TextAlignment _alignment = TextAlignment.Left;
    private TextWrapping _wrapping = TextWrapping.Wrap;

    private TextLayout? _layout;
#pragma warning disable CS0618 // TextRunCache 是 Avalonia 12 的预览 API（[Unstable]），只在这里使用
    // 已整形文本段的缓存：流式追加时只为变化处之后的文本重新整形
    private readonly TextRunCache _runCache = new();
#pragma warning restore CS0618

    /// <summary>是否使用整形缓存（测试与基准对比用）。</summary>
    internal static bool UseRunCache { get; set; } = true;
    private double _layoutWidth = double.NaN;
    private int _hoverLink = -1;
    private bool _hoverClickable;
    private bool _frameRequested;

    public TextSegmentView()
    {
        ClipToBounds = false;
    }

    /// <summary>片段身份（选区坐标）。</summary>
    public SegmentKey Key { get; private set; }

    /// <summary>是否是文档最后一个片段（流式时绘制渐显和光标）。</summary>
    public bool IsDocumentTail { get; set; }

    public InlineContent Content => _content;

    string ISelectableSegment.Text => _content.Text;

    Control ISelectableSegment.View => this;

    public TextLayout? Layout => _layout;

    /// <summary>链接点击（冒泡）。</summary>
    public static readonly RoutedEvent<LinkClickedEventArgs> LinkClickedEvent =
        RoutedEvent.Register<TextSegmentView, LinkClickedEventArgs>(nameof(LinkClicked), RoutingStrategies.Bubble);

    public event EventHandler<LinkClickedEventArgs>? LinkClicked
    {
        add => AddHandler(LinkClickedEvent, value);
        remove => RemoveHandler(LinkClickedEvent, value);
    }

    /// <summary>可点击的行内组件被点击（冒泡），例如引用标签。</summary>
    public static readonly RoutedEvent<ComponentClickedEventArgs> ComponentClickedEvent =
        RoutedEvent.Register<TextSegmentView, ComponentClickedEventArgs>(nameof(ComponentClicked), RoutingStrategies.Bubble);

    public event EventHandler<ComponentClickedEventArgs>? ComponentClicked
    {
        add => AddHandler(ComponentClickedEvent, value);
        remove => RemoveHandler(ComponentClickedEvent, value);
    }

    /// <summary>设置内容与外观；内容或外观未变时不重新排版。</summary>
    public void Set(RenderContext context, SegmentKey key, InlineContent content, double fontSize, FontWeight weight = default,
        IBrush? foreground = null, TextAlignment alignment = TextAlignment.Left, TextWrapping wrapping = TextWrapping.Wrap)
    {
        if (weight == default)
        {
            weight = FontWeight.Normal;
        }
        var keyChanged = Key != key;
        Key = key;
        var changed = !ReferenceEquals(_context, context) || !ReferenceEquals(_content, content) && _content != content ||
                      _fontSize != fontSize || _fontWeight != weight || !Equals(_foreground, foreground) || _alignment != alignment || _wrapping != wrapping;
        if (!changed)
        {
            if (keyChanged)
            {
                InvalidateVisual();
            }
            return;
        }
        if (_context is not null && !ReferenceEquals(_context.Selection, context.Selection))
        {
            _context.Selection.Changed -= OnSelectionChanged;
            if (IsAttached)
            {
                context.Selection.Changed += OnSelectionChanged;
            }
        }
        else if (_context is null && IsAttached)
        {
            context.Selection.Changed += OnSelectionChanged;
        }
        // 只追加且样式不变时，保留差异点之前的整形结果
        var appendOnly = ReferenceEquals(_context, context) && _fontSize == fontSize && _fontWeight == weight && Equals(_foreground, foreground);
        var from = appendOnly ? FirstDifference(_content, content) : 0;
#pragma warning disable CS0618
        if (from <= 0)
        {
            _runCache.Invalidate();
        }
        else
        {
            _runCache.InvalidateFrom(from);
        }
#pragma warning restore CS0618
        _context = context;
        _content = content;
        _fontSize = fontSize;
        _fontWeight = weight;
        _foreground = foreground;
        _alignment = alignment;
        _wrapping = wrapping;
        InvalidateLayout();
    }

    private bool IsAttached => VisualRoot is not null;

    // 新旧内容第一个不同的位置：文本公共前缀，且该位置之前的样式区间、对象完全相同
    private static int FirstDifference(InlineContent old, InlineContent next)
    {
        var limit = old.Text.AsSpan().CommonPrefixLength(next.Text);
        foreach (var span in old.Spans)
        {
            if (span.Start >= limit)
            {
                break;
            }
            var match = next.Spans.FirstOrDefault(s => s.Start == span.Start);
            if (match != span)
            {
                // 区间被延长（如粗体继续增长）时从区间开头重新整形
                limit = Math.Min(limit, span.Start);
            }
        }
        foreach (var span in next.Spans)
        {
            if (span.Start < limit && !old.Spans.Contains(span))
            {
                limit = Math.Min(limit, span.Start);
            }
        }
        foreach (var obj in next.Objects)
        {
            if (obj.Offset < limit && !old.Objects.Contains(obj))
            {
                limit = Math.Min(limit, obj.Offset);
            }
        }
        return limit;
    }

    private void InvalidateLayout()
    {
        _layout?.Dispose();
        _layout = null;
        _layoutWidth = double.NaN;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_context is not null)
        {
            _context.Selection.Changed += OnSelectionChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_context is not null)
        {
            _context.Selection.Changed -= OnSelectionChanged;
        }
    }

    private void OnSelectionChanged() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = EnsureLayout(availableSize.Width);
        if (layout is null)
        {
            return default;
        }
        var width = _wrapping == TextWrapping.Wrap && double.IsFinite(availableSize.Width)
            ? Math.Min(layout.WidthIncludingTrailingWhitespace, availableSize.Width)
            : layout.WidthIncludingTrailingWhitespace;
        return new Size(Math.Ceiling(width), Math.Ceiling(layout.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // 对齐方式需要按最终宽度排版
        if (_alignment != TextAlignment.Left && _wrapping == TextWrapping.Wrap)
        {
            EnsureLayout(finalSize.Width);
        }
        return finalSize;
    }

    private TextLayout? EnsureLayout(double width)
    {
        if (_context is null)
        {
            return null;
        }
        var maxWidth = _wrapping == TextWrapping.Wrap && double.IsFinite(width) ? Math.Max(width, 1) : double.PositiveInfinity;
        if (_layout is not null && (_layoutWidth.Equals(maxWidth) || _wrapping == TextWrapping.NoWrap))
        {
            return _layout;
        }
        _layout?.Dispose();
        var source = new InlineTextSource(this, _context, _content, _fontSize, _fontWeight, _foreground ?? _context.Style.Foreground, maxWidth);
        var paragraph = new GenericTextParagraphProperties(source.DefaultProperties, _alignment, _wrapping,
            lineHeight: _context.Style.LineHeight * (_fontSize / _context.Style.FontSize));
#pragma warning disable CS0618
        _layout = new TextLayout(source, paragraph, maxWidth: maxWidth, textRunCache: UseRunCache ? _runCache : null);
#pragma warning restore CS0618
        _layoutWidth = maxWidth;
        return _layout;
    }

    /// <summary>图片等异步资源就绪后重新排版。</summary>
    internal void OnResourceLoaded() => InvalidateLayout();

    public override void Render(DrawingContext context)
    {
        var layout = _layout;
        if (layout is null || _context is null)
        {
            return;
        }

        if (_context.Selection.GetSegmentRange(Key, _content.Text.Length) is { } selected)
        {
            foreach (var rect in layout.HitTestTextRange(selected.Start, selected.End - selected.Start))
            {
                context.FillRectangle(_context.Style.SelectionBackground, rect);
            }
        }

        var streaming = IsDocumentTail ? _context.Streaming : null;
        var fading = streaming is not null ? streaming.GetFadingTail(StreamClock.Now) : (0, 1.0, 1.0);
        if (fading.Item1 > 0 && _content.Text.Length > 0)
        {
            DrawWithFade(context, layout, fading.Item1, fading.Item2, fading.Item3);
            RequestNextFrame();
        }
        else
        {
            layout.Draw(context, default);
        }

        DrawStrikethrough(context, layout);

        if (streaming is { IsActive: true, ShowCaret: true })
        {
            var end = layout.HitTestTextPosition(_content.Text.Length);
            var height = Math.Min(end.Height, _fontSize * 1.25);
            var caret = new Rect(end.Right + 2, end.Bottom - height - (end.Height - height) / 2, Math.Max(2, _fontSize * 0.45), height);
            context.FillRectangle(_foreground ?? _context.Style.Foreground, caret);
        }
    }

    private void DrawStrikethrough(DrawingContext context, TextLayout layout)
    {
        Pen? pen = null;
        foreach (var span in _content.Spans)
        {
            if (!span.Style.HasFlag(InlineStyle.Strikethrough))
            {
                continue;
            }
            pen ??= new Pen(_foreground ?? _context!.Style.Foreground, Math.Max(1, _fontSize / 14));
            foreach (var rect in layout.HitTestTextRange(span.Start, span.Length))
            {
                var y = Math.Round(rect.Top + rect.Height * 0.54) + 0.5;
                context.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
            }
        }
    }

    // 末尾最近出现的字符按出现时间从透明渐变到不透明：
    // 渐显区域之外正常绘制，区域之内用不透明度蒙版再绘制一次
    private void DrawWithFade(DrawingContext context, TextLayout layout, int count, double oldest, double newest)
    {
        var start = Math.Max(0, _content.Text.Length - count);
        var first = layout.HitTestTextPosition(start);
        var bounds = new Rect(0, 0, Math.Max(Bounds.Width, layout.WidthIncludingTrailingWhitespace) + _fontSize * 2, Math.Max(Bounds.Height, layout.Height));
        var lineRect = new Rect(first.X, first.Y, bounds.Width - first.X, first.Height);
        var below = new Rect(0, first.Bottom, bounds.Width, Math.Max(0, bounds.Height - first.Bottom));
        var fadeGeometry = new GeometryGroup { Children = { new RectangleGeometry(lineRect), new RectangleGeometry(below) } };
        var solidGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), fadeGeometry);

        using (context.PushGeometryClip(solidGeometry))
        {
            layout.Draw(context, default);
        }

        var endX = layout.HitTestTextPosition(_content.Text.Length).Right;
        var mask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(first.X, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(Math.Max(endX, first.X + 1), 0, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(oldest * 255), 0, 0, 0), 0),
                new GradientStop(Color.FromArgb((byte)(newest * 255), 0, 0, 0), 1),
            },
        };
        using (context.PushGeometryClip(fadeGeometry))
        using (context.PushOpacityMask(mask, bounds))
        {
            layout.Draw(context, default);
        }
    }

    private void RequestNextFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateVisual();
        });
    }

    /// <summary>本地坐标 → 字符偏移。</summary>
    public int HitTest(Point point)
    {
        if (_layout is null)
        {
            return 0;
        }
        var result = _layout.HitTestPoint(point);
        return Math.Clamp(result.TextPosition, 0, _content.Text.Length);
    }

    /// <summary>字符偏移 → 本地矩形。</summary>
    public Rect GetCharacterBounds(int offset) => _layout?.HitTestTextPosition(Math.Clamp(offset, 0, _content.Text.Length)) ?? default;

    /// <summary>指针处可点击的组件（成对组件的文字或自闭合组件对象）。</summary>
    private (string Name, ComponentAttributes Attributes)? ComponentAt(Point point)
    {
        if (_layout is null || _context is null || (_content.Components.IsEmpty && _content.Objects.IsEmpty))
        {
            return null;
        }
        var hit = _layout.HitTestPoint(point);
        if (!hit.IsInside)
        {
            return null;
        }
        var index = hit.TextPosition;
        foreach (var obj in _content.Objects)
        {
            if (obj.Kind == InlineObjectKind.Component && (obj.Offset == index || obj.Offset == index - 1 && hit.IsTrailing)
                && _context.Plugins.FindInlineComponent(obj.Value) is { } renderer && renderer.IsClickable(obj.Attributes))
            {
                return (obj.Value, obj.Attributes);
            }
        }
        foreach (var span in _content.Spans)
        {
            if (span.ComponentIndex >= 0 && index >= span.Start && index < span.End)
            {
                var component = _content.Components[span.ComponentIndex];
                if (_context.Plugins.FindInlineComponent(component.Name) is { } renderer && renderer.IsClickable(component.Attributes))
                {
                    return (component.Name, component.Attributes);
                }
            }
        }
        return null;
    }

    private int LinkAt(Point point)
    {
        if (_layout is null || _content.Links.IsEmpty)
        {
            return -1;
        }
        var hit = _layout.HitTestPoint(point);
        if (!hit.IsInside)
        {
            return -1;
        }
        var index = hit.TextPosition;
        foreach (var span in _content.Spans)
        {
            if (span.LinkIndex >= 0 && index >= span.Start && index < span.End)
            {
                return span.LinkIndex;
            }
        }
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var link = LinkAt(position);
        var clickable = link >= 0 || ComponentAt(position) is not null;
        if (link != _hoverLink || clickable != _hoverClickable)
        {
            _hoverLink = link;
            _hoverClickable = clickable;
            Cursor = clickable ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverLink = -1;
        _hoverClickable = false;
        Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }
        // 拖拽选择时不触发链接 / 组件
        if (_context is not { Selection.IsEmpty: true })
        {
            return;
        }
        var position = e.GetPosition(this);
        if (ComponentAt(position) is { } component)
        {
            RaiseEvent(new ComponentClickedEventArgs(ComponentClickedEvent, this, component.Name, component.Attributes));
            e.Handled = true;
            return;
        }
        var link = LinkAt(position);
        if (link >= 0)
        {
            var target = _content.Links[link];
            RaiseEvent(new LinkClickedEventArgs(LinkClickedEvent, this, target.Url, target.Title));
            e.Handled = true;
        }
    }

    /// <summary>为 <see cref="TextLayout"/> 提供带样式的文本段和嵌入对象。</summary>
    private sealed class InlineTextSource : ITextSource
    {
        private readonly TextSegmentView _owner;
        private readonly RenderContext _context;
        private readonly InlineContent _content;
        private readonly double _fontSize;
        private readonly FontWeight _weight;
        private readonly IBrush _foreground;
        private readonly double _maxWidth;
        private readonly Dictionary<(InlineStyle, int), TextRunProperties> _properties = [];

        public InlineTextSource(TextSegmentView owner, RenderContext context, InlineContent content, double fontSize, FontWeight weight, IBrush foreground, double maxWidth)
        {
            _owner = owner;
            _context = context;
            _content = content;
            _fontSize = fontSize;
            _weight = weight;
            _foreground = foreground;
            _maxWidth = maxWidth;
            DefaultProperties = PropertiesFor(InlineStyle.None);
        }

        public TextRunProperties DefaultProperties { get; }

        public TextRun? GetTextRun(int textSourceIndex)
        {
            var text = _content.Text;
            if (textSourceIndex >= text.Length)
            {
                return null;
            }

            var end = text.Length;
            foreach (var obj in _content.Objects)
            {
                if (obj.Offset == textSourceIndex)
                {
                    return CreateObjectRun(obj);
                }
                if (obj.Offset > textSourceIndex)
                {
                    end = Math.Min(end, obj.Offset);
                    break;
                }
            }

            var style = InlineStyle.None;
            var component = -1;
            foreach (var span in _content.Spans)
            {
                if (span.End <= textSourceIndex)
                {
                    continue;
                }
                if (span.Start > textSourceIndex)
                {
                    end = Math.Min(end, span.Start);
                    break;
                }
                style = span.Style;
                component = span.ComponentIndex;
                end = Math.Min(end, span.End);
                break;
            }

            var properties = PropertiesFor(style, component);
            return CreateCharacters(text, textSourceIndex, end, properties, style, component);
        }

        // 回退字体：Avalonia 在为缺字的文字找回退字体时，会优先复用"上一段"的回退字体而不检查字重 / 字形，
        // 导致"中文 **粗体**"中的粗体沿用常规字重的回退字体（反之亦然）。这里自己按本段的字重、字形解析回退字体，
        // 让每段文字的主字体就能覆盖其字符，从而不进入那条复用路径。
        private TextRun CreateCharacters(string text, int start, int end, TextRunProperties properties, InlineStyle style, int component)
        {
            var map = GlyphMap(properties);
            var first = Rune.GetRuneAt(text, start);
            if (!Covered(map, first))
            {
                var length = 0;
                while (start + length < end)
                {
                    var rune = Rune.GetRuneAt(text, start + length);
                    if (Covered(map, rune))
                    {
                        break;
                    }
                    length += rune.Utf16SequenceLength;
                }
                var typeface = properties.Typeface;
                if (FontManager.Current.TryMatchCharacter(first.Value, typeface.Style, typeface.Weight, typeface.Stretch, typeface.FontFamily, null, out var fallback))
                {
                    return new TextCharacters(text.AsMemory(start, length), FallbackProperties(properties, style, component, fallback));
                }
                return new TextCharacters(text.AsMemory(start, length), properties);
            }
            // 主字体能覆盖的前缀：截到第一个缺字处，缺字部分由下一次调用按上面的规则处理
            var covered = first.Utf16SequenceLength;
            while (start + covered < end)
            {
                var rune = Rune.GetRuneAt(text, start + covered);
                if (!Covered(map, rune))
                {
                    break;
                }
                covered += rune.Utf16SequenceLength;
            }
            return new TextCharacters(text.AsMemory(start, covered), properties);
        }

        private static bool Covered(CharacterToGlyphMap? map, Rune rune) =>
            map is null || Rune.IsWhiteSpace(rune) || rune.Value < 0x20 || rune.Value == InlineObject.Placeholder || map.Value.ContainsGlyph(rune.Value);

        private readonly Dictionary<TextRunProperties, CharacterToGlyphMap?> _maps = [];
        private readonly Dictionary<(InlineStyle, int, string), TextRunProperties> _fallbacks = [];

        private CharacterToGlyphMap? GlyphMap(TextRunProperties properties)
        {
            if (!_maps.TryGetValue(properties, out var map))
            {
                try
                {
                    map = properties.Typeface.GlyphTypeface.CharacterToGlyphMap;
                }
                catch (Exception)
                {
                    map = null;
                }
                _maps[properties] = map;
            }
            return map;
        }

        // 粗体回退：系统字体可能没有 700 字重（如苹方只有中粗 600），而按 700 匹配会落回常规字重；
        // 依次尝试邻近字重，取第一个真正够粗（不低于 600 或带合成粗体）的字形
        private static Typeface HeavyEnough(FontFamily family, Typeface requested)
        {
            var typeface = new Typeface(family, requested.Style, requested.Weight, requested.Stretch);
            if ((int)requested.Weight < 600)
            {
                return typeface;
            }
            foreach (var weight in new[] { (int)requested.Weight, 600, 800, 900 })
            {
                var candidate = new Typeface(family, requested.Style, (FontWeight)weight, requested.Stretch);
                try
                {
                    var glyphs = candidate.GlyphTypeface;
                    if ((int)glyphs.Weight >= 600 || glyphs.FontSimulations.HasFlag(FontSimulations.Bold))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                }
            }
            return typeface;
        }

        private TextRunProperties FallbackProperties(TextRunProperties properties, InlineStyle style, int component, Typeface fallback)
        {
            var key = (style, component, fallback.FontFamily.Name);
            if (!_fallbacks.TryGetValue(key, out var result))
            {
                result = new GenericTextRunProperties(HeavyEnough(fallback.FontFamily, properties.Typeface),
                    properties.FontRenderingEmSize, properties.TextDecorations, properties.ForegroundBrush, properties.BackgroundBrush, properties.BaselineAlignment);
                _fallbacks[key] = result;
            }
            return result;
        }

        private TextRun CreateObjectRun(InlineObject obj)
        {
            var style = _context.Style;
            var properties = DefaultProperties;
            switch (obj.Kind)
            {
                case InlineObjectKind.Math:
                    {
                        if (_context.Services.Registry.Math?.Layout(obj.Value, obj.Flag, out _) is { } layout)
                        {
                            return new MathRun(layout, _fontSize * 1.05, _foreground, properties);
                        }
                        // 无法排版：显示源码
                        var code = PropertiesFor(InlineStyle.Code);
                        return new TextObjectRun(obj.Flag ? $"\\[{obj.Value}\\]" : $"\\({obj.Value}\\)", code, style.InlineCodeBackground, 3);
                    }
                case InlineObjectKind.Image:
                    {
                        if (Core.Remend.Remend.IncompleteImageUrl == obj.Value)
                        {
                            return new ImageRun(null, obj.Alt ?? "", _maxWidth, properties, style.CodeBorderBrush, style.InlineCodeBackground);
                        }
                        if (ImageCache.TryGet(obj.Value, out var image))
                        {
                            return new ImageRun(image, obj.Alt ?? "", _maxWidth, properties, style.CodeBorderBrush, style.InlineCodeBackground);
                        }
                        var owner = _owner;
                        ImageCache.Request(obj.Value, _context.Services.ImageLoader, owner.OnResourceLoaded);
                        return new ImageRun(null, obj.Alt ?? "", _maxWidth, properties, style.CodeBorderBrush, style.InlineCodeBackground);
                    }
                case InlineObjectKind.TaskCheckbox:
                    return new CheckboxRun(obj.Flag, _fontSize, style.SecondaryForeground, style.LinkForeground, properties);
                case InlineObjectKind.Component:
                    {
                        var renderer = _context.Plugins.FindInlineComponent(obj.Value);
                        var context = new Plugins.InlineComponentContext(style, _fontSize, _foreground, properties, _maxWidth);
                        return renderer?.CreateRun(obj, context) ?? new TextObjectRun("", properties, null);
                    }
                default:
                    return new TextCharacters(InlineObject.Placeholder.ToString(), properties);
            }
        }

        private TextRunProperties PropertiesFor(InlineStyle style, int component = -1)
        {
            if (_properties.TryGetValue((style, component), out var cached))
            {
                return cached;
            }
            var s = _context.Style;
            var isCode = style.HasFlag(InlineStyle.Code) || style.HasFlag(InlineStyle.Keyboard);
            var family = isCode ? s.MonospaceFontFamily : s.FontFamily;
            var weight = style.HasFlag(InlineStyle.Bold) ? FontWeight.Bold : _weight;
            var fontStyle = style.HasFlag(InlineStyle.Italic) ? FontStyle.Italic : FontStyle.Normal;
            var size = _fontSize;
            if (isCode)
            {
                size *= 0.9;
            }
            var baseline = BaselineAlignment.Baseline;
            if (style.HasFlag(InlineStyle.Superscript))
            {
                size *= 0.75;
                baseline = BaselineAlignment.Superscript;
            }
            else if (style.HasFlag(InlineStyle.Subscript))
            {
                size *= 0.75;
                baseline = BaselineAlignment.Subscript;
            }

            // 行内代码 / 按键使用独立的文字色（未配置时等于正文色）；链接样式优先
            IBrush foreground = isCode && ReferenceEquals(_foreground, s.Foreground) ? s.InlineCodeForeground : _foreground;
            TextDecorationCollection? decorations = null;
            if (style.HasFlag(InlineStyle.Link) || style.HasFlag(InlineStyle.FootnoteReference))
            {
                foreground = s.LinkForeground;
                if (!style.HasFlag(InlineStyle.FootnoteReference))
                {
                    decorations = TextDecorations.Underline;
                }
            }
            else if (style.HasFlag(InlineStyle.IncompleteLink))
            {
                foreground = s.SecondaryForeground;
            }
            // 删除线在 Render 中自行绘制：部分字体（及无头环境的回退字体）缺少删除线度量
            if (style.HasFlag(InlineStyle.Underline))
            {
                decorations = TextDecorations.Underline;
            }
            IBrush? background = isCode ? s.InlineCodeBackground : style.HasFlag(InlineStyle.Mark) ? s.MarkBackground : null;

            // 成对行内组件（如 <cite>）的样式覆盖
            if (component >= 0 && component < _content.Components.Length &&
                _context.Plugins.FindInlineComponent(_content.Components[component].Name)?.GetStyle(_content.Components[component], s) is { } custom)
            {
                foreground = custom.Foreground ?? foreground;
                background = custom.Background ?? background;
                weight = custom.Weight ?? weight;
                if (custom.Underline)
                {
                    decorations = TextDecorations.Underline;
                }
            }

            var properties = new GenericTextRunProperties(
                new Typeface(family, fontStyle, weight),
                size,
                decorations,
                foreground,
                background,
                baseline);
            _properties[(style, component)] = properties;
            return properties;
        }
    }
}

public sealed class LinkClickedEventArgs(RoutedEvent routedEvent, object source, string url, string? title) : RoutedEventArgs(routedEvent, source)
{
    public string Url { get; } = url;

    public string? Title { get; } = title;
}

public sealed class ComponentClickedEventArgs(RoutedEvent routedEvent, object source, string name, ComponentAttributes attributes) : RoutedEventArgs(routedEvent, source)
{
    public string Name { get; } = name;

    public ComponentAttributes Attributes { get; } = attributes;
}
