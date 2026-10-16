using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Rendering;
using StreamMark.Avalonia.Selection;
using StreamMark.Core.Streaming;

namespace StreamMark.Avalonia;

/// <summary>
/// 渲染单个 Markdown 文档（非虚拟化，高度随内容增长）。设置 <see cref="Markdown"/> 显示静态文本，
/// 或设置 <see cref="Stream"/> / <see cref="Document"/> 显示流式文档。长会话请使用 TranscriptView。
/// </summary>
public class MarkdownView : Control, ISelectionHost
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<StreamingDocument?> DocumentProperty =
        AvaloniaProperty.Register<MarkdownView, StreamingDocument?>(nameof(Document));

    public static readonly StyledProperty<MarkdownStream?> StreamProperty =
        AvaloniaProperty.Register<MarkdownView, MarkdownStream?>(nameof(Stream));

    /// <summary>链接点击（来自内部任一文本片段）。</summary>
    public static readonly RoutedEvent<LinkClickedEventArgs> LinkClickedEvent = TextSegmentView.LinkClickedEvent;

    private readonly DocumentPresenter _presenter;
    private readonly SelectionController _selectionController;
    private StreamingDocument? _subscribed;
    private StreamingDocument? _ownDocument;
    private RenderContext? _context;
    private bool _frameRequested;
    private readonly Plugins.BlockStateStore _state = new();

    static MarkdownView()
    {
        FocusableProperty.OverrideDefaultValue<MarkdownView>(true);
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnSourceChanged());
        DocumentProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnSourceChanged());
        StreamProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnSourceChanged());
        MarkdownServices.ServicesProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.InvalidateContext());
        MarkdownServices.ImageBaseUriProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.InvalidateContext());
    }

    public MarkdownView()
    {
        _presenter = new DocumentPresenter(
            (index, view) =>
            {
                LogicalChildren.Insert(index, view);
                VisualChildren.Insert(index, view);
                InvalidateMeasure();
            },
            (_, view) =>
            {
                LogicalChildren.Remove(view);
                VisualChildren.Remove(view);
                InvalidateMeasure();
            });
        _selectionController = new SelectionController(this);
        MarkdownContextMenu.Attach(this, _selectionController);
        AddHandler(TextSegmentView.LinkClickedEvent, OnAnchorLinkClicked);
        ResourcesChanged += (_, _) => InvalidateContext();
        ActualThemeVariantChanged += (_, _) => InvalidateContext();
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public StreamingDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public MarkdownStream? Stream
    {
        get => GetValue(StreamProperty);
        set => SetValue(StreamProperty, value);
    }

    public SelectionModel Selection { get; } = new();

    public SelectionController SelectionController => _selectionController;

    /// <summary>当前显示的快照。</summary>
    public DocumentSnapshot? Snapshot => _presenter.Applied;

    public event EventHandler<LinkClickedEventArgs>? LinkClicked
    {
        add => AddHandler(LinkClickedEvent, value);
        remove => RemoveHandler(LinkClickedEvent, value);
    }

    private StreamingDocument? EffectiveDocument => Stream?.Document ?? Document ?? _ownDocument;

    private void OnSourceChanged()
    {
        if (Stream is null && Document is null)
        {
            _ownDocument ??= new StreamingDocument(MarkdownServices.GetServices(this).Profile);
            _ownDocument.Load(Markdown ?? "");
        }
        else
        {
            _ownDocument = null;
        }
        Subscribe();
        _context = null;
        _presenter.Clear();
        _state.Clear();
        Selection.Clear();
        RequestFrame();
    }

    private void Subscribe()
    {
        var document = VisualRoot is null ? null : EffectiveDocument;
        if (ReferenceEquals(document, _subscribed))
        {
            return;
        }
        if (_subscribed is not null)
        {
            _subscribed.SnapshotReady -= OnSnapshotReady;
        }
        _subscribed = document;
        if (document is not null)
        {
            document.SnapshotReady += OnSnapshotReady;
        }
    }

    private void OnSnapshotReady(DocumentSnapshot snapshot) => Dispatcher.UIThread.Post(RequestFrame, DispatcherPriority.Render);

    private void RequestFrame()
    {
        if (_frameRequested)
        {
            return;
        }
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            ApplyLatest();
        });
    }

    /// <summary>立即应用最新快照（测试或需要同步刷新时使用）。</summary>
    public void ApplyLatest()
    {
        if (EffectiveDocument is not { } document)
        {
            return;
        }
        _context ??= CreateContext();
        _presenter.Apply(document.Latest, _context);
    }

    private RenderContext CreateContext() =>
        new(MarkdownStyle.Resolve(this), MarkdownServices.GetServices(this), Selection, 0, Stream?.State, _state, MarkdownServices.GetImageBaseUri(this));

    private void InvalidateContext()
    {
        _context = null;
        RequestFrame();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Stream is null && Document is null && _ownDocument is null)
        {
            OnSourceChanged();
        }
        Subscribe();
        _context = null;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe();
    }

    private double Spacing => _context?.Style.BlockSpacing ?? 12;

    void ISelectionHost.ScrollToPoint(TextPoint point) => ScrollToPoint(point);

    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Automation.MarkdownDocumentAutomationPeer(this);

    /// <summary>文档内查找。</summary>
    public SearchModel Search => _search ??= new SearchModel(this);

    private SearchModel? _search;

    /// <summary>文档的目录（标题，按阅读顺序）。</summary>
    public List<OutlineItem> GetOutline() => _presenter.Applied is { } snapshot ? DocumentNavigation.Outline(snapshot, 0) : [];

    /// <summary>让选区坐标所在位置进入可视区域（由外层滚动容器滚动，例如 <see cref="ScrollViewer"/>）。找不到目标时返回 false。</summary>
    public bool ScrollToPoint(TextPoint point)
    {
        var key = new SegmentKey(point.Entry, point.BlockKey, point.Segment);
        foreach (var view in _presenter.Views)
        {
            if (view.TopKey != point.BlockKey)
            {
                continue;
            }
            var segment = view.Segments().FirstOrDefault(s => s.Key == key);
            if (segment is null)
            {
                view.BringIntoView(new Rect(view.Bounds.Size).WithHeight(Math.Min(view.Bounds.Height, 40)));
                return true;
            }
            var rect = segment.GetCharacterBounds(point.Offset);
            // 目标上方多留一些空间，避免紧贴顶部
            var target = new Rect(rect.X, rect.Y - 80, Math.Max(1, rect.Width), rect.Height + 160);
            segment.View.BringIntoView(target);
            return true;
        }
        return false;
    }

    /// <summary>滚动到某个顶层块的开头。</summary>
    public bool ScrollToBlock(int blockKey) => ScrollToPoint(new TextPoint(0, blockKey, 0, 0));

    // 文内锚点（#标题、#fn:脚注）在文档内跳转，找不到时交给宿主
    private void OnAnchorLinkClicked(object? sender, LinkClickedEventArgs e)
    {
        if (e.Url.StartsWith('#') && _presenter.Applied is { } snapshot && DocumentNavigation.ResolveAnchor(snapshot, 0, e.Url) is { } target)
        {
            ScrollToPoint(target);
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0, width = 0;
        var views = _presenter.Views;
        for (var i = 0; i < views.Count; i++)
        {
            views[i].Measure(new Size(availableSize.Width, double.PositiveInfinity));
            height += views[i].DesiredSize.Height + (i > 0 ? Spacing : 0);
            width = Math.Max(width, views[i].DesiredSize.Width);
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        var views = _presenter.Views;
        for (var i = 0; i < views.Count; i++)
        {
            if (i > 0)
            {
                y += Spacing;
            }
            views[i].Arrange(new Rect(0, y, finalSize.Width, views[i].DesiredSize.Height));
            y += views[i].DesiredSize.Height;
        }
        return finalSize;
    }

    public override void Render(global::Avalonia.Media.DrawingContext context)
    {
        // 透明背景：块与块之间的空白也能开始拖拽选择
        context.FillRectangle(global::Avalonia.Media.Brushes.Transparent, new Rect(Bounds.Size));
    }

    Control ISelectionHost.HostControl => this;

    IEnumerable<ISelectableSegment> ISelectionHost.RealizedSegments() => _presenter.Views.SelectMany(v => v.Segments());

    int ISelectionHost.EntryCount => 1;

    Plugins.PluginRegistry? ISelectionHost.Plugins => MarkdownServices.GetServices(this).Registry;

    DocumentSnapshot? ISelectionHost.GetSnapshot(int entry) => entry == 0 ? _presenter.Applied : null;

    string ISelectionHost.GetSource(int entry, int start, int length) => EffectiveDocument?.GetText(start, length) ?? "";
}
