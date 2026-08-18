using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;
using XrayProxyWorker.Tools;

namespace XrayProxyWorker.Workers
{
    public class ConsoleRunner
    {
        private readonly IV2RayConfigService _configService;
        private readonly IXrayBenchmarkService _benchmarkService;
        private readonly ILogger<ConsoleRunner> _logger;

        public ConsoleRunner(
            IV2RayConfigService configService,
            IXrayBenchmarkService benchmarkService,
            ILogger<ConsoleRunner> logger)
        {
            _configService = configService;
            _benchmarkService = benchmarkService;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("       XRAY PROXY CONFIG TESTER");
            Console.WriteLine("========================================");
            Console.WriteLine();

            while (!ct.IsCancellationRequested)
            {
                Console.Write("Enter GitHub config URL (or 'exit'): ");

                string? input = Console.ReadLine()?.Trim();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!input.TryGetRawContentUrl(out string rawUrl))
                {
                    Console.WriteLine();
                    Console.WriteLine("Invalid GitHub URL.");
                    Console.WriteLine(
                        "Please enter a GitHub file URL such as:");
                    Console.WriteLine(
                        "https://github.com/user/repository/blob/main/Sub1.txt");
                    Console.WriteLine();
                    continue;
                }

                try
                {
                    Console.WriteLine();
                    Console.WriteLine($"Raw URL:");
                    Console.WriteLine(rawUrl);
                    Console.WriteLine();

                    Console.WriteLine("Downloading configs...");

                    List<V2RayConfig> configs =
                        await _configService.GetConfigsAsync(rawUrl);

                    if (configs == null || configs.Count == 0)
                    {
                        Console.WriteLine("No configs were found.");
                        Console.WriteLine();
                        continue;
                    }

                    Console.WriteLine(
                        $"Downloaded {configs.Count} configs.");

                    Console.WriteLine();
                    Console.WriteLine("Testing configs...");
                    Console.WriteLine();

                    var results =
                        await _benchmarkService.FindAllFastProxiesAsync(
                            configs,
                            ct);

                    var workingConfigs = results
                        .OrderBy(x => x.Latency)
                        .ToList();

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
                        for (int i = 0; i < workingConfigs.Count; i++)
                        {
                            var item = workingConfigs[i];
                            Console.WriteLine(item.Config.Url);
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
                }
            }

            Console.WriteLine("Shutting down...");
        }
    }
}