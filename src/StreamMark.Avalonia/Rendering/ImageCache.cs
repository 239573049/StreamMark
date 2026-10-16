using Avalonia.Media;
using Avalonia.Threading;
using StreamMark.Avalonia.Extensions;

namespace StreamMark.Avalonia.Rendering;

/// <summary>
/// 按 URL 缓存已加载的图片，避免流式重解析时重复加载；同一 URL 的并发请求合并为一次。
/// 加载完成后在界面线程回调。
/// </summary>
internal static class ImageCache
{
    private const int Capacity = 128;

    private static readonly Dictionary<string, IImage?> Loaded = new();
    private static readonly LinkedList<string> Order = new();
    private static readonly Dictionary<string, List<Action>> Pending = new();

    /// <summary>已加载（或确定无法加载，值为 null）时返回 true。</summary>
    public static bool TryGet(string url, out IImage? image)
    {
        Dispatcher.UIThread.VerifyAccess();
        return Loaded.TryGetValue(url, out image);
    }

    /// <summary>开始加载（若尚未加载），完成后调用 <paramref name="onLoaded"/>。</summary>
    public static void Request(string url, IImageLoader loader, Action onLoaded)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (Loaded.ContainsKey(url))
        {
            onLoaded();
            return;
        }
        if (Pending.TryGetValue(url, out var waiters))
        {
            waiters.Add(onLoaded);
            return;
        }
        Pending[url] = [onLoaded];
        _ = LoadAsync(url, loader);
    }

    private static async Task LoadAsync(string url, IImageLoader loader)
    {
        IImage? image = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            image = await loader.LoadAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            image = null;
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Loaded[url] = image;
            Order.AddLast(url);
            while (Order.Count > Capacity)
            {
                var oldest = Order.First!.Value;
                Order.RemoveFirst();
                // 不主动释放：被淘汰的图片可能仍在某个视图中显示，交给 GC
                Loaded.Remove(oldest);
            }
            if (Pending.Remove(url, out var waiters))
            {
                foreach (var waiter in waiters)
                {
                    waiter();
                }
            }
        });
    }
}
