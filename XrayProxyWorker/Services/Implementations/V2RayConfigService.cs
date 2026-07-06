using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;
using XrayProxyWorker.Tools;

namespace XrayProxyWorker.Services.Implementations
{
    public class V2RayConfigService : IV2RayConfigService
    {
        private readonly HttpClient _httpClient;
        private const string ConfigUrl1 = "https://raw.githubusercontent.com/Epodonios/v2ray-configs/main/Sub1.txt";
        private const string ConfigUrl2 = "https://raw.githubusercontent.com/Epodonios/v2ray-configs/main/Sub2.txt";
        private readonly ILogger<IV2RayConfigService> _logger;

        public V2RayConfigService(HttpClient httpClient,
            ILogger<IV2RayConfigService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<V2RayConfig>> GetConfigsAsync()
        {
            var allLines = new List<string>();
            var protocols = new[] { "vless://", "vmess://", "trojan://", "ss://" };

            // ۱. دریافت دیتای لینک اول با مدیریت خطا
            try
            {
                var rawContent1 = await _httpClient.GetStringAsync(ConfigUrl1);
                if (!string.IsNullOrWhiteSpace(rawContent1))
                {
                    var lines = rawContent1.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    allLines.AddRange(lines);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch configs from ConfigUrl1");
            }

            // ۲. دریافت دیتای لینک دوم با مدیریت خطا
            try
            {
                var rawContent2 = await _httpClient.GetStringAsync(ConfigUrl2);
                if (!string.IsNullOrWhiteSpace(rawContent2))
                {
                    var lines = rawContent2.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    allLines.AddRange(lines);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch configs from ConfigUrl2");
            }

            // اگر از هیچ‌کدام از لینک‌ها خطی دریافت نشد، سریعاً خارج شو
            if (!allLines.Any()) return new List<V2RayConfig>();

            // ۳. فیلتر کردن خطوطی که با پروتکل‌های معتبر شروع می‌شوند و پارس کردن آن‌ها
            var parsedConfigs = allLines
                .Where(line => protocols.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.ToV2RayConfig())
                .Where(config => config is not null)
                .Cast<V2RayConfig>()
                .DistinctBy(c => c.Address)
                .ToList();

            return parsedConfigs;
        }
    }
}
