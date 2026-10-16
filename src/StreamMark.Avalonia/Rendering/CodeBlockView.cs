using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using StreamMark.Avalonia.Selection;
using StreamMark.Core.Highlighting;
using StreamMark.Core.Model;

namespace StreamMark.Avalonia.Rendering;

/// <summary>
/// 代码块：头部（语言、换行切换、复制）+ 代码区。代码按行排版并缓存，流式追加时只为新行和最后一行重新排版；
/// 语法高亮在后台增量进行，结果回到界面线程后只替换颜色有变化的行。
/// </summary>
public sealed class CodeBlockView : BlockView
{
    private const double HeaderHeight = 34;
    private const double Padding = 14;

    private readonly IconButton _copy = new() { Icon = MarkdownIcons.Copy };
    private readonly IconButton _wrap = new() { Icon = MarkdownIcons.Wrap };
    private readonly CodeTextView _code = new();
    private TextLayout? _language;
    private DispatcherTimer? _copiedTimer;
    private readonly List<(IconButton Button, Plugins.BlockActionDescriptor Action)> _actions = [];
    private const string WrapState = "code.wrap";

    public CodeBlockView()
    {
        AddChild(_code);
        AddChild(_wrap);
        AddChild(_copy);
        _copy.Click += async (_, _) => await CopyAsync();
        _wrap.Click += (_, _) =>
        {
            Wrap = !Wrap;
            Context?.State.Set(TopKey, WrapState, Wrap);
            _wrap.InvalidateVisual();
            InvalidateMeasure();
        };
    }

    public string Code => _code.Text;

    public bool Wrap
    {
        get => _code.Wrap;
        set
        {
            _code.Wrap = value;
            _wrap.IsActive = value;
        }
    }

    protected override void OnUpdate(RenderContext context, MarkdownBlock? previous, MarkdownBlock block, int topKey, ref int segment, bool contextChanged)
    {
        var code = (CodeBlock)block;
        var style = context.Style;
        if (contextChanged || previous is not CodeBlock old || old.Info != code.Info)
        {
            // 头部标题：语言，信息串带路径时显示 "语言 · 路径#L起-止"
            var fence = Plugins.CodeFenceInfo.Parse(code.Info);
            var title = string.IsNullOrEmpty(code.Language) ? "text" : code.Language;
            if (fence.Path is { } path)
            {
                title += " · " + path + (fence.StartLine is { } start ? $"#L{start}" + (fence.EndLine is { } end && end != start ? $"-{end}" : "") : "");
            }
            _language = new TextLayout(title, new Typeface(style.MonospaceFontFamily), style.CodeFontSize * 0.92, style.SecondaryForeground,
                textTrimming: TextTrimming.CharacterEllipsis, maxWidth: 480);
            UpdateActions(context, code);
            foreach (var button in new[] { _copy, _wrap }.Concat(_actions.Select(a => a.Button)))
            {
                button.Foreground = style.SecondaryForeground;
                button.HoverBackground = style.HoverBackground;
                button.InvalidateVisual();
            }
        }
        Wrap = context.State.Get(topKey, WrapState, false);
        _code.Set(context, KeyFor(context, topKey, ref segment), code.Code, code.Language, code.IsOpen);
        InvalidateMeasure();
    }

    // 插件注册的头部操作（例如"应用到文件""在终端运行"）
    private void UpdateActions(RenderContext context, CodeBlock code)
    {
        foreach (var (button, _) in _actions)
        {
            RemoveChild(button);
        }
        _actions.Clear();
        foreach (var action in context.Plugins.BlockActions)
        {
            if (!action.AppliesTo(code))
            {
                continue;
            }
            var button = new IconButton { Icon = action.Icon };
            ToolTip.SetTip(button, action.Label);
            button.Click += async (_, _) =>
            {
                if (Block is CodeBlock current)
                {
                    await action.Execute(new Plugins.BlockActionContext(current, this, Plugins.CodeFenceInfo.Parse(current.Info)));
                }
            };
            _actions.Add((button, action));
            AddChild(button);
        }
    }

