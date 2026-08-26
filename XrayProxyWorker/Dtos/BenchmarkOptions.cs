namespace XrayProxyWorker.Dtos
{
    public class BenchmarkOptions
    {
        public int MaxConfigs { get; set; } = 500;

        public int PortPoolStart { get; set; } = 20000;

        public int ReachabilityConcurrency { get; set; } = 100;

        public int SpeedTestConcurrency { get; set; } = 8;

        public int ConnectTimeoutSeconds { get; set; } = 5;

        public int SpeedTestTimeoutSeconds { get; set; } = 12;

        public long DownloadBytes { get; set; } = 2 * 1024 * 1024;

        public int XrayStartupTimeoutSeconds { get; set; } = 20;
    }
}
