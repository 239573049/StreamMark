using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Rendering;
using StreamMark.Avalonia.Selection;
using StreamMark.Core.Model;
using StreamMark.Core.Streaming;

namespace StreamMark.Avalonia.Transcript;

/// <summary>
/// 会话面板：把所有消息摊平成行（每个 Markdown 顶层块一行，每个宿主内容一行），只实例化视口附近的行。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>未测量的行使用按内容估算的高度；实例化后用真实高度替换，并调整滚动偏移，使视口内首行位置不跳动（滚动锚定）。</item>
/// <item>贴底：滚动到底部时自动跟随新内容；用户向上滚动即解除，滚回底部恢复。</item>
/// <item>每帧最多应用一次各文档的最新快照（<see cref="TopLevel.RequestAnimationFrame"/>）。</item>
/// <item>选区与视图无关（见 <see cref="SelectionModel"/>），被回收的行仍可复制。</item>
/// </list>
/// </remarks>
public sealed class TranscriptPanel : Panel, ILogicalScrollable, ISelectionHost
{
    private sealed class EntryState(TranscriptEntry entry)
    {
        public TranscriptEntry Entry { get; } = entry;
        public DocumentSnapshot? Applied;
        public RenderContext? Context;
        public readonly List<Row> Rows = [];
        // 每个顶层块占用的行数（超长代码块拆成多行），与快照中的块一一对应
        public readonly List<int> Slots = [];
        public readonly Plugins.BlockStateStore State = new();
        public Control? ContentView;
    }

    private sealed class Row(EntryState owner)
    {
        public EntryState Owner { get; } = owner;
        public MarkdownBlock? Block;
        public Control? View;
        public int Chunk;
        public int ChunkCount = 1;
        public double Height;
        public bool Measured;
        public double Top;
    }

    private const double Overscan = 600;

    private readonly List<EntryState> _entries = [];
    private readonly List<Row> _rows = [];
    private readonly HashSet<Row> _realized = [];
    private readonly SelectionController _selection;
    private INotifyCollectionChanged? _subscribedCollection;
    private IReadOnlyList<TranscriptEntry> _source = [];
    private bool _rowsDirty = true;
    private bool _topsDirty = true;
    private bool _frameRequested;
    private MarkdownStyle? _style;
    private MarkdownServices? _services;
    private double _width = double.NaN;
    private Size _extent;
    private Size _viewport;
    private Vector _offset;
    private bool _pinned = true;
    private bool _settingOffset;
    private TextSegmentView? _tail;

    // 行结构变化前记下的锚点（视口内第一行及其相对视口的位置），下一次测量时恢复，
    // 使在视口上方插入条目、按需解析的条目展开为真实行时，视口内容不跳动
    private PendingAnchor? _pendingAnchor;

    private sealed record PendingAnchor(Row Row, double Delta, double EntryDelta);

    public TranscriptPanel()
    {
        ClipToBounds = true;
        Focusable = true;
        // 透明背景：空白处也能开始拖拽选择
        Background = Brushes.Transparent;
        _selection = new SelectionController(this);
        AddHandler(CodeBlockView.ExpandedChangedEvent, OnCodeExpandedChanged);
        ResourcesChanged += (_, _) => InvalidateStyle();
        ActualThemeVariantChanged += (_, _) => InvalidateStyle();
    }

    static TranscriptPanel()
    {
        MarkdownServices.ServicesProperty.Changed.AddClassHandler<TranscriptPanel>((panel, _) => panel.InvalidateStyle());
    }

    /// <summary>内容列的最大宽度：内容在面板内水平居中，滚动条仍在面板边缘。</summary>
    public double MaxContentWidth
    {
        get => _maxContentWidth;
        set
        {
            _maxContentWidth = value;
            InvalidateMeasure();
        }
    }

    private double _maxContentWidth = double.PositiveInfinity;

    /// <summary>消息之间的间距。</summary>
    public double EntrySpacing { get; set; } = 24;

    /// <summary>贴底跟随新内容。</summary>
    public bool IsPinnedToBottom => _pinned;

    public SelectionModel Selection { get; } = new();

    public SelectionController SelectionController => _selection;

