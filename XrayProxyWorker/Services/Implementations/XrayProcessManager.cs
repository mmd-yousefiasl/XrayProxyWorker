using System.Diagnostics;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class XrayProcessManager : IProcessManager
    {
        #region Fields
        private readonly ILogger<XrayProcessManager> _logger;
        #endregion

        #region Ctor
        public XrayProcessManager(ILogger<XrayProcessManager> logger)
        {
            _logger = logger;
        }
        #endregion

        public void KillOrphanedWorkerProcesses()
        {
            _logger.LogInformation("Cleaning up any orphaned Xray core processes belonging to this worker...");
            string currentAppFolder = AppContext.BaseDirectory;

            foreach (var oldProcess in Process.GetProcessesByName("xray"))
            {
                try
                {
                    string? processPath = oldProcess.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(processPath) && processPath.StartsWith(currentAppFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        oldProcess.Kill(true);
                        _logger.LogInformation("Killed zombie worker Xray process PID: {Pid}", oldProcess.Id);
                    }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch { /* Fallback */ }
            }
        }

        public Process StartXrayEngine(string configPath)
        {
            string xrayExeName = OperatingSystem.IsWindows() ? "xray.exe" : "xray";
            string xrayDirectory = Path.Combine(AppContext.BaseDirectory, "Core");
            string xrayPath = Path.Combine(xrayDirectory, xrayExeName);

            if (!File.Exists(xrayPath))
                throw new FileNotFoundException($"CRITICAL: Xray executable not found at: {xrayPath}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = xrayPath,
                    Arguments = $"run -c \"{configPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = xrayDirectory
                }
            };

            process.OutputDataReceived += (s, e) => HandleLog(e.Data, isError: false);
            process.ErrorDataReceived += (s, e) => HandleLog(e.Data, isError: true);

            return process;
        }

        private void HandleLog(string? data, bool isError)
        {
            if (string.IsNullOrEmpty(data) || data.Contains("[Warning]") || data.Contains("deprecated")) return;
            _logger.LogInformation(isError ? "[XRAY-ERR] {Data}" : "[XRAY-OUT] {Data}", data);
        }
    }
}
