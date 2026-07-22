using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using StockAggregator.Services;

// Load local secrets from .env into the environment before the host reads
// configuration. Only used for local development — the file is git-ignored and
// absent in Azure, where these come from Function App settings instead.
DotNetEnv.Env.TraversePath().Load();

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Named HttpClient for Yahoo Finance, plus the app's own services. Yahoo rejects
// requests without a browser-like User-Agent, so set one.
builder.Services.AddHttpClient("yahoo", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
});
builder.Services.AddSingleton<QuoteFetcher>();
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton<QuoteRepository>();
builder.Services.AddSingleton<AnalyticsRepository>();
builder.Services.AddSingleton<AnalyticsRollupService>();
builder.Services.AddSingleton<SnapshotRunner>();
builder.Services.AddSingleton<ISnapshotRunner>(sp => sp.GetRequiredService<SnapshotRunner>());

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
