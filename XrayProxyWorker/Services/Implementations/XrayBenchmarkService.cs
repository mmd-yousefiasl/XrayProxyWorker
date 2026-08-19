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

                _logger.LogInformation(
    "Validating generated Xray configuration...");

                bool configValid =
                    await _processManager.ValidateConfigAsync(
                        configPath,
                        ct);

                if (!configValid)
                {
                    _logger.LogError(
                        "Generated Xray configuration is invalid. Benchmark aborted.");

                    return new();
                }

                _logger.LogInformation(
                    "Xray configuration is valid.");

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

                using var semaphore =
                    new SemaphoreSlim(MaxConcurrentTests);

                const int barWidth = 40;

                int completed = 0;
                int workingCount = 0;
                int failedCount = 0;

                var benchmarkStopwatch =
                    Stopwatch.StartNew();

                var tasks = generated.Select(async generatedProxy =>
                {
                    await semaphore.WaitAsync(ct);

                    try
                    {
                        var test =
                            await _tester.TestPortLatencyAsync(
                                generatedProxy.Port,
                                ct);

                        Interlocked.Increment(ref completed);

                        if (!test.Success)
                        {
                            Interlocked.Increment(ref failedCount);
                            return null;
                        }

                        Interlocked.Increment(ref workingCount);

                        return new ProxyBenchmarkResult(
                            generatedProxy.Config,
                            test.LatencyMs,
                            test.DownloadMbps);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();


                // ---------------------------------------------------------
                // Live progress display
                // Updates every 250ms even when no proxy finishes.
                // ---------------------------------------------------------

                var progressTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            int current =
                                Volatile.Read(ref completed);

                            if (current >= generated.Count)
                                break;

                            int currentWorking =
                                Volatile.Read(ref workingCount);

                            int currentFailed =
                                Volatile.Read(ref failedCount);

                            double progress =
                                generated.Count == 0
                                    ? 0
                                    : (double)current / generated.Count;

                            int filled =
                                (int)(progress * barWidth);

                            string bar =
                                new string('#', filled) +
                                new string('.', barWidth - filled);

                            Console.Write(
                                $"\r[{bar}] " +
                                $"{current}/{generated.Count} " +
                                $"| Working: {currentWorking} " +
                                $"| Failed: {currentFailed} " +
                                $"| {benchmarkStopwatch.Elapsed:mm\\:ss}");

                            await Task.Delay(250, ct);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }, ct);


                // ---------------------------------------------------------
                // Wait for all proxy tests.
                // ---------------------------------------------------------

                var results =
                    await Task.WhenAll(tasks);

                benchmarkStopwatch.Stop();

                try
                {
                    await progressTask;
                }
                catch (OperationCanceledException)
                {
                }


                // ---------------------------------------------------------
                // Final progress line
                // ---------------------------------------------------------

                Console.Write(
                    $"\r[{new string('#', barWidth)}] " +
                    $"{generated.Count}/{generated.Count} " +
                    $"| Working: {workingCount} " +
                    $"| Failed: {failedCount} " +
                    $"| {benchmarkStopwatch.Elapsed:mm\\:ss}");

                Console.WriteLine();
                Console.WriteLine();

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