    /// <summary>当前实例化的行数（用于诊断与测试）。</summary>
    public int RealizedCount => _realized.Count;

    public int RowCount
    {
        get
        {
            EnsureRows();
            return _rows.Count;
        }
    }

    public void SetEntries(IReadOnlyList<TranscriptEntry> entries)
    {
        if (_subscribedCollection is not null)
        {
            _subscribedCollection.CollectionChanged -= OnCollectionChanged;
        }
        _source = entries;
        _subscribedCollection = entries as INotifyCollectionChanged;
        if (_subscribedCollection is not null)
        {
            _subscribedCollection.CollectionChanged += OnCollectionChanged;
        }
        SyncEntries();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncEntries();

    // 按引用复用已有条目的状态（行、视图、已应用快照），只为新条目创建状态
    private void SyncEntries()
    {
        CaptureAnchor();
        var existing = _entries.ToDictionary(s => s.Entry);
        var next = new List<EntryState>(_source.Count);
        foreach (var entry in _source)
        {
            if (existing.Remove(entry, out var state))
            {
                next.Add(state);
            }
            else
            {
                state = new EntryState(entry);
                if (entry is MarkdownEntry markdown)
                {
                    markdown.Document.SnapshotReady += OnSnapshotReady;
                }
                next.Add(state);
            }
        }
        foreach (var removed in existing.Values)
        {
            if (removed.Entry is MarkdownEntry markdown)
            {
                markdown.Document.SnapshotReady -= OnSnapshotReady;
            }
            foreach (var row in removed.Rows)
            {
                Unrealize(row);
            }
            if (removed.ContentView is not null)
            {
                Children.Remove(removed.ContentView);
            }
        }
        _entries.Clear();
        _entries.AddRange(next);
        // 序号变化后上下文（选区坐标的第一维）需要重建
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Context is { } context && context.Entry != i)
            {
                _entries[i].Context = null;
            }
        }
        _rowsDirty = true;
        RequestFrame();
        InvalidateMeasure();
    }

    private void OnSnapshotReady(DocumentSnapshot snapshot) => Dispatcher.UIThread.Post(RequestFrame, DispatcherPriority.Render);

    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } top)
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

    private void InvalidateStyle()
    {
        _style = null;
        foreach (var entry in _entries)
        {
            entry.Context = null;
        }
        RequestFrame();
        InvalidateMeasure();
    }

    private RenderContext ContextFor(int index, EntryState state)
    {
        _style ??= MarkdownStyle.Resolve(this);
        _services = MarkdownServices.GetServices(this);
        if (state.Context is null || !ReferenceEquals(state.Context.Style, _style) || !ReferenceEquals(state.Context.Services, _services)
            || state.Context.ImageBaseUri != MarkdownServices.GetImageBaseUri(this))
        {
            var streaming = (state.Entry as MarkdownEntry)?.State;
            state.Context = new RenderContext(_style, _services, Selection, index, streaming, state.State, MarkdownServices.GetImageBaseUri(this));
        }
        return state.Context;
    }

    /// <summary>把所有文档的最新快照应用到行上（每帧最多一次；测试中可直接调用）。</summary>
    public void ApplyLatest()
    {
        ApplyAll();
        InvalidateMeasure();
    }

    private void ApplyAll()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var state = _entries[i];
            if (state.Entry is not MarkdownEntry markdown || !markdown.IsParsed)
            {
                continue;
            }
            var context = ContextFor(i, state);
            var latest = markdown.Document.Latest;
            if (state.Rows.Count > 0 && state.Rows[0].Block is null)
            {
                // 按需解析的条目刚解析完：去掉估算高度的占位行，按快照重建
                CaptureAnchor();
                state.Rows.Clear();
                state.Slots.Clear();
                state.Applied = null;
                _rowsDirty = true;
            }
            var contextChanged = state.Rows.Any(r => r.View is BlockView view && !ReferenceEquals(view.Block, r.Block)) || state.Applied is null;
            if (ReferenceEquals(latest, state.Applied) && !contextChanged)
            {
                RefreshRealizedContexts(state, context);
                continue;
            }
            CaptureAnchor();
            foreach (var op in SnapshotDiff.Compute(state.Applied, latest))
            {
                var rowStart = 0;
                for (var b = 0; b < op.Index; b++)
                {
                    rowStart += state.Slots[b];
                }
                switch (op.Kind)
                {
                    case DiffKind.Insert:
                        {
                            var count = ChunkCount(op.Block!, context);
                            for (var c = 0; c < count; c++)
                            {
                                var row = new Row(state) { Block = op.Block, Chunk = c, ChunkCount = count };
                                row.Height = Estimate(row, context);
                                state.Rows.Insert(rowStart + c, row);
                            }
                            state.Slots.Insert(op.Index, count);
                            _rowsDirty = true;
                            break;
                        }
                    case DiffKind.Update:
                        UpdateBlockRows(state, op.Index, rowStart, op.Block!, context);
                        break;
                    case DiffKind.Remove:
                        {
                            var count = state.Slots[op.Index];
                            for (var c = 0; c < count; c++)
                            {
                                Unrealize(state.Rows[rowStart]);
                                state.Rows.RemoveAt(rowStart);
                            }
                            state.Slots.RemoveAt(op.Index);
                            _rowsDirty = true;
                            break;
                        }
                }
            }
            state.Applied = latest;
            RefreshRealizedContexts(state, context);
        }
    }

    private void CaptureAnchor()
    {
        if (_pendingAnchor is not null || _pinned || _rows.Count == 0)
        {
            return;
        }
        EnsureTops();
        var index = FirstRowAt(_offset.Y);
        if (index >= _rows.Count)
        {
            return;
        }
        var row = _rows[index];
        var entryTop = row.Owner.Rows.Count > 0 ? row.Owner.Rows[0].Top : row.Top;
        _pendingAnchor = new PendingAnchor(row, row.Top - _offset.Y, entryTop - _offset.Y);
    }

    private void RestoreAnchor()
    {
        if (_pendingAnchor is not { } anchor)
        {
            return;
        }
        _pendingAnchor = null;
        if (_pinned || !_entries.Contains(anchor.Row.Owner))
        {
            return;
        }
        if (anchor.Row.Owner.Rows.Contains(anchor.Row))
        {
            _offset = new Vector(0, Math.Max(0, anchor.Row.Top - anchor.Delta));
        }
        else if (anchor.Row.Owner.Rows.Count > 0)
        {
            // 锚点行已被替换（占位行展开）：保持所在条目顶部的位置
            _offset = new Vector(0, Math.Max(0, anchor.Row.Owner.Rows[0].Top - anchor.EntryDelta));
        }
    }

    // 视口附近还没解析的按需条目：同步解析并展开为真实行（可能连带让更多条目进入视口，最多几轮）
    private void ParseVisibleDeferred(double height)
    {
        for (var round = 0; round < 8; round++)
        {
            var offset = _pinned ? Math.Max(0, _extent.Height - height) : _offset.Y;
            var top = Math.Max(0, offset - Overscan);
            var bottom = offset + height + Overscan;
            var parsed = false;
            for (var i = FirstRowAt(top); i < _rows.Count && _rows[i].Top <= bottom; i++)
            {
                if (_rows[i].Block is null && _rows[i].Owner.Entry is MarkdownEntry { IsParsed: false } markdown)
                {
                    CaptureAnchor();
                    markdown.EnsureParsed();
                    parsed = true;
                }
            }
            if (!parsed)
            {
                return;
            }
            ApplyAll();
            EnsureRows();
            EnsureTops();
            RestoreAnchor();
        }
    }

    // 按需条目解析前的高度估算：按每行字符数与换行数
    private double EstimateText(string text)
    {
        var style = _style ?? MarkdownStyle.Resolve(this);
        var width = double.IsFinite(_width) && _width > 0 ? _width : 720;
        var charsPerLine = Math.Max(10, width / (style.FontSize * 0.62));
        double height = 0;
        var blankRun = false;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (line.IsWhiteSpace())
            {
                if (!blankRun)
                {
                    height += style.BlockSpacing;
                }
                blankRun = true;
                continue;
            }
            blankRun = false;
            height += Math.Max(1, Math.Ceiling(line.Length / charsPerLine)) * style.LineHeight;
        }
        return Math.Max(style.LineHeight, height);
    }

    // 一个顶层块的行原地更新；分段数变化时增删末尾的行
    private void UpdateBlockRows(EntryState state, int index, int rowStart, MarkdownBlock block, RenderContext context)
    {
        var oldCount = state.Slots[index];
        var count = ChunkCount(block, context);
        for (var c = count; c < oldCount; c++)
        {
            Unrealize(state.Rows[rowStart + count]);
            state.Rows.RemoveAt(rowStart + count);
        }
        for (var c = oldCount; c < count; c++)
        {
            state.Rows.Insert(rowStart + c, new Row(state));
        }
        for (var c = 0; c < count; c++)
        {
            var updated = state.Rows[rowStart + c];
            updated.Block = block;
            updated.Chunk = c;
            updated.ChunkCount = count;
            updated.Measured = false;
            if (updated.View is BlockView view)
            {
                UpdateView(updated, view, context);
            }
            else
            {
                updated.Height = Estimate(updated, context);
            }
        }
        state.Slots[index] = count;
        if (count != oldCount)
        {
            _rowsDirty = true;
        }
        _topsDirty = true;
    }

    // 代码块展开 / 折叠：按新的分段数重建该块的行
    private void OnCodeExpandedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is not CodeBlockView view)
        {
            return;
        }
        for (var i = 0; i < _entries.Count; i++)
        {
            var state = _entries[i];
            var rowStart = 0;
            for (var b = 0; b < state.Slots.Count; b++)
            {
                var count = state.Slots[b];
                for (var c = 0; c < count; c++)
                {
                    if (ReferenceEquals(state.Rows[rowStart + c].View, view))
                    {
                        CaptureAnchor();
                        UpdateBlockRows(state, b, rowStart, state.Rows[rowStart].Block!, ContextFor(i, state));
                        InvalidateMeasure();
                        return;
                    }
                }
                rowStart += count;
            }
        }
    }

    // 样式变化后已实例化的视图需要按新上下文重新应用
    private void RefreshRealizedContexts(EntryState state, RenderContext context)
    {
        foreach (var row in state.Rows)
        {
            if (row.View is BlockView view && row.Block is not null && !ReferenceEquals(ViewContext(view), context))
            {
                UpdateView(row, view, context);
            }
        }
    }

    private static RenderContext? ViewContext(BlockView view) => view.CurrentContext;

    /// <summary>超过这个行数的代码块在会话中拆成多段，每段一行，只实例化视口附近的分段。</summary>
    public const int ChunkThresholdLines = 200;

    public const int LinesPerChunk = 128;

    private static int ChunkCount(MarkdownBlock block, RenderContext context)
    {
        // 折叠的代码块只显示前若干行：不分段
        if (block is not CodeBlock code || CodeBlockView.ShouldCollapse(code, context, block.Key, out _))
        {
            return 1;
        }
        var lines = 1;
        foreach (var c in code.Code)
        {
            if (c == '\n')
            {
                lines++;
            }
        }
        return lines > ChunkThresholdLines ? (lines + LinesPerChunk - 1) / LinesPerChunk : 1;
    }

    private static void ApplyChunk(Row row, BlockView view)
    {
        if (view is CodeBlockView code)
        {
            code.SetChunk(row.Chunk, row.ChunkCount, LinesPerChunk);
        }
    }

    private void UpdateView(Row row, BlockView view, RenderContext context)
    {
        var segment = 0;
        if (BlockViewFactory.CanReuse(view, row.Block!, context))
        {
            ApplyChunk(row, view);
            view.Update(context, row.Block!, row.Block!.Key, ref segment);
        }
        else
        {
            Children.Remove(view);
            var replacement = BlockViewFactory.Create(row.Block!, context);
            ApplyChunk(row, replacement);
            replacement.Update(context, row.Block!, row.Block!.Key, ref segment);
            row.View = replacement;
            Children.Add(replacement);
        }
        row.Measured = false;
        _topsDirty = true;
    }

    private double Estimate(Row row, RenderContext context)
    {
        if (row.ChunkCount > 1 && row.Block is CodeBlock)
        {
            var lines = row.Chunk < row.ChunkCount - 1 ? LinesPerChunk : Math.Max(1, ChunkCountLines(row) - row.Chunk * LinesPerChunk);
            return lines * context.Style.CodeLineHeight + (row.Chunk == 0 ? 34 : 0) + (row.Chunk == row.ChunkCount - 1 ? 14 : 0);
        }
        return Estimate(row.Block!, context);
    }

    private static int ChunkCountLines(Row row) => ((CodeBlock)row.Block!).Code.Count(c => c == '\n') + 1;

    // 未测量行的高度估算：按块类型与内容长度粗略估计
    private double Estimate(MarkdownBlock block, RenderContext context)
    {
        var style = context.Style;
        var width = double.IsFinite(_width) && _width > 0 ? _width : 720;
        var charsPerLine = Math.Max(10, width / (style.FontSize * 0.62));
        double Lines(int length) => Math.Max(1, Math.Ceiling(length / charsPerLine));
        return block switch
        {
            ParagraphBlock p => Lines(p.Content.Text.Length) * style.LineHeight,
            HeadingBlock h => style.HeadingFontSize(h.Level) * 1.6 + 10,
            CodeBlock c => (c.Code.Count(ch => ch == '\n') + 1) * style.CodeLineHeight + 48,
            MathBlock => style.FontSize * 3,
            TableBlock t => t.Rows.Length * (style.LineHeight + 12),
            ListBlock l => l.Items.Sum(i => i.Children.Sum(child => Estimate(child, context))) + l.Items.Length * 4,
            QuoteBlock q => q.Children.Sum(child => Estimate(child, context)) + q.Children.Length * style.BlockSpacing,
            HtmlBoxBlock b => b.Children.Sum(child => Estimate(child, context)) + b.Children.Length * style.BlockSpacing + b.Box.Margin.Top + b.Box.Margin.Bottom + b.Box.Padding.Top + b.Box.Padding.Bottom,
            AlertBlock a => a.Children.Sum(child => Estimate(child, context)) + style.LineHeight + 8,
            ThematicBreakBlock => 17,
            _ => style.LineHeight * 2,
        };
    }

    private void EnsureRows()
    {
        if (!_rowsDirty)
        {
            return;
        }
        _rows.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            var state = _entries[i];
            if (state.Entry is ContentEntry)
            {
                if (state.Rows.Count == 0)
                {
                    state.Rows.Add(new Row(state) { Height = 60 });
                }
            }
            else if (state.Entry is MarkdownEntry { IsParsed: false } deferred && state.Rows.Count == 0)
            {
                // 按需解析的条目：一个估算高度的占位行，进入视口附近时才解析
                state.Rows.Add(new Row(state) { Height = EstimateText(deferred.DeferredText ?? "") });
            }
            _rows.AddRange(state.Rows);
        }
        _rowsDirty = false;
        _topsDirty = true;
    }

    private void EnsureTops()
    {
        if (!_topsDirty)
        {
            return;
        }
        double y = 0;
        var spacing = _style?.BlockSpacing ?? 12;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (i > 0)
            {
                // 同一代码块的分段首尾相接；同一消息的块之间用块间距；消息之间用消息间距
                y += _rows[i].Chunk > 0 && ReferenceEquals(_rows[i].Block, _rows[i - 1].Block) ? 0
                    : ReferenceEquals(_rows[i].Owner, _rows[i - 1].Owner) ? spacing : EntrySpacing;
            }
            _rows[i].Top = y;
            y += _rows[i].Height;
        }
        _extent = new Size(double.IsFinite(_width) ? _width : 0, y);
        _topsDirty = false;
    }

    private int FirstRowAt(double y)
    {
        int lo = 0, hi = _rows.Count - 1, result = _rows.Count;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_rows[mid].Top + _rows[mid].Height >= y)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return result;
    }

    private Control Realize(Row row)
    {
        if (row.View is not null)
        {
            return row.View;
        }
        var index = _entries.IndexOf(row.Owner);
        Control view;
        if (row.Owner.Entry is ContentEntry content)
        {
            view = row.Owner.ContentView ??= BuildContent(content.Content);
        }
        else
        {
            var context = ContextFor(index, row.Owner);
            var blockView = BlockViewFactory.Create(row.Block!, context);
            ApplyChunk(row, blockView);
            var segment = 0;
            blockView.Update(context, row.Block!, row.Block!.Key, ref segment);
            view = blockView;
        }
        row.View = view;
        _realized.Add(row);
        Children.Add(view);
        return view;
    }

    private Control BuildContent(object content)
    {
        if (content is Control control)
        {
            return control;
        }
        var template = this.FindDataTemplate(content);
        var built = template?.Build(content) ?? new TextBlock { Text = content.ToString() };
        built.DataContext = content;
        return built;
    }

    private void Unrealize(Row row)
    {
        if (row.View is null)
        {
            return;
        }
        Children.Remove(row.View);
        // 宿主内容控件保留以便再次出现时复用（保持其内部状态）；块视图直接丢弃
        row.View = null;
        _realized.Remove(row);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var fullWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : 720;
        var width = Math.Min(fullWidth, MaxContentWidth);
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 800;
        if (!width.Equals(_width))
        {
            _width = width;
            foreach (var row in _rows)
            {
                row.Measured = false;
            }
            _topsDirty = true;
        }
        _style ??= MarkdownStyle.Resolve(this);
        EnsureRows();
        EnsureTops();
        RestoreAnchor();
        ParseVisibleDeferred(height);

        // 记录锚点：视口内第一行及其相对视口的位置
        var anchorIndex = FirstRowAt(_offset.Y);
        var anchor = anchorIndex < _rows.Count ? _rows[anchorIndex] : null;
        var anchorDelta = anchor is null ? 0 : anchor.Top - _offset.Y;

        for (var pass = 0; pass < 3; pass++)
        {
            if (_pinned)
            {
                _offset = new Vector(0, Math.Max(0, _extent.Height - height));
            }
            var top = Math.Max(0, _offset.Y - Overscan);
            var bottom = _offset.Y + height + Overscan;
            var changed = false;
            var keep = new HashSet<Row>();
            for (var i = FirstRowAt(top); i < _rows.Count && _rows[i].Top <= bottom; i++)
            {
                var row = _rows[i];
                if (row.Block is null && row.Owner.Entry is MarkdownEntry)
                {
                    continue;
                }
                var view = Realize(row);
                keep.Add(row);
                if (!row.Measured || !view.IsMeasureValid)
                {
                    view.Measure(new Size(width, double.PositiveInfinity));
                    row.Measured = true;
                }
                // 子控件自身变化（例如宿主内容展开 / 折叠）时，布局管理器会先单独重新测量它，
                // 再通知面板；此时测量已有效，但记录的行高已过期，所以总是与当前期望高度比较
                var measured = view.DesiredSize.Height;
                if (Math.Abs(measured - row.Height) > 0.5)
                {
                    row.Height = measured;
                    changed = true;
                }
            }
            foreach (var row in _realized.Where(r => !keep.Contains(r)).ToList())
            {
                Unrealize(row);
            }
            if (!changed)
            {
                break;
            }
            _topsDirty = true;
            EnsureTops();
            if (!_pinned && anchor is not null)
            {
                _offset = new Vector(0, Math.Max(0, anchor.Top - anchorDelta));
            }
        }

        var maxOffset = Math.Max(0, _extent.Height - height);
        _offset = new Vector(0, Math.Clamp(_offset.Y, 0, maxOffset));
        _viewport = new Size(fullWidth, height);
        _extent = new Size(fullWidth, _extent.Height);
        UpdateTail();
        ScrollInvalidated?.Invoke(this, EventArgs.Empty);
        return new Size(fullWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Min(finalSize.Width, MaxContentWidth);
        var x = Math.Max(0, (finalSize.Width - width) / 2);
        foreach (var row in _realized)
        {
            row.View!.Arrange(new Rect(x, row.Top - _offset.Y, width, row.Height));
        }
        return finalSize;
    }

    // 只有最后一个流式文档的末尾片段显示渐显和光标
    private void UpdateTail()
    {
        TextSegmentView? tail = null;
        for (var i = _entries.Count - 1; i >= 0 && tail is null; i--)
        {
            var state = _entries[i];
            if (state.Entry is MarkdownEntry { State: not null } && state.Rows.Count > 0)
            {
                tail = (state.Rows[^1].View as BlockView)?.LastSegment();
                break;
            }
        }
        if (ReferenceEquals(tail, _tail))
        {
            return;
        }
        if (_tail is not null)
        {
            _tail.IsDocumentTail = false;
            _tail.InvalidateVisual();
        }
        _tail = tail;
        if (_tail is not null)
        {
            _tail.IsDocumentTail = true;
            _tail.InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InvalidateStyle();
    }

    /// <summary>滚动到底部并恢复贴底。</summary>
    public void ScrollToEnd()
    {
        _pinned = true;
        InvalidateMeasure();
    }

    // ---- ILogicalScrollable ----

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public bool IsLogicalScrollEnabled => true;

    public Size ScrollSize => new(0, 48);

    public Size PageScrollSize => new(0, Math.Max(48, _viewport.Height - 48));

    public Size Extent => _extent;

    public Size Viewport => _viewport;

    public Vector Offset
    {
        get => _offset;
        set
        {
            if (_settingOffset)
            {
                return;
            }
            var max = Math.Max(0, _extent.Height - _viewport.Height);
            var y = Math.Clamp(value.Y, 0, max);
            if (Math.Abs(y - _offset.Y) < 0.01)
            {
                return;
            }
            _offset = new Vector(0, y);
            _pinned = y >= max - 2;
            _pendingAnchor = null;
            InvalidateMeasure();
        }
    }

    public event EventHandler? ScrollInvalidated;

    public bool BringIntoView(Control target, Rect targetRect)
    {
        foreach (var row in _realized)
        {
            if (row.View is { } view && (ReferenceEquals(view, target) || view.IsVisualAncestorOf(target)))
            {
                var origin = target.TranslatePoint(targetRect.TopLeft, view) ?? default;
                var top = row.Top + origin.Y;
                var bottom = top + targetRect.Height;
                var y = _offset.Y;
                if (top < y)
                {
                    y = top;
                }
                else if (bottom > y + _viewport.Height)
                {
                    y = bottom - _viewport.Height;
                }
                SetOffsetInternal(y);
                return true;
            }
        }
        return false;
    }

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    private void SetOffsetInternal(double y)
    {
        var max = Math.Max(0, _extent.Height - _viewport.Height);
        _offset = new Vector(0, Math.Clamp(y, 0, max));
        _pinned = _offset.Y >= max - 2;
        _pendingAnchor = null;
        InvalidateMeasure();
        _settingOffset = true;
        try
        {
            ScrollInvalidated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _settingOffset = false;
        }
    }

    // ---- ISelectionHost ----

    Control ISelectionHost.HostControl => this;

    IEnumerable<ISelectableSegment> ISelectionHost.RealizedSegments() =>
        _rows.Where(r => r.View is BlockView).SelectMany(r => ((BlockView)r.View!).Segments());

    int ISelectionHost.EntryCount => _entries.Count;

    Plugins.PluginRegistry? ISelectionHost.Plugins => MarkdownServices.GetServices(this).Registry;

    DocumentSnapshot? ISelectionHost.GetSnapshot(int entry)
    {
        if (entry < 0 || entry >= _entries.Count)
        {
            return null;
        }
        var state = _entries[entry];
        if (state.Entry is MarkdownEntry markdown && (!markdown.IsParsed || state.Applied is null))
        {
            // 选区 / 复制跨过尚未解析的条目：就地解析，下一帧再展开它的行
            markdown.EnsureParsed();
            RequestFrame();
            return markdown.Document.Latest;
        }
        return state.Applied;
    }

    string ISelectionHost.GetSource(int entry, int start, int length) =>
        entry >= 0 && entry < _entries.Count && _entries[entry].Entry is MarkdownEntry markdown ? markdown.Document.GetText(start, length) : "";

    void ISelectionHost.OnSelectionDrag(Point hostPoint)
    {
        if (hostPoint.Y < 0)
        {
            SetOffsetInternal(_offset.Y + Math.Max(hostPoint.Y, -40));
        }
        else if (hostPoint.Y > _viewport.Height)
        {
            SetOffsetInternal(_offset.Y + Math.Min(hostPoint.Y - _viewport.Height, 40));
        }
    }
}