    private async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }
        await clipboard.SetTextAsync(_code.Text);
        _copy.Icon = MarkdownIcons.Check;
        _copiedTimer?.Stop();
        _copiedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Normal, (_, _) =>
        {
            _copy.Icon = MarkdownIcons.Copy;
            _copiedTimer?.Stop();
        });
        _copiedTimer.Start();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _copy.Measure(Size.Infinity);
        _wrap.Measure(Size.Infinity);
        foreach (var (button, _) in _actions)
        {
            button.Measure(Size.Infinity);
        }
        var inner = Math.Max(0, availableSize.Width - Padding * 2);
        _code.Measure(new Size(inner, double.PositiveInfinity));
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : _code.DesiredSize.Width + Padding * 2;
        return new Size(width, HeaderHeight + _code.DesiredSize.Height + Padding);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = finalSize.Width - 6;
        foreach (var button in new[] { _copy, _wrap }.Concat(_actions.Select(a => a.Button)))
        {
            x -= button.DesiredSize.Width;
            button.Arrange(new Rect(x, (HeaderHeight - button.DesiredSize.Height) / 2, button.DesiredSize.Width, button.DesiredSize.Height));
            x -= 2;
        }
        _code.Arrange(new Rect(Padding, HeaderHeight, Math.Max(0, finalSize.Width - Padding * 2), _code.DesiredSize.Height));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (Context is null)
        {
            return;
        }
        var style = Context.Style;
        var radius = style.CornerRadius.TopLeft;
        context.DrawRectangle(style.CodeBackground, new Pen(style.CodeBorderBrush, 1), new Rect(Bounds.Size).Deflate(0.5), radius, radius);
        _language?.Draw(context, new Point(Padding, (HeaderHeight - _language.Height) / 2));
    }

    public override IEnumerable<ISelectableSegment> Segments() => [_code];
}

/// <summary>代码文本：等宽、逐行排版并缓存，支持不换行时的横向滚动。</summary>
public sealed class CodeTextView : Control, ISelectableSegment
{
    private sealed class Line
    {
        public required string Text;
        public ImmutableArray<HighlightToken> Tokens;
        public TextLayout? Layout;
        public double Width;
    }

    private readonly List<Line> _lines = [];
    private readonly List<int> _lineStarts = [];
    private RenderContext? _context;
    private string _text = "";
    private string? _language;
    private bool _wrap;
    private double _layoutWidth = double.NaN;
    private double _offsetX;
    private double _contentWidth;
    private bool _hover;

    private IHighlightSession? _session;
    private string? _sessionLanguage;
    private readonly object _highlightLock = new();
    private bool _highlightRunning;
    private string? _highlightPending;

    public SegmentKey Key { get; private set; }

    public string Text => _text;

