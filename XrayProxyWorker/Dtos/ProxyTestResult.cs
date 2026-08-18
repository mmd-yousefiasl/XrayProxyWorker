namespace XrayProxyWorker.Dtos
{
    public record ProxyTestResult(
        bool Success,
        long LatencyMs,
        double DownloadMbps)
    {
        public static ProxyTestResult Failed()
        {
            return new ProxyTestResult(
                false,
                long.MaxValue,
                0);
        }
    }
}
