using System.Diagnostics;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IProcessManager
    {
        void KillOrphanedWorkerProcesses();
        Process StartXrayEngine(string configPath);
    }
}
