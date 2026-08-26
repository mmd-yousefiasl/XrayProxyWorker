using XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface ILatencyTester
    {
        /// <summary>
        /// Cheap phase-1 check: confirms the proxy can reach the internet
        /// and measures time-to-first-byte. No large download involved.
        /// </summary>
        Task<ProxyTestResult> TestReachabilityAsync(
            int port,
            CancellationToken ct);

        /// <summary>
        /// Phase-2 speed test: downloads the full payload through the
        /// proxy and reports throughput excluding handshake time.
        /// </summary>
        Task<ProxyTestResult> TestSpeedAsync(
            int port,
            CancellationToken ct);
    }
}
