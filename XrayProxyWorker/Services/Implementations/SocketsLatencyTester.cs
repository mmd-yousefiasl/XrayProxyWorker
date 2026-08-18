using System.Diagnostics;
using System.Net;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class SocketsLatencyTester : ILatencyTester
    {
        public async Task<long> TestPortLatencyAsync(int port, CancellationToken ct)
        {
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    Proxy = new WebProxy($"socks5://127.0.0.1:{port}"),
                    UseProxy = true,
                    ConnectTimeout = TimeSpan.FromSeconds(8)
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var sw = Stopwatch.StartNew();

                using var response = await client.GetAsync(
                    "https://www.google.com/generate_204",
                    ct);

                sw.Stop();

                if (response.IsSuccessStatusCode)
                    return sw.ElapsedMilliseconds;
            }
            catch
            {
                // Proxy failed or timed out
            }

            return long.MaxValue;
        }
    }
}
