using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;
using XrayProxyWorker.Tools;

namespace XrayProxyWorker.Services.Implementations
{
    public class V2RayConfigService : IV2RayConfigService
    {
        private readonly HttpClient _httpClient;
        private const string ConfigUrl = "https://raw.githubusercontent.com/Epodonios/v2ray-configs/main/Sub1.txt";

        public V2RayConfigService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<V2RayConfig>> GetConfigsAsync()
        {
            var rawContent = await _httpClient.GetStringAsync(ConfigUrl);

            // Split and filter:
            // 1. Split by lines
            // 2. Remove empty lines
            // 3. Keep only lines starting with a known protocol (vless, vmess, trojan, ss)
            var protocols = new[] { "vless://", "vmess://", "trojan://", "ss://" };

            var validConfigs = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                         .Where(line => protocols.Any(p => line.StartsWith(p)))
                                         .ToList();

            var parsedConfigs = validConfigs
                .Select(c => c.ToV2RayConfig())
                .Where(config => config is not null)
                .Cast<V2RayConfig>()
                .ToList();

            return parsedConfigs;
        }
    }
}
