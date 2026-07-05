using XrayProxyWorker.Services.Implementations;
using XrayProxyWorker.Services.Interfaces;
using XrayProxyWorker.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<IV2RayConfigService, V2RayConfigService>();

// Register individual SOLID services
builder.Services.AddSingleton<IProxyConfigValidator, ProxyConfigValidator>();
builder.Services.AddSingleton<IXrayConfigGenerator, XrayConfigGenerator>();
builder.Services.AddSingleton<IProcessManager, XrayProcessManager>();
builder.Services.AddSingleton<ILatencyTester, SocketsLatencyTester>();
builder.Services.AddHttpClient<ITelegramPublisher, TelegramPublisher>();

// Main orchestration facade service entry point registration mapping
builder.Services.AddSingleton<IXrayBenchmarkService, XrayBenchmarkService>();

builder.Services.AddHostedService<FindWorkingConfigs>();


var host = builder.Build();
host.Run();
