using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Selection;
using StreamMark.Core.Model;

namespace StreamMark.Avalonia.Rendering;

/// <summary>
/// 图表代码块（如 Mermaid）：代码块闭合后在后台渲染；渲染完成前和失败时显示源码。
/// 头部可在"图表 / 源码"之间切换。渲染结果按 (语言, 代码, 明暗) 缓存，视图回收后再出现不会重复渲染。
/// </summary>
public sealed class DiagramBlockView : BlockView
{
    private const double HeaderHeight = 34;
    private const double Padding = 14;

    private readonly IconButton _toggle = new() { Icon = MarkdownIcons.Code };
    private readonly IconButton _copy = new() { Icon = MarkdownIcons.Copy };
    private readonly CodeTextView _code = new();
    private DiagramResult? _diagram;
    private string? _error;
    private bool _showCode;
    private string _renderKey = "";
    private CancellationTokenSource? _cts;
    private TextLayout? _title;
    private TextLayout? _status;
    private string? _statusText;
    private bool _statusIsError;

    public DiagramBlockView()
    {
        AddChild(_code);
        AddChild(_toggle);
        AddChild(_copy);
        _toggle.Click += (_, _) =>
        {
            _showCode = !_showCode;
            Context?.State.Set(TopKey, "diagram.code", _showCode);
            _toggle.Icon = _showCode ? MarkdownIcons.Diagram : MarkdownIcons.Code;
            UpdateVisibility();
        };
        _copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_code.Text);
            }
        };
    }

    protected override void OnUpdate(RenderContext context, MarkdownBlock? previous, MarkdownBlock block, int topKey, ref int segment, bool contextChanged)
    {
        var code = (CodeBlock)block;
        var style = context.Style;
        _code.Set(context, KeyFor(context, topKey, ref segment), code.Code, code.Language, code.IsOpen);
        _showCode = context.State.Get(topKey, "diagram.code", false);
        _toggle.Icon = _showCode ? MarkdownIcons.Diagram : MarkdownIcons.Code;
        foreach (var button in new[] { _toggle, _copy })
        {
            button.Foreground = style.SecondaryForeground;
            button.HoverBackground = style.HoverBackground;
        }
        _title = new TextLayout(code.Language ?? "diagram", new Typeface(style.MonospaceFontFamily), style.CodeFontSize * 0.92, style.SecondaryForeground);

        var key = $"{code.Language}\n{style.IsDark}\n{code.Code}";
        if (key != _renderKey)
        {
            _renderKey = key;
            StartRender(context, code);
        }
        UpdateVisibility();
    }

    private void StartRender(RenderContext context, CodeBlock code)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        if (DiagramCache.TryGet(_renderKey, out var cached))
        {
            _diagram = cached;
            _error = null;
            return;
        }
        var renderer = context.Services.FindDiagramRenderer(code.Language);
        if (renderer is null)
        {
            return;
        }
        _diagram = null;
        _error = null;
        _statusText = "Rendering…";
        _statusIsError = false;
        _status = null;
        var style = context.Style;
        var renderContext = new DiagramRenderContext(style.IsDark, ColorOf(style.Foreground), ColorOf(style.CodeBackground), ColorOf(style.LinkForeground),
            style.FontFamily.Name, style.FontSize);
        var key = _renderKey;
        var language = code.Language ?? "";
        var source = code.Code;
        _ = Task.Run(async () =>
        {
            DiagramResult? result = null;
            string? error = null;
            try
            {
                result = await renderer.RenderAsync(language, source, renderContext, token).ConfigureAwait(false);
                if (result is null)
                {
                    error = "Diagram could not be rendered.";
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result is not null)
                {
                    DiagramCache.Add(key, result);
                }
                if (token.IsCancellationRequested || key != _renderKey)
                {
                    return;
                }
                _diagram = result;
                _error = error;
                _statusText = error;
                _statusIsError = error is not null;
                _status = null;
                UpdateVisibility();
            });
        }, token);
    }

    private static Color ColorOf(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    private bool DiagramVisible => _diagram is not null && !_showCode;

    /// <summary>当前显示的是渲染好的图表（而不是源码）。</summary>
    public bool IsShowingDiagram => DiagramVisible;

    private void UpdateVisibility()
    {
        _code.IsVisible = !DiagramVisible;
        _toggle.IsVisible = _diagram is not null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private double StatusHeight => _status is null || DiagramVisible ? 0 : _status.Height + 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        _toggle.Measure(Size.Infinity);
        _copy.Measure(Size.Infinity);
        var inner = Math.Max(0, availableSize.Width - Padding * 2);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 600;
        if (DiagramVisible)
        {
            var size = FitDiagram(inner);
            return new Size(width, HeaderHeight + size.Height + Padding);
        }
        _status = _statusText is null || Context is null
            ? null
            : new TextLayout(_statusText, new Typeface(Context.Style.FontFamily), Context.Style.FontSize * 0.85,
                _statusIsError ? Context.Style.AlertBrush("CAUTION") : Context.Style.SecondaryForeground,
                textWrapping: TextWrapping.Wrap, maxWidth: double.IsFinite(inner) ? Math.Max(1, inner) : double.PositiveInfinity);
        _code.Measure(new Size(inner, double.PositiveInfinity));
        return new Size(width, HeaderHeight + StatusHeight + _code.DesiredSize.Height + Padding);
    }

    private Size FitDiagram(double width)
    {
        var natural = _diagram!.Size;
        if (natural.Width <= 0 || natural.Height <= 0)
        {
            return default;
        }
        var scale = Math.Min(1, width / natural.Width);
        // 过高的图表限制高度
        scale = Math.Min(scale, 900 / natural.Height);
        return new Size(natural.Width * scale, natural.Height * scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = finalSize.Width - 6;
        foreach (var button in new[] { _copy, _toggle })
        {
            if (!button.IsVisible)
            {
                continue;
            }
            x -= button.DesiredSize.Width;
            button.Arrange(new Rect(x, (HeaderHeight - button.DesiredSize.Height) / 2, button.DesiredSize.Width, button.DesiredSize.Height));
            x -= 2;
        }
        _code.Arrange(new Rect(Padding, HeaderHeight + StatusHeight, Math.Max(0, finalSize.Width - Padding * 2), _code.DesiredSize.Height));
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
        _title?.Draw(context, new Point(Padding, (HeaderHeight - _title.Height) / 2));
        if (DiagramVisible)
        {
            var size = FitDiagram(Math.Max(0, Bounds.Width - Padding * 2));
            var rect = new Rect((Bounds.Width - size.Width) / 2, HeaderHeight, size.Width, size.Height);
            _diagram!.Draw(context, rect);
        }
        else
        {
            _status?.Draw(context, new Point(Padding, HeaderHeight));
        }
    }

    public override IEnumerable<ISelectableSegment> Segments() => DiagramVisible ? [] : [_code];

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _cts?.Cancel();
        // 取消后重新挂载时需要重新渲染（缓存命中则立即显示）
        if (_diagram is null)
        {
            _renderKey = "";
        }
    }

    private static class DiagramCache
    {
        private const int Capacity = 32;
        private static readonly Dictionary<string, DiagramResult> Items = [];
        private static readonly LinkedList<string> Order = new();

        public static bool TryGet(string key, out DiagramResult? result)
        {
            if (Items.TryGetValue(key, out var found))
            {
                result = found;
                return true;
            }
            result = null;
            return false;
        }

        public static void Add(string key, DiagramResult result)
        {
            if (Items.ContainsKey(key))
            {
                return;
            }
            Items[key] = result;
            Order.AddLast(key);
            while (Order.Count > Capacity)
            {
                Items.Remove(Order.First!.Value);
                Order.RemoveFirst();
            }
        }
    }
}
