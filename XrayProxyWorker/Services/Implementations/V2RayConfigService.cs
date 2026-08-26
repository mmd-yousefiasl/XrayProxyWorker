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
                       NormalizeProxyUrl(c.Url),
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

        private static string NormalizeProxyUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            url = url.Trim();

            // Remove remark
            int hashIndex = url.IndexOf('#');

            if (hashIndex >= 0)
            {
                url = url[..hashIndex];
            }

            return url.ToLowerInvariant();
        }
    }
}
