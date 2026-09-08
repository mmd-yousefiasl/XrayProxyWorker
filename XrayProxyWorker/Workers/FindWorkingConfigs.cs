using Microsoft.Extensions.Options;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;
using XrayProxyWorker.Extensions;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Workers
{
    public class ConsoleRunner
    {
        private readonly IV2RayConfigService _configService;
        private readonly IXrayBenchmarkService _benchmarkService;
        private readonly BenchmarkOptions _options;
        private readonly ILogger<ConsoleRunner> _logger;

        public ConsoleRunner(
            IV2RayConfigService configService,
            IXrayBenchmarkService benchmarkService,
            IOptions<BenchmarkOptions> options,
            ILogger<ConsoleRunner> logger)
        {
            _configService = configService;
            _benchmarkService = benchmarkService;
            _options = options.Value;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var sources = _options.Sources
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            Console.WriteLine("========================================");
            Console.WriteLine("       XRAY PROXY CONFIG TESTER");
            Console.WriteLine("========================================");
            Console.WriteLine(
                $"{sources.Count} sources configured.");
            Console.WriteLine();

            // Stdin closed (piped EOF, launched without a console) -
            // ReadKey throws instead of blocking, so run once and exit
            // instead of spinning on prompts.

            bool interactive = true;

            while (true)
            {
                if (interactive)
                {
                    Console.Write(
                        "Press any key to start testing (Esc to quit)... ");

                    try
                    {
                        if (Console.ReadKey(intercept: true).Key ==
                            ConsoleKey.Escape)
                        {
                            break;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        interactive = false;
                    }
                }

                try
                {
                    Console.WriteLine();
                    Console.WriteLine("Downloading configs...");

                    List<V2RayConfig> configs =
                        await LoadConfigsFromSourcesAsync(sources, ct);

                    if (configs.Count == 0)
                    {
                        Console.WriteLine("No configs were found.");
                        Console.WriteLine();

                        if (!interactive)
                            break;

                        continue;
                    }

                    Console.WriteLine(
                        $"Loaded {configs.Count} unique configs.");

                    var workingConfigs =
                        await _benchmarkService.FindReachableProxiesAsync(
                            configs,
                            ct);

                    Console.WriteLine();
                    Console.WriteLine("========================================");
                    Console.WriteLine("           WORKING CONFIGS");
                    Console.WriteLine("========================================");

                    if (workingConfigs.Count == 0)
                    {
                        Console.WriteLine("No working configs found.");
                    }
                    else
                    {
                        foreach (var item in workingConfigs)
                        {
                            Console.WriteLine(BuildOutputUrl(item.Config.Url));
                        }
                    }

                    Console.WriteLine();
                    Console.WriteLine(
                        $"Finished. {workingConfigs.Count} working configs found.");

                    Console.WriteLine();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error while processing subscription.");

                    Console.WriteLine();
                    Console.WriteLine(
                        $"ERROR: {ex.Message}");
                    Console.WriteLine();

                    if (!interactive)
                        break;
                }
            }

            Console.WriteLine("Shutting down...");
        }

        /// <summary>
        /// Fetches every configured source and merges the results into a
        /// deduplicated list - duplicates are removed before any testing
        /// starts, across sources and across differing remarks.
        /// </summary>
        private async Task<List<V2RayConfig>> LoadConfigsFromSourcesAsync(
            List<string> sources,
            CancellationToken ct)
        {
            var allConfigs = new List<V2RayConfig>();

            foreach (string source in sources)
            {
                if (!source.TryGetRawContentUrl(out string rawUrl))
                {
                    Console.WriteLine(
                        $"Skipped invalid source URL: {source}");

                    continue;
                }

                List<V2RayConfig> configs =
                    await _configService.GetConfigsAsync(rawUrl);

                allConfigs.AddRange(configs);
            }

            return allConfigs
                .DistinctBy(c =>
                    c.Url.ToNormalizedProxyKey(),
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildOutputUrl(string url)
        {
            int hashIndex = url.IndexOf('#');

            if (hashIndex >= 0)
            {
                url = url[..hashIndex];
            }

            return url + "#telegram@connect_iran";
        }
    }
}