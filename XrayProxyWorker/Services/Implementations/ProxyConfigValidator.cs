using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class ProxyConfigValidator : IProxyConfigValidator
    {
        public List<V2RayConfig> FilterValidConfigs(
            List<V2RayConfig> original)
        {
            if (original == null || original.Count == 0)
                return new();

            return original
                .Where(IsValid)
                .ToList();
        }

        private static bool IsValid(V2RayConfig config)
        {
            if (config == null)
                return false;

            if (string.IsNullOrWhiteSpace(config.Protocol))
                return false;

            if (string.IsNullOrWhiteSpace(config.Address))
                return false;

            if (config.Port <= 0 || config.Port > 65535)
                return false;

            if (string.IsNullOrWhiteSpace(config.UserId))
                return false;

            return config.Protocol.ToLowerInvariant() switch
            {
                "vless" =>
                    Guid.TryParse(config.UserId, out _),

                "vmess" =>
                    Guid.TryParse(config.UserId, out _),

                "trojan" =>
                    config.UserId.Length >= 4,

                "shadowsocks" or "ss" =>
                    config.UserId.Length >= 1,

                _ => false
            };
        }
    }
}