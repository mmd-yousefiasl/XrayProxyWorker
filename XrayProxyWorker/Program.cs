using XrayProxyWorker.Services.Implementations;
using XrayProxyWorker.Services.Interfaces;
using XrayProxyWorker.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<IV2RayConfigService, V2RayConfigService>();

builder.Services.AddSingleton<IProxyConfigValidator, ProxyConfigValidator>();
builder.Services.AddSingleton<IXrayConfigGenerator, XrayConfigGenerator>();
builder.Services.AddSingleton<IProcessManager, XrayProcessManager>();
builder.Services.AddSingleton<ILatencyTester, SocketsLatencyTester>();

builder.Services.AddHttpClient<ITelegramPublisher, TelegramPublisher>();

builder.Services.AddSingleton<IXrayBenchmarkService, XrayBenchmarkService>();

// Interactive console runner
builder.Services.AddSingleton<ConsoleRunner>();

var host = builder.Build();

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var runner = host.Services.GetRequiredService<ConsoleRunner>();

await runner.RunAsync(cts.Token);