using Avalonia.Threading;
using StreamMark.Avalonia.Rendering;
using StreamMark.Core.Pacing;
using StreamMark.Core.Parsing;
using StreamMark.Core.Streaming;

namespace StreamMark.Avalonia;

/// <summary>
/// 一条流式回答：把网络分片经 <see cref="StreamPacer"/> 平滑后送入 <see cref="StreamingDocument"/>，
/// 并记录每批文本的显示时间供渐显动画使用。<see cref="Push"/> / <see cref="Complete"/> 可在任意线程调用。
/// </summary>
public sealed class MarkdownStream
{
    private readonly StreamPacer _pacer;
    private readonly bool _smooth;
    private readonly DispatcherTimer _timer;
    private bool _completeRequested;

    public MarkdownStream(StreamPacerOptions? pacing = null, bool smooth = true, TimeSpan? fadeDuration = null, MarkdownProfile? profile = null)
    {
        _pacer = new StreamPacer(pacing);
        _smooth = smooth;
        Document = new StreamingDocument(profile);
        State = new StreamingState(fadeDuration ?? TimeSpan.FromMilliseconds(220));
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
    }

    public StreamingDocument Document { get; }

    public StreamingState State { get; }

    /// <summary>流（包括平滑放出的积压）已全部结束。</summary>
    public event Action? Completed;

    public void Push(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (_completeRequested)
            {
                return;
            }
            State.IsActive = true;
            if (!_smooth)
            {
                Document.Append(text);
                State.RecordReveal(text.Length, StreamClock.Now);
                return;
            }
            _pacer.Push(text, StreamClock.Now);
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                Tick();
            }
        });
    }

    public void Complete() => Dispatcher.UIThread.Post(() =>
    {
        if (_completeRequested)
        {
            return;
        }
        _completeRequested = true;
        _pacer.Finish(StreamClock.Now);
        if (!_smooth || _pacer.IsDrained)
        {
            Finish();
        }
        else if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    });

    /// <summary>立即显示全部积压（例如用户点击"跳到结尾"或窗口被隐藏）。</summary>
    public void Flush() => Dispatcher.UIThread.Post(() =>
    {
        var text = _pacer.Flush(StreamClock.Now);
        if (text.Length > 0)
        {
            Document.Append(text);
        }
    });

    private void Tick()
    {
        var now = StreamClock.Now;
        var text = _pacer.Tick(now);
        if (text.Length > 0)
        {
            Document.Append(text);
            State.RecordReveal(text.Length, now);
        }
        if (_pacer.Backlog == 0)
        {
            if (_completeRequested)
            {
                Finish();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    private void Finish()
    {
        _timer.Stop();
        Document.Complete();
        State.IsActive = false;
        Completed?.Invoke();
    }
}
