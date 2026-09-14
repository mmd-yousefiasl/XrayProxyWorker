using System.Diagnostics;
using Microsoft.Extensions.Options;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;
using XrayProxyWorker.Extensions;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class XrayBenchmarkService : IXrayBenchmarkService
    {
        private readonly ILogger<XrayBenchmarkService> _logger;
        private readonly IXrayConfigGenerator _configGenerator;
        private readonly IProcessManager _processManager;
        private readonly ILatencyTester _tester;
        private readonly BenchmarkOptions _options;

        private const int BarWidth = 40;

        public XrayBenchmarkService(
            ILogger<XrayBenchmarkService> logger,
            IXrayConfigGenerator configGenerator,
            IProcessManager processManager,
            ILatencyTester tester,
            IOptions<BenchmarkOptions> options)
        {
            _logger = logger;
            _configGenerator = configGenerator;
            _processManager = processManager;
            _tester = tester;
            _options = options.Value;
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
                    _options.PortPoolStart);

                if (generated.Count == 0)
                {
                    //_logger.LogWarning("No valid proxies were generated.");

                    return new();
                }

                //_logger.LogInformation("Validating generated Xray configuration...");

                bool configValid =
                    await _processManager.ValidateConfigAsync(
                        configPath,
                        ct);

                if (!configValid)
                {
                    //_logger.LogError("Generated Xray configuration is invalid. Benchmark aborted.");

                    return new();
                }

                //_logger.LogInformation("Xray configuration is valid.");

                process = _processManager.StartXrayEngine(configPath);

                if (!process.Start())
                {
                    _logger.LogError("Unable to start Xray.");
                    return new();
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // -----------------------------------------------------
                // Wait until the engine actually accepts connections on
                // the first SOCKS inbound instead of a fixed sleep.
                // -----------------------------------------------------

                bool ready =
                    await _processManager.WaitUntilReadyAsync(
                        process,
                        generated[0].Port,
                        TimeSpan.FromSeconds(
                            _options.XrayStartupTimeoutSeconds),
                        ct);

                if (!ready)
                {
                    _logger.LogError(
                        "Xray was not ready in time. Benchmark aborted.");

                    return new();
                }

                // -----------------------------------------------------
                // Phase 1: cheap reachability sweep at high concurrency.
                // -----------------------------------------------------

                Console.WriteLine();
                Console.WriteLine(
                    $"Phase 1/2: reachability check for {generated.Count} proxies...");
                Console.WriteLine();

                var reachable = await RunPhaseAsync<ReachableProxy>(
                    generated,
                    _options.ReachabilityConcurrency,
                    "Reachability",
                    async proxy =>
                    {
                        var test =
                            await _tester.TestReachabilityAsync(
                                proxy.Port,
                                ct);

                        return test.Success
                            ? new ReachableProxy(
                                proxy,
                                test.LatencyMs)
                            : null;
                    },
                    ct);

                if (reachable.Count == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("No reachable proxies found.");

                    return new();
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"Reachable proxies: {reachable.Count}/{generated.Count}");
                Console.WriteLine();

                // -----------------------------------------------------
                // Phase 2: speed test only survivors, low concurrency so
                // downloads don't share bandwidth and skew results.
                // Survivors are tested best-latency first.
                // -----------------------------------------------------

                var orderedSurvivors = reachable
                    .OrderBy(x => x.LatencyMs)
                    .Select(x => x.Proxy)
                    .ToList();

                Console.WriteLine(
                    $"Phase 2/2: speed test for {orderedSurvivors.Count} reachable proxies...");
                Console.WriteLine();

                var speedResults = await RunPhaseAsync<ProxyBenchmarkResult>(
                    orderedSurvivors,
                    _options.SpeedTestConcurrency,
                    "Speed test",
                    async proxy =>
                    {
                        var test =
                            await _tester.TestSpeedAsync(proxy.Port, ct);

                        if (!test.Success)
                            return null;

                        return new ProxyBenchmarkResult(
                            proxy.Config,
                            test.LatencyMs,
                            test.DownloadMbps);
                    },
                    ct);

                var working = speedResults
                    .OfType<ProxyBenchmarkResult>()
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

        /// <summary>
        /// Ping-only benchmark. Configs are chunked into batches so each
        /// one spins up its own short-lived Xray engine - the listening
        /// sockets are released when the process dies and the same port
        /// pool is reused for the next batch. System load stays flat no
        /// matter how many configs come in.
        /// </summary>
        public async Task<List<ProxyBenchmarkResult>> FindReachableProxiesAsync(
            List<V2RayConfig> configs,
            CancellationToken ct)
        {
            if (configs == null || configs.Count == 0)
                return new();

            var uniqueConfigs = configs
                .Where(c => !string.IsNullOrWhiteSpace(c.Url))
                .GroupBy(
                    c => c.Url.ToNormalizedProxyKey(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(_options.MaxConfigs)
                .ToList();

            if (uniqueConfigs.Count == 0)
                return new();

            int batchSize =
                Math.Max(1, _options.BatchSize);

            int totalBatches =
                (int)Math.Ceiling(
                    uniqueConfigs.Count / (double)batchSize);

            var working = new List<ProxyBenchmarkResult>();

            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var batch = uniqueConfigs
                    .Skip(batchIndex * batchSize)
                    .Take(batchSize)
                    .ToList();

                var batchResults =
                    await TestBatchReachabilityAsync(
                        batch,
                        $"Batch {batchIndex + 1}/{totalBatches}",
                        ct);

                working.AddRange(batchResults);
            }

            return working
                .OrderBy(x => x.LatencyMs)
                .ToList();
        }

        /// <summary>
        /// Runs the reachability phase for one batch against a dedicated
        /// Xray engine, then tears it down before returning.
        /// </summary>
        private async Task<List<ProxyBenchmarkResult>> TestBatchReachabilityAsync(
            List<V2RayConfig> batch,
            string label,
            CancellationToken ct)
        {
            string configPath = Path.Combine(
                AppContext.BaseDirectory,
                "base_config.json");

            Process? process = null;

            try
            {
                var generated = _configGenerator.GenerateMasterConfig(
                    batch,
                    configPath,
                    _options.PortPoolStart);

                if (generated.Count == 0)
                {
                    //_logger.LogWarning("No valid proxies were generated for {Label}.",label);

                    return new();
                }

                bool configValid =
                    await _processManager.ValidateConfigAsync(
                        configPath,
                        ct);

                if (!configValid)
                {
                    //_logger.LogError("Generated Xray configuration is invalid. {Label} aborted.",label);

                    return new();
                }

                process = _processManager.StartXrayEngine(configPath);

                if (!process.Start())
                {
                    //_logger.LogError("Unable to start Xray for {Label}.",label);

                    return new();
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool ready =
                    await _processManager.WaitUntilReadyAsync(
                        process,
                        generated[0].Port,
                        TimeSpan.FromSeconds(
                            _options.XrayStartupTimeoutSeconds),
                        ct);

                if (!ready)
                {
                    _logger.LogError(
                        "Xray was not ready in time. {Label} aborted.",
                        label);

                    return new();
                }

                var reachable = await RunPhaseAsync<ReachableProxy>(
                    generated,
                    _options.ReachabilityConcurrency,
                    label,
                    async proxy =>
                    {
                        var test =
                            await _tester.TestReachabilityAsync(
                                proxy.Port,
                                ct);

                        return test.Success
                            ? new ReachableProxy(
                                proxy,
                                test.LatencyMs)
                            : null;
                    },
                    ct);

                return reachable
                    .Select(x => new ProxyBenchmarkResult(
                        x.Proxy.Config,
                        x.LatencyMs,
                        0))
                    .ToList();
            }
            finally
            {
                // The process kill must complete before the next batch
                // rebinds the same ports, so wait for the exit.

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

        /// <summary>
        /// Runs an async worker over every item under a concurrency cap,
        /// with a live progress bar. Returns non-null results only.
        /// </summary>
        private sealed record ReachableProxy(
            GeneratedProxy Proxy,
            long LatencyMs);

        private static async Task<List<T>> RunPhaseAsync<T>(
            List<GeneratedProxy> items,
            int maxConcurrency,
            string label,
            Func<GeneratedProxy, Task<T?>> worker,
            CancellationToken ct)
            where T : class
        {
            int completed = 0;
            int okCount = 0;
            int failedCount = 0;

            var stopwatch =
                Stopwatch.StartNew();

            using var semaphore =
                new SemaphoreSlim(maxConcurrency);

            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync(ct);

                try
                {
                    T? result = await worker(item);

                    Interlocked.Increment(ref completed);

                    if (result is null)
                        Interlocked.Increment(ref failedCount);
                    else
                        Interlocked.Increment(ref okCount);

                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    Interlocked.Increment(ref completed);
                    Interlocked.Increment(ref failedCount);

                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var progressTask = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        int current =
                            Volatile.Read(ref completed);

                        if (current >= items.Count)
                            break;

                        DrawProgress(
                            label,
                            current,
                            items.Count,
                            Volatile.Read(ref okCount),
                            Volatile.Read(ref failedCount),
                            stopwatch);

                        await Task.Delay(250, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, ct);

            var results =
                await Task.WhenAll(tasks);

            stopwatch.Stop();

            try
            {
                await progressTask;
            }
            catch (OperationCanceledException)
            {
            }

            // Final progress line.

            DrawProgress(
                label,
                items.Count,
                items.Count,
                okCount,
                failedCount,
                stopwatch);

            Console.WriteLine();
            Console.WriteLine();

            return results
                .Where(x => x is not null)
                .Select(x => x!)
                .ToList();
        }

        private static void DrawProgress(
            string label,
            int done,
            int total,
            int okCount,
            int failedCount,
            Stopwatch stopwatch)
        {
            double progress =
                total == 0 ? 0 : (double)done / total;

            int filled =
                (int)(progress * BarWidth);

            string bar =
                new string('#', filled) +
                new string('.', BarWidth - filled);

            Console.Write(
                $"\r[{bar}] [{label}] " +
                $"{done}/{total} " +
                $"| Ok: {okCount} " +
                $"| Failed: {failedCount} " +
                $"| {stopwatch.Elapsed:mm\\:ss}   ");
        }
    }
}
