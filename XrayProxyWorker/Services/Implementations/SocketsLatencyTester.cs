using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Options;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class SocketsLatencyTester : ILatencyTester
    {
        private const string SpeedTestUrl =
            "https://speed.cloudflare.com/__down";

        // Small payload for the reachability phase - enough to prove
        // data actually flows (some transports return headers fine
        // and then hang on the body).
        private const long ReachabilityBytes = 64 * 1024;

        private readonly ILogger<SocketsLatencyTester> _logger;
        private readonly BenchmarkOptions _options;

        public SocketsLatencyTester(
            ILogger<SocketsLatencyTester> logger,
            IOptions<BenchmarkOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        public async Task<ProxyTestResult> TestReachabilityAsync(
            int port,
            CancellationToken ct)
        {
            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct);

            timeoutCts.CancelAfter(
                TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

            try
            {
                using var client = CreateProxyClient(port);

                var stopwatch = Stopwatch.StartNew();

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{SpeedTestUrl}?bytes={ReachabilityBytes}");

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                long ttfbMs =
                    stopwatch.ElapsedMilliseconds;

                if (!response.IsSuccessStatusCode)
                    return ProxyTestResult.Failed();

                await using var stream =
                    await response.Content.ReadAsStreamAsync(
                        timeoutCts.Token);

                byte[] buffer = new byte[16 * 1024];

                long downloaded = 0;

                while (downloaded < ReachabilityBytes)
                {
                    int read = await stream.ReadAsync(
                        buffer,
                        timeoutCts.Token);

                    if (read <= 0)
                        break;

                    downloaded += read;
                }

                if (downloaded <= 0)
                    return ProxyTestResult.Failed();

                stopwatch.Stop();

                return new ProxyTestResult(
                    true,
                    ttfbMs,
                    0);
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested)
            {
                // Individual proxy timeout.
                return ProxyTestResult.Failed();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Reachability check failed for port {Port}.",
                    port);

                return ProxyTestResult.Failed();
            }
        }

        public async Task<ProxyTestResult> TestSpeedAsync(
            int port,
            CancellationToken ct)
        {
            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct);

            timeoutCts.CancelAfter(
                TimeSpan.FromSeconds(_options.SpeedTestTimeoutSeconds));

            try
            {
                using var client = CreateProxyClient(port);

                long targetBytes = _options.DownloadBytes;

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{SpeedTestUrl}?bytes={targetBytes}");

                var stopwatch = Stopwatch.StartNew();

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                long latencyMs =
                    stopwatch.ElapsedMilliseconds;

                if (!response.IsSuccessStatusCode)
                    return ProxyTestResult.Failed();

                await using var stream =
                    await response.Content.ReadAsStreamAsync(
                        timeoutCts.Token);

                // -----------------------------------------------------
                // Restart the clock: handshake time must not count
                // against throughput.
                // -----------------------------------------------------

                stopwatch.Restart();

                byte[] buffer = new byte[256 * 1024];

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
                    latencyMs,
                    mbps);
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested)
            {
                // Individual proxy timeout.
                return ProxyTestResult.Failed();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Speed test failed for port {Port}.",
                    port);

                return ProxyTestResult.Failed();
            }
        }

        private HttpClient CreateProxyClient(int port)
        {
            var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy(
                    $"socks5://127.0.0.1:{port}"),

                UseProxy = true,

                ConnectTimeout = TimeSpan.FromSeconds(
                    _options.ConnectTimeoutSeconds),

                PooledConnectionLifetime =
                    TimeSpan.FromSeconds(30),

                AutomaticDecompression =
                    System.Net.DecompressionMethods.None
            };

            return new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
    }
}
