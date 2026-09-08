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

        /// <summary>
        /// Ping-only benchmark: tests configs in small batches (each
        /// batch gets its own short-lived Xray engine so the port pool
        /// is reused) and returns every proxy that responds.
        /// </summary>
        Task<List<ProxyBenchmarkResult>>
               FindReachableProxiesAsync(
                   List<V2RayConfig> configs,
                   CancellationToken ct);
    }
}
