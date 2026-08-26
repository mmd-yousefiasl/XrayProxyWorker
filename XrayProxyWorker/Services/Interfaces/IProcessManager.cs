using System.Diagnostics;

namespace XrayProxyWorker.Services.Interfaces
{
    public interface IProcessManager
    {
        void KillOrphanedWorkerProcesses();
        Process StartXrayEngine(string configPath);

        /// <summary>
        /// Waits until the Xray engine accepts TCP connections on the
        /// given local SOCKS port (or fails/exits). Replaces fixed sleeps.
        /// </summary>
        Task<bool> WaitUntilReadyAsync(
            Process process,
            int probePort,
            TimeSpan timeout,
            CancellationToken ct);

        Task<bool> ValidateConfigAsync(
            string configPath,
            CancellationToken ct);
    }
}
