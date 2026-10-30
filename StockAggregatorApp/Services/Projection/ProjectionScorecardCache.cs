using System.Diagnostics;
using Microsoft.Extensions.Options;
using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services.Digest;

namespace StockAggregatorApp.Services.Projection;

/// <summary>
/// Runs the projection test over the full price history and keeps the result until
/// the nightly rollup adds a new trading day, so the email, the endpoint and any
/// retries share one compute per day. A backfill alone doesn't refresh it; the next
/// rollup day does.
/// </summary>
public sealed class ProjectionScorecardCache
{
    private static readonly DateTime HistoryStart = new(2000, 1, 1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<EtfGroup> _groups;
    private readonly IReadOnlyList<string> _destinations;
    private readonly ILogger<ProjectionScorecardCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTime Key, ProjectionScorecard Scorecard)? _entry;

    public ProjectionScorecardCache(
        IServiceScopeFactory scopeFactory,
        IOptions<EtfGroupOptions> groupOptions,
        IOptions<RotationDestinationOptions> destinationOptions,
        ILogger<ProjectionScorecardCache> logger)
    {
        _scopeFactory = scopeFactory;
        _groups = groupOptions.Value.Groups;
        _destinations = destinationOptions.Value.Symbols;
        _logger = logger;
    }

    public async Task<ProjectionScorecard> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var ohlc = scope.ServiceProvider.GetRequiredService<IDailyOhlcReadRepository>();
        var rollup = scope.ServiceProvider.GetRequiredService<IAnalyticsReadRepository>();
        var projections = scope.ServiceProvider.GetRequiredService<IProjectionReadRepository>();

        // The newest day either price source knows about decides whether the cache is stale.
        var key = await rollup.GetLatestTradingDateAsync(cancellationToken) ?? DateTime.MinValue;
        if (_entry is { } hit && hit.Key == key)
        {
            return hit.Scorecard;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_entry is { } raced && raced.Key == key)
            {
                return raced.Scorecard;
            }

            var timer = Stopwatch.StartNew();
            var symbols = _groups
                .SelectMany(g => g.Members.Prepend(g.Etf))
                .Concat(_destinations)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();

            var today = DateTime.UtcNow.Date;
            var official = await ohlc.GetClosesForSymbolsAsync(symbols, HistoryStart, today, cancellationToken);
            var recent = await rollup.GetLastPricesForSymbolsAsync(symbols, HistoryStart, today, cancellationToken);
            var history = DigestQueryService.Stitch(official, recent);

            var reported = await projections.GetEarningsDatesAsync(cancellationToken);
            var earliest = history.Values.Where(s => s.Count > 0).Select(s => s[0].TradingDate).DefaultIfEmpty(today).Min();
            var earnings = reported.ToDictionary(
                kv => kv.Key,
                kv => EarningsCalendar.WithEstimates(kv.Value, earliest),
                StringComparer.OrdinalIgnoreCase);

            var scorecard = ProjectionTest.Run(new ProjectionInput(_groups, history, earnings, _destinations));
            _entry = (key, scorecard);

            _logger.LogInformation(
                "Projection scorecard computed in {Seconds:n1}s: {Patterns} patterns listed, {FollowOnGuesses} follow-on guesses, as of {AsOf:yyyy-MM-dd}.",
                timer.Elapsed.TotalSeconds, scorecard.Patterns.Count, scorecard.FollowOn?.All.Tests ?? 0, scorecard.AsOf);
            return scorecard;
        }
        finally
        {
            _gate.Release();
        }
    }
}
