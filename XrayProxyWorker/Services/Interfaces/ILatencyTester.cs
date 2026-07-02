namespace XrayProxyWorker.Services.Interfaces
{
    public interface ILatencyTester
    {
        Task<long> TestPortLatencyAsync(int port, CancellationToken ct);
    }
}
