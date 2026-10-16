using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace StreamMark.Avalonia.Extensions;

/// <summary>只加载 <c>data:image/*;base64,</c> 内联图片，不发起任何网络请求。</summary>
public sealed class DataUriImageLoader : IImageLoader
{
    public Task<IImage?> LoadAsync(string url, CancellationToken cancellationToken)
    {
        if (!url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IImage?>(null);
        }
        var comma = url.IndexOf(',');
        if (comma < 0 || !url.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IImage?>(null);
        }
        return Task.Run<IImage?>(() =>
        {
            try
            {
                var bytes = Convert.FromBase64String(url[(comma + 1)..]);
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }, cancellationToken);
    }
}
