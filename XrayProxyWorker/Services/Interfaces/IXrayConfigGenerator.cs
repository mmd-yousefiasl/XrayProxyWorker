using XrayProxyWorker.Dtos;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IXrayConfigGenerator
    {
        List<GeneratedProxy> GenerateMasterConfig(
            List<V2RayConfig> configs,
            string outputPath,
            int basePortPoolStart);
    }
}
