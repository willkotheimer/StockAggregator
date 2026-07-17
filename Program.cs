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

// Named HttpClient for Financial Modeling Prep, plus the app's own services.
builder.Services.AddHttpClient("fmp", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<QuoteFetcher>();
builder.Services.AddSingleton<QuoteRepository>();
builder.Services.AddSingleton<SnapshotRunner>();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
