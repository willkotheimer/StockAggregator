using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

/// <summary>
/// The midday digest email: 11:30 CT on weekdays, after the 11:00 snapshot and its
/// catch-up have had their chance. Fires on Central time via WEBSITE_TIME_ZONE, like
/// the snapshot timers. See <see cref="DailyDigestRunner"/> for what a run does.
/// </summary>
public class DailyDigestFunction
{
    private readonly DailyDigestRunner _runner;
    private readonly ILogger<DailyDigestFunction> _logger;

    public DailyDigestFunction(DailyDigestRunner runner, ILogger<DailyDigestFunction> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [Function("DailyDigest")]
    public async Task Run(
        [TimerTrigger("0 30 11 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(fetchFresh: true, date: null, cancellationToken);
        _logger.LogInformation("Daily digest: {Message}", result.Message);
    }

    /// <summary>
    /// Sends the digest on demand, for testing. By default it resends from prices already
    /// stored (no new snapshot), so it works on a weekend. ?fresh=true runs the full
    /// scheduled path; ?date=yyyy-MM-dd builds the email for a past day.
    /// </summary>
    [Function("DailyDigest_Send")]
    public async Task<HttpResponseData> Send(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "digest/send")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var fresh = bool.TryParse(query["fresh"], out var f) && f;
        DateTime? date = DateTime.TryParse(query["date"], out var d) ? d.Date : null;

        try
        {
            var result = await _runner.RunAsync(fresh, date, cancellationToken);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(result.Message, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual digest send failed.");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync(ex.Message, cancellationToken);
            return response;
        }
    }
}
