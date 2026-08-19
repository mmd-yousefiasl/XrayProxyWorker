using System.Diagnostics;
using System.Net;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class SocketsLatencyTester : ILatencyTester
    {
        private const int ConnectTimeoutSeconds = 7;
        private const int RequestTimeoutSeconds = 10;

        private const int TestDownloadBytes =
            2 * 1024 * 1024;

        private const string TestUrl =
            "https://speed.cloudflare.com/__down?bytes=2097152";

        public async Task<ProxyTestResult> TestPortLatencyAsync(
            int port,
            CancellationToken ct)
        {
            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct);

            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                using var handler = new SocketsHttpHandler
                {
                    Proxy = new WebProxy(
                        $"socks5://127.0.0.1:{port}"),

                    UseProxy = true,

                    ConnectTimeout =
                        TimeSpan.FromSeconds(7),

                    PooledConnectionLifetime =
                        TimeSpan.FromSeconds(30),

                    AutomaticDecompression =
                        System.Net.DecompressionMethods.None
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };

                var stopwatch = Stopwatch.StartNew();

                using var response =
                    await client.GetAsync(
                        "https://speed.cloudflare.com/__down?bytes=2097152",
                        HttpCompletionOption.ResponseHeadersRead,
                        timeoutCts.Token);

                long latency =
                    stopwatch.ElapsedMilliseconds;

                if (!response.IsSuccessStatusCode)
                    return ProxyTestResult.Failed();

                await using var stream =
                    await response.Content.ReadAsStreamAsync(
                        timeoutCts.Token);

                byte[] buffer = new byte[64 * 1024];

                const long targetBytes = 2 * 1024 * 1024;

                long downloaded = 0;

                while (downloaded < targetBytes)
                {
                    int toRead = (int)Math.Min(
                        buffer.Length,
                        targetBytes - downloaded);

                    int read = await stream.ReadAsync(
                        buffer.AsMemory(0, toRead),
                        timeoutCts.Token);

                    if (read <= 0)
                        break;

                    downloaded += read;
                }

                stopwatch.Stop();

                if (downloaded <= 0)
                    return ProxyTestResult.Failed();

                double seconds =
                    stopwatch.Elapsed.TotalSeconds;

                double mbps =
                    downloaded * 8.0 /
                    seconds /
                    1_000_000.0;

                return new ProxyTestResult(
                    true,
                    latency,
                    mbps);
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested)
            {
                // Individual proxy timeout.
                return ProxyTestResult.Failed();
            }
            catch
            {
                return ProxyTestResult.Failed();
            }
        }
    }
}