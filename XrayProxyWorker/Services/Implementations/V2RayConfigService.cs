using System.Text;
using Microsoft.Extensions.Options;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;
using XrayProxyWorker.Extensions;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class V2RayConfigService : IV2RayConfigService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<IV2RayConfigService> _logger;
        private readonly BenchmarkOptions _options;

        public V2RayConfigService(HttpClient httpClient,
            ILogger<IV2RayConfigService> logger,
            IOptions<BenchmarkOptions> options)
        {
            _httpClient = httpClient;
            _logger = logger;
            _options = options.Value;
        }

        public async Task<List<V2RayConfig>> GetConfigsAsync(string configsUrl)
        {
            int MaxConfigs =
                _options.MaxConfigs;

            var protocols = new[]
            {
        "vless://",
        "vmess://",
        "trojan://",
        "ss://"
    };

            try
            {
                var rawContent = await _httpClient.GetStringAsync(configsUrl);

                if (string.IsNullOrWhiteSpace(rawContent))
                    return new List<V2RayConfig>();

                // Most subscriptions are base64-encoded blobs of links.
                // If none of the supported schemes appear in the raw
                // content, try decoding the whole body once.

                if (!protocols.Any(p =>
                        rawContent.Contains(
                            p,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    string decoded =
                        TryDecodeBase64(rawContent);

                    if (!string.IsNullOrWhiteSpace(decoded))
                        rawContent = decoded;
                }

                var lines = rawContent
                    .Split(
                        new[] { "\r\n", "\r", "\n" },
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);

                var parsedConfigs = lines
                    .Where(line =>
                        protocols.Any(protocol =>
                            line.StartsWith(
                                protocol,
                                StringComparison.OrdinalIgnoreCase)))
                    .Select(line => line.ToV2RayConfig())
                    .Where(config => config is not null)
                    .Cast<V2RayConfig>()
                    .DistinctBy(c =>
                       c.Url.ToNormalizedProxyKey(),
                       StringComparer.OrdinalIgnoreCase)
                    .Take(MaxConfigs)
                    .ToList();

                _logger.LogInformation(
                    "Downloaded and parsed {Count} configs (maximum {Max}).",
                    parsedConfigs.Count,
                    MaxConfigs);

                return parsedConfigs;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to fetch configs from {Url}",
                    configsUrl);

                return new List<V2RayConfig>();
            }
        }

        private static string TryDecodeBase64(string content)
        {
            try
            {
                string normalized =
                    content
                        .Trim()
                        .Replace("\r", "")
                        .Replace("\n", "")
                        .Replace(" ", "+")
                        .Replace("-", "+")
                        .Replace("_", "/");

                while (normalized.Length % 4 != 0)
                {
                    normalized += "=";
                }

                byte[] bytes =
                    Convert.FromBase64String(normalized);

                string decoded =
                    Encoding.UTF8.GetString(bytes);

                // Sanity check: decoding random text must not win.

                return decoded.Contains(
                    "://",
                    StringComparison.OrdinalIgnoreCase)
                    ? decoded
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
