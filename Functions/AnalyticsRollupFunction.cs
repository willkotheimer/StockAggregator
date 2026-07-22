using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

/// <summary>
/// Recomputes the daily analytics rollup. Runs nightly after the close, and can
/// be triggered on demand (e.g. to backfill after a schema/formula change).
/// </summary>
public class AnalyticsRollupFunction
{
    private readonly AnalyticsRollupService _rollup;
    private readonly ILogger<AnalyticsRollupFunction> _logger;

    public AnalyticsRollupFunction(AnalyticsRollupService rollup, ILogger<AnalyticsRollupFunction> logger)
    {
        _rollup = rollup;
        _logger = logger;
    }

    // 22:30 UTC on weekdays ≈ after the US market close.
    [Function("AnalyticsRollup_Nightly")]
    public async Task Nightly(
        [TimerTrigger("0 30 22 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        await _rollup.RunDailyRollupAsync(lookbackDays: 5, cancellationToken);
    }

    // On-demand recompute/backfill over a wider window.
    [Function("AnalyticsRollup_Run")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/rollup")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var rows = await _rollup.RunDailyRollupAsync(lookbackDays: 30, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync($"Rollup complete: {rows} daily rows.");
        return response;
    }
}
