using XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IXrayConfigGenerator
    {
        int GenerateMasterConfig(List<V2RayConfig> configs, string outputPath, int basePortPoolStart);
    }
}
