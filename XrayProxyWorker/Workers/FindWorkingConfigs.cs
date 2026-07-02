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
        private static readonly TimeSpan ExecutionInterval = TimeSpan.FromMinutes(30);
        #endregion

        #region Ctor
        public FindWorkingConfigs(IV2RayConfigService configService,
            IXrayBenchmarkService benchmarkService,
            ILogger<FindWorkingConfigs> logger)
        {
            _configService = configService;
            _benchmarkService = benchmarkService;
            _logger = logger;
        }
        #endregion

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Xray Proxy Worker Service initiated. Running benchmark every 30 minutes.");

            // Define a strict 30-minute ticking interval
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));

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

                // 3. Extract the Top 10 configurations sorted by fastest response speed
                var topTenProxies = benchmarkResults
                    .Take(10)
                    .ToList();

                // 4. Output the results
                LogTopTenResults(topTenProxies);

                // TODO: Here is where you can easily hook up your Telegram bot to broadcast these top 10 nodes!
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

        private void LogTopTenResults(List<(V2RayConfig Config, long Latency)> topTen)
        {
            if (!topTen.Any())
            {
                _logger.LogWarning("--- TOP 10 FASTEST PROXIES --- \nNo working proxies passed the network latency evaluations.");
                return;
            }

            _logger.LogInformation("--- TOP 10 FASTEST PROXIES SELECTION ---");
            for (int i = 0; i < topTen.Count; i++)
            {
                var item = topTen[i];
                _logger.LogInformation("[RANK {Rank}] Port: {Port} | Protocol: {Protocol} | Latency: {Latency}ms | Target: {Address}",
                    i + 1,
                    item.Config.Port,
                    item.Config.Protocol ?? "vless",
                    item.Latency,
                    item.Config.Address);
            }
        }
    }
}
