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
            string xrayExeName =
                OperatingSystem.IsWindows()
                    ? "xray.exe"
                    : "xray";

            string xrayDirectory =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Core");

            string xrayPath =
                Path.Combine(
                    xrayDirectory,
                    xrayExeName);

            if (!File.Exists(xrayPath))
            {
                throw new FileNotFoundException(
                    $"CRITICAL: Xray executable not found at: {xrayPath}");
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = xrayPath,

                    Arguments =
                        $"run -c \"{configPath}\"",

                    UseShellExecute = false,
                    CreateNoWindow = true,

                    RedirectStandardError = true,
                    RedirectStandardOutput = true,

                    WorkingDirectory = xrayDirectory
                }
            };

            // Keep the normal Xray output quiet.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };

            return process;
        }

        public async Task<bool> ValidateConfigAsync(
            string configPath,
            CancellationToken ct)
        {
            string xrayExeName =
                OperatingSystem.IsWindows()
                    ? "xray.exe"
                    : "xray";

            string xrayDirectory =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Core");

            string xrayPath =
                Path.Combine(
                    xrayDirectory,
                    xrayExeName);

            var psi = new ProcessStartInfo
            {
                FileName = xrayPath,

                Arguments =
                    $"run -test -c \"{configPath}\"",

                UseShellExecute = false,
                CreateNoWindow = true,

                RedirectStandardOutput = true,
                RedirectStandardError = true,

                WorkingDirectory = xrayDirectory
            };

            using var process = new Process
            {
                StartInfo = psi
            };

            process.Start();

            Task<string> stdoutTask =
                process.StandardOutput.ReadToEndAsync(ct);

            Task<string> stderrTask =
                process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                _logger.LogInformation(
                    "Xray configuration validation passed.");

                return true;
            }

            _logger.LogError(
                "Xray configuration validation FAILED.\n{StdOut}\n{StdErr}",
                stdout,
                stderr);

            return false;
        }
    }
}
