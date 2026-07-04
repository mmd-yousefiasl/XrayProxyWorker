using XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface ITelegramPublisher
    {
        Task BroadcastTopProxiesAsync(List<(V2RayConfig Config, long Latency)> topTen, CancellationToken ct);
    }
}
