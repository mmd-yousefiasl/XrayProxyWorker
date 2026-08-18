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

        public async Task<ProxyTestResult>
            TestPortLatencyAsync(
                int port,
                CancellationToken ct)
        {
            try
            {
                using var handler =
                    new SocketsHttpHandler
                    {
                        Proxy =
                            new WebProxy(
                                $"socks5://127.0.0.1:{port}"),

                        UseProxy = true,

                        ConnectTimeout =
                            TimeSpan.FromSeconds(
                                ConnectTimeoutSeconds),

                        PooledConnectionLifetime =
                            TimeSpan.FromSeconds(30),

                        AutomaticDecompression =
                            System.Net.DecompressionMethods.None
                    };

                using var client =
                    new HttpClient(handler)
                    {
                        Timeout =
                            TimeSpan.FromSeconds(
                                RequestTimeoutSeconds)
                    };

                var stopwatch =
                    Stopwatch.StartNew();

                using var response =
                    await client.GetAsync(
                        TestUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);

                long headersLatency =
                    stopwatch.ElapsedMilliseconds;

                if (!response.IsSuccessStatusCode)
                {
                    return ProxyTestResult.Failed();
                }

                await using var stream =
                    await response.Content
                        .ReadAsStreamAsync(ct);

                byte[] buffer =
                    new byte[64 * 1024];

                long downloadedBytes = 0;

                while (downloadedBytes <
                       TestDownloadBytes)
                {
                    int remaining =
                        TestDownloadBytes -
                        (int)downloadedBytes;

                    int requested =
                        Math.Min(
                            buffer.Length,
                            remaining);

                    int read =
                        await stream.ReadAsync(
                            buffer.AsMemory(
                                0,
                                requested),
                            ct);

                    if (read <= 0)
                        break;

                    downloadedBytes += read;
                }

                stopwatch.Stop();

                if (downloadedBytes <= 0)
                {
                    return ProxyTestResult.Failed();
                }

                double seconds =
                    stopwatch.Elapsed.TotalSeconds;

                if (seconds <= 0)
                {
                    return ProxyTestResult.Failed();
                }

                double downloadMbps =
                    downloadedBytes *
                    8.0 /
                    seconds /
                    1_000_000.0;

                return new ProxyTestResult(
                    true,
                    headersLatency,
                    downloadMbps);
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested)
            {
                return ProxyTestResult.Failed();
            }
            catch
            {
                return ProxyTestResult.Failed();
            }
        }
    }
}