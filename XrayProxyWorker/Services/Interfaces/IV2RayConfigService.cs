using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IV2RayConfigService
    {
        Task<List<V2RayConfig>> GetConfigsAsync(string configsUrl);
    }
}
