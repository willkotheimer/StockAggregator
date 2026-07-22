using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockAggregator.Functions;

/// <summary>
/// Keeps the dashboard web app warm so portfolio visitors don't hit a cold start.
/// Azure unloads an idle App Service after ~20 minutes with no requests, so this
/// pings every 10 minutes — two hits per idle window, leaving margin for a missed
/// ping. The ping only warms the app process; it does no database work.
/// </summary>
public class KeepWarmFunction
{
    private const string DefaultUrl = "https://stockaggregator-web.azurewebsites.net/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<KeepWarmFunction> _logger;

    public KeepWarmFunction(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<KeepWarmFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    [Function("KeepWarm")]
    public async Task Run(
        [TimerTrigger("0 */10 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var url = _config["KeepWarmUrl"] ?? DefaultUrl;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60); // tolerate a cold start if it had already slept
            using var response = await client.GetAsync(url, cancellationToken);
            _logger.LogInformation("Keep-warm ping to {Url} returned {StatusCode}.", url, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keep-warm ping to {Url} failed.", url);
        }
    }
}
