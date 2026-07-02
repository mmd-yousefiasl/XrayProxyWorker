using XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IXrayBenchmarkService
    {
        Task<List<(V2RayConfig Config, long Latency)>> FindAllFastProxiesAsync(List<V2RayConfig> configs, CancellationToken ct);
    }
}
