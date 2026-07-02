using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class ProxyConfigValidator : IProxyConfigValidator
    {
        public List<V2RayConfig> FilterValidConfigs(List<V2RayConfig> original)
        {
            if (original == null) return new();

            return original.Where(c =>
                !string.IsNullOrWhiteSpace(c.Address) &&
                !string.IsNullOrWhiteSpace(c.UserId) &&
                c.UserId.Trim().Length > 10
            ).ToList();
        }
    }
}
