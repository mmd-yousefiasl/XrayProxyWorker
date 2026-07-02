using System.Diagnostics;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class XrayBenchmarkService : IXrayBenchmarkService
    {
        #region Fields
        private readonly ILogger<XrayBenchmarkService> _logger;
        private readonly IProxyConfigValidator _validator;
        private readonly IXrayConfigGenerator _configGenerator;
        private readonly IProcessManager _processManager;
        private readonly ILatencyTester _latencyTester;

        private const int PortPoolStart = 20000;
        private const int MaxConcurrentTests = 40;
        #endregion

        #region Ctor
        public XrayBenchmarkService(
            ILogger<XrayBenchmarkService> logger,
            IProxyConfigValidator validator,
            IXrayConfigGenerator configGenerator,
            IProcessManager processManager,
            ILatencyTester latencyTester)
        {
            _logger = logger;
            _validator = validator;
            _configGenerator = configGenerator;
            _processManager = processManager;
            _latencyTester = latencyTester;
        }
        #endregion

        public async Task<List<(V2RayConfig Config, long Latency)>> FindAllFastProxiesAsync(List<V2RayConfig> configs, CancellationToken ct)
        {
            if (configs == null || !configs.Any()) return new();

            string configPath = Path.Combine(AppContext.BaseDirectory, "base_config.json");
            Process? process = null;

            try
            {
                // Clean up old instances using Process Manager
                _processManager.KillOrphanedWorkerProcesses();

                _logger.LogInformation("Generating master configuration for proxies...");
                int validConfigCount = _configGenerator.GenerateMasterConfig(configs, configPath, PortPoolStart);

                if (validConfigCount == 0)
                {
                    _logger.LogWarning("All provided configurations failed basic structural validation checks.");
                    return new();
                }

                // Build core runtime engine loop configuration parameters
                process = _processManager.StartXrayEngine(configPath);

                _logger.LogInformation("Starting persistent Xray Core wrapper...");
                if (!process.Start())
                {
                    _logger.LogError("Failed to start Xray core process engine.");
                    return new();
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Delay(4000, ct); // Wait initialization

                if (process.HasExited)
                {
                    _logger.LogError("Xray core exited prematurely during initialization.");
                    return new();
                }

                _logger.LogInformation("Beginning throttled speed benchmarks...");
                using var semaphore = new SemaphoreSlim(MaxConcurrentTests);

                var tasks = Enumerable.Range(0, validConfigCount).Select(async (index) =>
                {
                    int assignedPort = PortPoolStart + index;
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var latency = await _latencyTester.TestPortLatencyAsync(assignedPort, ct);
                        return (Index: index, Latency: latency, Success: latency < long.MaxValue);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var results = await Task.WhenAll(tasks);
                var validResults = new List<(V2RayConfig Config, long Latency)>();
                var validatedConfigs = _validator.FilterValidConfigs(configs);

                foreach (var res in results.Where(r => r.Success).OrderBy(p => p.Latency))
                {
                    if (res.Index < validatedConfigs.Count)
                    {
                        validResults.Add((validatedConfigs[res.Index], res.Latency));
                    }
                }

                _logger.LogInformation("Benchmark complete. Found {Count} working proxies.", validResults.Count);
                return validResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled execution error occurred while processing core benchmarks.");
                return new();
            }
            finally
            {
                if (process != null && !process.HasExited)
                {
                    try { process.Kill(true); } catch { }
                    process.Dispose();
                }
                if (File.Exists(configPath))
                {
                    try { File.Delete(configPath); } catch { }
                }
            }
        }
    }
}