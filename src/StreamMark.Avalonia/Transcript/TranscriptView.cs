using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using StreamMark.Avalonia.Selection;

namespace StreamMark.Avalonia.Transcript;

/// <summary>
/// 长会话视图：滚动容器 + <see cref="TranscriptPanel"/>。沿用宿主主题中 <see cref="ScrollViewer"/> 的模板。
/// 向 <see cref="Entries"/> 添加 <see cref="MarkdownEntry"/> / <see cref="ContentEntry"/> 即可。
/// </summary>
public class TranscriptView : ScrollViewer
{
    public TranscriptView()
    {
        Panel = new TranscriptPanel();
        Panel.SetEntries(Entries);
        Content = Panel;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public ObservableCollection<TranscriptEntry> Entries { get; } = [];

    public TranscriptPanel Panel { get; }

    public SelectionModel Selection => Panel.Selection;

    public SelectionController SelectionController => Panel.SelectionController;

    public bool IsPinnedToBottom => Panel.IsPinnedToBottom;

    /// <summary>滚动到底部并恢复贴底跟随。</summary>
    public new void ScrollToEnd() => Panel.ScrollToEnd();

    /// <summary>内容列的最大宽度（居中）。</summary>
    public double MaxContentWidth
    {
        get => Panel.MaxContentWidth;
        set => Panel.MaxContentWidth = value;
    }

    public double EntrySpacing
    {
        get => Panel.EntrySpacing;
        set => Panel.EntrySpacing = value;
    }
}
