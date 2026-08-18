using System.Diagnostics;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class XrayBenchmarkService : IXrayBenchmarkService
    {
        private readonly ILogger<XrayBenchmarkService> _logger;
        private readonly IXrayConfigGenerator _configGenerator;
        private readonly IProcessManager _processManager;
        private readonly ILatencyTester _tester;

        private const int PortPoolStart = 20000;
        private const int MaxConcurrentTests = 30;
        private const int XrayStartupDelayMs = 4000;

        public XrayBenchmarkService(
            ILogger<XrayBenchmarkService> logger,
            IXrayConfigGenerator configGenerator,
            IProcessManager processManager,
            ILatencyTester tester)
        {
            _logger = logger;
            _configGenerator = configGenerator;
            _processManager = processManager;
            _tester = tester;
        }

        public async Task<List<ProxyBenchmarkResult>> FindAllFastProxiesAsync(
            List<V2RayConfig> configs,
            CancellationToken ct)
        {
            if (configs == null || configs.Count == 0)
                return new();

            var uniqueConfigs = configs
                .Where(c => !string.IsNullOrWhiteSpace(c.Url))
                .GroupBy(
                    c => c.Url.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (uniqueConfigs.Count == 0)
                return new();

            string configPath = Path.Combine(
                AppContext.BaseDirectory,
                "base_config.json");

            Process? process = null;

            try
            {
                _processManager.KillOrphanedWorkerProcesses();

                var generated = _configGenerator.GenerateMasterConfig(
                    uniqueConfigs,
                    configPath,
                    PortPoolStart);

                if (generated.Count == 0)
                {
                    _logger.LogWarning(
                        "No valid proxies were generated.");

                    return new();
                }

                process = _processManager.StartXrayEngine(configPath);

                if (!process.Start())
                {
                    _logger.LogError("Unable to start Xray.");
                    return new();
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Delay(XrayStartupDelayMs, ct);

                if (process.HasExited)
                {
                    _logger.LogError(
                        "Xray exited before benchmark began.");

                    return new();
                }

                Console.WriteLine();
                Console.WriteLine($"Testing {generated.Count} proxies...");
                Console.WriteLine();

                using var semaphore = new SemaphoreSlim(MaxConcurrentTests);

                int completed = 0;
                double bestSpeed = 0;

                var tasks = generated.Select(async generatedProxy =>
                {
                    await semaphore.WaitAsync(ct);

                    try
                    {
                        var test = await _tester.TestPortLatencyAsync(
                            generatedProxy.Port,
                            ct);

                        int current = Interlocked.Increment(ref completed);

                        // Progress indicator
                        Console.Write("#");

                        // Every 25 completed tests show progress
                        if (current % 25 == 0 || current == generated.Count)
                        {
                            lock (Console.Out)
                            {
                                Console.WriteLine(
                                    $" {current}/{generated.Count} | Best: {bestSpeed:F2} Mbps");
                            }
                        }

                        if (!test.Success)
                            return null;

                        // Update best speed safely
                        double currentBest;

                        lock (Console.Out)
                        {
                            if (test.DownloadMbps > bestSpeed)
                                bestSpeed = test.DownloadMbps;

                            currentBest = bestSpeed;
                        }

                        return new ProxyBenchmarkResult(
                            generatedProxy.Config,
                            test.LatencyMs,
                            test.DownloadMbps);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var results = await Task.WhenAll(tasks);

                var working = results
                    .Where(x => x != null)
                    .Select(x => x!)
                    .OrderByDescending(x => x.DownloadMbps)
                    .ThenBy(x => x.LatencyMs)
                    .ToList();

                Console.WriteLine();
                Console.WriteLine(
                    $"Working proxies: {working.Count}/{generated.Count}");

                _logger.LogInformation(
                    "Benchmark complete. {Working}/{Total} proxies working.",
                    working.Count,
                    generated.Count);

                return working;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                Console.WriteLine("Benchmark cancelled.");
                throw;
            }
            finally
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill(true);
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                }
                finally
                {
                    process?.Dispose();
                }

                try
                {
                    if (File.Exists(configPath))
                        File.Delete(configPath);
                }
                catch
                {
                }
            }
        }
    }
}