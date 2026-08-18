using XrayProxyWorker.Dtos;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IXrayBenchmarkService
    {
        Task<List<ProxyBenchmarkResult>>
               FindAllFastProxiesAsync(
                   List<V2RayConfig> configs,
                   CancellationToken ct);
    }
}
