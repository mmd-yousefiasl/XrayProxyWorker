using XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IProxyConfigValidator
    {
        List<V2RayConfig> FilterValidConfigs(List<V2RayConfig> original);
    }
}
