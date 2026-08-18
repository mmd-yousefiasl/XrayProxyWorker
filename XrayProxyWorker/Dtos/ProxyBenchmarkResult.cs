using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Dtos
{
    public record ProxyBenchmarkResult(
        V2RayConfig Config,
        long LatencyMs,
        double DownloadMbps);
}
