using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Workers
{
    public class FindWorkingConfigs : BackgroundService
    {
        #region Fields
        private readonly IV2RayConfigService _configService;
        private readonly ILogger<FindWorkingConfigs> _logger;
        private readonly IXrayBenchmarkService _benchmarkService;
        private static readonly int ExecutionInterval = 30;
        private readonly ITelegramPublisher _telegramPublisher;
        #endregion

        #region Ctor
        public FindWorkingConfigs(IV2RayConfigService configService,
            IXrayBenchmarkService benchmarkService,
            ILogger<FindWorkingConfigs> logger,
            ITelegramPublisher telegramPublisher)
        {
            _configService = configService;
            _benchmarkService = benchmarkService;
            _logger = logger;
            _telegramPublisher = telegramPublisher;
        }
        #endregion

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Xray Proxy Worker Service initiated. Running benchmark every 30 minutes.");

            // Define a strict 30-minute ticking interval
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(ExecutionInterval));

            // 1. Run immediately on application startup
            await ExecuteBenchmarkCycleAsync(stoppingToken);

            // 2. Wait exactly 30 minutes between each subsequent execution
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExecuteBenchmarkCycleAsync(stoppingToken);
            }
        }

        private async Task ExecuteBenchmarkCycleAsync(CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("Starting scheduled 30-minute proxy processing loop at: {Time}", DateTimeOffset.Now);

                // 1. Fetch your raw configurations list
                List<V2RayConfig> rawConfigs = await _configService.GetConfigsAsync();

                if (rawConfigs == null || rawConfigs.Count == 0)
                {
                    _logger.LogWarning("No raw configurations retrieved. Skipping benchmark run.");
                    return;
                }

                // 2. Pass them to your SOLID orchestrated benchmark engine
                List<(V2RayConfig Config, long Latency)> benchmarkResults =
                    await _benchmarkService.FindAllFastProxiesAsync(rawConfigs, ct);

                var sortedResults = benchmarkResults
                    .OrderBy(x => x.Latency)
                    .ToList();

                var proxyChunks = sortedResults
                    .Chunk(10)
                    .Take(2)
                    .ToList();

                foreach (var chunk in proxyChunks)
                {
                    var topTenBatch = chunk.ToList();

                    await _telegramPublisher.BroadcastTopProxiesAsync(topTenBatch, ct);
                    await Task.Delay(5000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("The proxy scanning sequence execution was cancelled via system shutdown request.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled critical fault occurred during the background benchmark execution cycle.");
            }
        }
    }
}
