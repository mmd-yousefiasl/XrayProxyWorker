using XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface ILatencyTester
    {
        Task<ProxyTestResult> TestPortLatencyAsync(
            int port,
            CancellationToken ct);
    }
}
