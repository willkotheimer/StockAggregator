using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

/// <summary>
/// Keeps dbo.EarningsDates current for the projection test, which marks guesses whose
/// 20-day window crossed an earnings report. Weekly is plenty: companies report four
/// times a year, and Nasdaq's data shows each report the day it happens.
/// </summary>
public class EarningsRefreshFunction
{
    private readonly EarningsDateFetcher _fetcher;
    private readonly EarningsRepository _repository;
    private readonly ILogger<EarningsRefreshFunction> _logger;

    public EarningsRefreshFunction(EarningsDateFetcher fetcher, EarningsRepository repository, ILogger<EarningsRefreshFunction> logger)
    {
        _fetcher = fetcher;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Saturdays at 07:00 CT, when nothing else is running.</summary>
    [Function("EarningsRefresh")]
    public async Task Run(
        [TimerTrigger("0 0 7 * * 6")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var added = await RefreshAsync(cancellationToken);
        _logger.LogInformation("Earnings refresh added {Added} dates.", added);
    }

    /// <summary>On demand: POST /api/earnings/refresh.</summary>
    [Function("EarningsRefresh_Run")]
    public async Task<HttpResponseData> RunNow(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "earnings/refresh")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var added = await RefreshAsync(cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync($"Added {added} new earnings dates.", cancellationToken);
        return response;
    }

    private async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        var reports = await _fetcher.FetchAsync(cancellationToken);
        return await _repository.AddNewAsync(reports, cancellationToken);
    }
}
