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
                    ConnectTimeout = TimeSpan.FromMilliseconds(3000)
                };

                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(5000) };

                var sw = Stopwatch.StartNew();
                using var response = await client.GetAsync("https://www.google.com/generate_204", ct);
                sw.Stop();

                if (response.IsSuccessStatusCode) return sw.ElapsedMilliseconds;
            }
            catch { /* Suppress benchmark connection timeouts */ }

            return long.MaxValue;
        }
    }
}
