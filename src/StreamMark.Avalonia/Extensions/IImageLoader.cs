using Avalonia.Media;

namespace StreamMark.Avalonia.Extensions;

/// <summary>
/// 图片加载器。默认实现只加载 <c>data:</c> URI；网络图片需要宿主提供实现（并自行决定来源白名单，
/// 因为自动加载的图片 URL 可能被提示注入用来外泄数据）。
/// </summary>
public interface IImageLoader
{
    Task<IImage?> LoadAsync(string url, CancellationToken cancellationToken);
}