    public Control View => this;

    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap != value)
            {
                _wrap = value;
                _offsetX = 0;
                foreach (var line in _lines)
                {
                    line.Layout = null;
                }
                _layoutWidth = double.NaN;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    public CodeTextView()
    {
        ClipToBounds = true;
    }

    internal void Set(RenderContext context, SegmentKey key, string code, string? language, bool isOpen)
    {
        var contextChanged = !ReferenceEquals(_context, context);
        if (_context is not null && contextChanged)
        {
            _context.Selection.Changed -= InvalidateVisual;
        }
        if (contextChanged && VisualRoot is not null)
        {
            context.Selection.Changed += InvalidateVisual;
        }
        var styleChanged = _context is null || !ReferenceEquals(_context.Style, context.Style) || !ReferenceEquals(_context.Services, context.Services);
        _context = context;
        Key = key;
        if (styleChanged || language != _language)
        {
            _session?.Dispose();
            _session = null;
            _sessionLanguage = null;
            _language = language;
            _lines.Clear();
            _lineStarts.Clear();
            _text = "";
        }
        if (code == _text && _lines.Count > 0)
        {
            return;
        }
        ApplyText(code);
        RequestHighlight(code);
    }

    private void ApplyText(string code)
    {
        _text = code;
        var start = 0;
        var index = 0;
        _lineStarts.Clear();
        while (true)
        {
            var newline = code.IndexOf('\n', start);
            var end = newline < 0 ? code.Length : newline;
            var text = code[start..end].TrimEnd('\r');
            _lineStarts.Add(start);
            if (index < _lines.Count)
            {
                if (_lines[index].Text != text)
                {
                    _lines[index].Text = text;
                    _lines[index].Layout = null;
                    _lines[index].Tokens = default;
                }
            }
            else
            {
                _lines.Add(new Line { Text = text });
            }
            index++;
            if (newline < 0)
            {
                break;
            }
            start = newline + 1;
        }
        if (_lines.Count > index)
        {
            _lines.RemoveRange(index, _lines.Count - index);
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    // 后台增量着色：同一时刻最多一个任务，期间到达的新代码合并为一次
    private void RequestHighlight(string code)
    {
        if (_context?.Services.Registry.Highlighter is not { } highlighter || string.IsNullOrEmpty(_language) || !highlighter.SupportsLanguage(_language))
        {
            return;
        }
        if (_session is null || _sessionLanguage != _language)
        {
            _session?.Dispose();
            _session = highlighter.CreateSession(_language, _context.Style.HighlightTheme);
            _sessionLanguage = _language;
        }
        lock (_highlightLock)
        {
            _highlightPending = code;
            if (_highlightRunning)
            {
                return;
            }
            _highlightRunning = true;
        }
        var session = _session;
        _ = Task.Run(() => RunHighlight(session));
    }

    private void RunHighlight(IHighlightSession session)
    {
        while (true)
        {
            string? code;
            lock (_highlightLock)
            {
                code = _highlightPending;
                _highlightPending = null;
                if (code is null)
                {
                    _highlightRunning = false;
                    return;
                }
            }
            IReadOnlyList<ImmutableArray<HighlightToken>> tokens;
            try
            {
                tokens = session.Update(code);
            }
            catch (Exception)
            {
                continue;
            }
            Dispatcher.UIThread.Post(() => ApplyTokens(session, code, tokens));
        }
    }

    private void ApplyTokens(IHighlightSession session, string code, IReadOnlyList<ImmutableArray<HighlightToken>> tokens)
    {
        if (!ReferenceEquals(session, _session))
        {
            return;
        }
        // 着色期间代码可能又增长了：只应用文本仍然一致的行
        var codeLines = code.Split('\n');
        var changed = false;
        for (var i = 0; i < tokens.Count && i < _lines.Count && i < codeLines.Length; i++)
        {
            var line = _lines[i];
            if (line.Text != codeLines[i].TrimEnd('\r') || !line.Tokens.IsDefault && line.Tokens.SequenceEqual(tokens[i]))
            {
                continue;
            }
            line.Tokens = tokens[i];
            line.Layout = null;
            changed = true;
        }
        if (changed)
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private double LineHeight => _context?.Style.CodeLineHeight ?? 20;

    private TextLayout LayoutFor(Line line, double maxWidth)
    {
        if (line.Layout is not null)
        {
            return line.Layout;
        }
        var style = _context!.Style;
        var typeface = new Typeface(style.MonospaceFontFamily);
        List<ValueSpan<TextRunProperties>>? overrides = null;
        if (!line.Tokens.IsDefaultOrEmpty)
        {
            overrides = new List<ValueSpan<TextRunProperties>>(line.Tokens.Length);
            foreach (var token in line.Tokens)
            {
                if (token.Start >= line.Text.Length)
                {
                    break;
                }
                var length = Math.Min(token.Length, line.Text.Length - token.Start);
                if (token.Foreground == 0 && token.Style == HighlightFontStyle.None)
                {
                    continue;
                }
                var brush = token.Foreground == 0 ? style.Foreground : BrushCache.Get(token.Foreground);
                var face = new Typeface(style.MonospaceFontFamily,
                    token.Style.HasFlag(HighlightFontStyle.Italic) ? FontStyle.Italic : FontStyle.Normal,
                    token.Style.HasFlag(HighlightFontStyle.Bold) ? FontWeight.Bold : FontWeight.Normal);
                TextDecorationCollection? decorations = token.Style.HasFlag(HighlightFontStyle.Underline) ? TextDecorations.Underline : null;
                overrides.Add(new ValueSpan<TextRunProperties>(token.Start, length, new GenericTextRunProperties(face, style.CodeFontSize, decorations, brush)));
            }
        }
        line.Layout = new TextLayout(line.Text.Length == 0 ? " " : line.Text, typeface, style.CodeFontSize, style.Foreground,
            textWrapping: _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, maxWidth: _wrap ? maxWidth : double.PositiveInfinity,
            lineHeight: LineHeight, textStyleOverrides: overrides);
        line.Width = line.Layout.WidthIncludingTrailingWhitespace;
        return line.Layout;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_context is null)
        {
            return default;
        }
        var maxWidth = double.IsFinite(availableSize.Width) ? Math.Max(availableSize.Width, 1) : double.PositiveInfinity;
        if (_wrap && !_layoutWidth.Equals(maxWidth))
        {
            foreach (var line in _lines)
            {
                line.Layout = null;
            }
        }
        _layoutWidth = maxWidth;
        double height = 0, width = 0;
        foreach (var line in _lines)
        {
            var layout = LayoutFor(line, maxWidth);
            height += _wrap ? layout.Height : LineHeight;
            width = Math.Max(width, line.Width);
        }
        _contentWidth = width;
        ClampOffset(double.IsFinite(availableSize.Width) ? availableSize.Width : width);
        var scrollbar = !_wrap && double.IsFinite(availableSize.Width) && width > availableSize.Width ? 8 : 0;
        return new Size(double.IsFinite(availableSize.Width) ? Math.Min(width, availableSize.Width) : width, height + scrollbar);
    }

    private void ClampOffset(double viewport) => _offsetX = Math.Clamp(_offsetX, 0, Math.Max(0, _contentWidth - viewport));

    public override void Render(DrawingContext context)
    {
        if (_context is null)
        {
            return;
        }
        var style = _context.Style;
        var selected = _context.Selection.GetSegmentRange(Key, _text.Length);
        using (context.PushTransform(Matrix.CreateTranslation(-_offsetX, 0)))
        {
            double y = 0;
            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var layout = LayoutFor(line, _layoutWidth);
                var height = _wrap ? layout.Height : LineHeight;
                if (selected is { } range)
                {
                    var lineStart = _lineStarts[i];
                    var lineEnd = lineStart + line.Text.Length;
                    var s = Math.Max(range.Start, lineStart);
                    var e = Math.Min(range.End, lineEnd + 1);
                    if (e > s)
                    {
                        var hasNewline = range.End > lineEnd && i < _lines.Count - 1;
                        var textEnd = Math.Min(e, lineEnd);
                        foreach (var rect in layout.HitTestTextRange(s - lineStart, Math.Max(0, textEnd - s)))
                        {
                            context.FillRectangle(style.SelectionBackground, rect.Translate(new Vector(0, y)));
                        }
                        if (hasNewline)
                        {
                            var endRect = layout.HitTestTextPosition(line.Text.Length);
                            context.FillRectangle(style.SelectionBackground, new Rect(endRect.X, y + endRect.Y, style.CodeFontSize * 0.5, endRect.Height));
                        }
                    }
                }
                layout.Draw(context, new Point(0, y));
                y += height;
            }
        }

        // 横向滚动条（悬停时显示）
        if (!_wrap && _contentWidth > Bounds.Width + 0.5 && _hover)
        {
            var ratio = Bounds.Width / _contentWidth;
            var thumbWidth = Math.Max(24, Bounds.Width * ratio);
            var x = (_offsetX / (_contentWidth - Bounds.Width)) * (Bounds.Width - thumbWidth);
            context.FillRectangle(style.HoverBackground, new Rect(x, Bounds.Height - 6, thumbWidth, 4), 2);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (_wrap || _contentWidth <= Bounds.Width)
        {
            base.OnPointerWheelChanged(e);
            return;
        }
        // 横向滚动：触控板横向手势，或按住 Shift 的纵向滚轮
        var dx = e.Delta.X != 0 ? -e.Delta.X : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -e.Delta.Y : 0;
        if (dx == 0)
        {
            base.OnPointerWheelChanged(e);
            return;
        }
        _offsetX += dx * 40;
        ClampOffset(Bounds.Width);
        InvalidateVisual();
        e.Handled = true;
    }

    public int HitTest(Point point)
    {
        var p = new Point(point.X + _offsetX, point.Y);
        double y = 0;
        for (var i = 0; i < _lines.Count; i++)
        {
            var layout = LayoutFor(_lines[i], _layoutWidth);
            var height = _wrap ? layout.Height : LineHeight;
            if (p.Y < y + height || i == _lines.Count - 1)
            {
                var hit = layout.HitTestPoint(new Point(p.X, Math.Max(0, p.Y - y)));
                return _lineStarts[i] + Math.Clamp(hit.TextPosition, 0, _lines[i].Text.Length);
            }
            y += height;
        }
        return 0;
    }

    public Rect GetCharacterBounds(int offset)
    {
        double y = 0;
        for (var i = 0; i < _lines.Count; i++)
        {
            var layout = LayoutFor(_lines[i], _layoutWidth);
            var height = _wrap ? layout.Height : LineHeight;
            var lineEnd = _lineStarts[i] + _lines[i].Text.Length;
            if (offset <= lineEnd || i == _lines.Count - 1)
            {
                var rect = layout.HitTestTextPosition(Math.Clamp(offset - _lineStarts[i], 0, _lines[i].Text.Length));
                return rect.Translate(new Vector(-_offsetX, y));
            }
            y += height;
        }
        return default;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_context is not null)
        {
            _context.Selection.Changed += InvalidateVisual;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_context is not null)
        {
            _context.Selection.Changed -= InvalidateVisual;
        }
    }
}

/// <summary>按 ARGB 复用画刷。</summary>
internal static class BrushCache
{
    private static readonly Dictionary<uint, IBrush> Brushes = [];

    public static IBrush Get(uint argb)
    {
        lock (Brushes)
        {
            if (!Brushes.TryGetValue(argb, out var brush))
            {
                brush = new ImmutableSolidColorBrush(Color.FromUInt32(argb));
                Brushes[argb] = brush;
            }
            return brush;
        }
    }
}
