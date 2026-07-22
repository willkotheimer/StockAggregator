using Microsoft.Extensions.Logging;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Computes the daily per-symbol rollup from raw snapshots and persists it.
/// Idempotent — recomputing a date overwrites its rows.
/// </summary>
public sealed class AnalyticsRollupService
{
    private readonly AnalyticsRepository _repository;
    private readonly ILogger<AnalyticsRollupService> _logger;

    public AnalyticsRollupService(AnalyticsRepository repository, ILogger<AnalyticsRollupService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<int> RunDailyRollupAsync(int lookbackDays, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            var since = DateTime.UtcNow.Date.AddDays(-Math.Max(1, lookbackDays));
            var raw = await _repository.GetQuotesSinceAsync(since, cancellationToken);
            var stats = Compute(raw);

            await _repository.ReplaceDailyStatsAsync(stats, cancellationToken);
            await _repository.LogRunAsync("DailyRollup", startedAt, DateTime.UtcNow, stats.Count, "Success", null, cancellationToken);

            _logger.LogInformation("Daily rollup wrote {Count} rows across {Days} lookback days.", stats.Count, lookbackDays);
            return stats.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily rollup failed.");
            try
            {
                await _repository.LogRunAsync("DailyRollup", startedAt, DateTime.UtcNow, 0, "Failed", ex.Message, cancellationToken);
            }
            catch
            {
                // best-effort logging
            }
            throw;
        }
    }

    // All four capture times fall on the same UTC calendar day, so UTC date == trading date.
    private static List<DailySymbolStat> Compute(IReadOnlyList<AnalyticsRepository.RawQuote> raw)
    {
        return raw
            .GroupBy(q => (q.Symbol, Date: q.CapturedAtUtc.Date))
            .Select(g =>
            {
                var ordered = g.OrderBy(q => q.CapturedAtUtc).ToList();
                var withPrice = ordered.Where(q => q.Price.HasValue).Select(q => q.Price!.Value).ToList();

                decimal? firstPrice = withPrice.Count > 0 ? withPrice[0] : null;
                decimal? lastPrice = withPrice.Count > 0 ? withPrice[^1] : null;

                var highs = ordered.Where(q => q.DayHigh.HasValue).Select(q => q.DayHigh!.Value).ToList();
                var lows = ordered.Where(q => q.DayLow.HasValue).Select(q => q.DayLow!.Value).ToList();
                decimal? dayHigh = highs.Count > 0 ? highs.Max() : (withPrice.Count > 0 ? withPrice.Max() : null);
                decimal? dayLow = lows.Count > 0 ? lows.Min() : (withPrice.Count > 0 ? withPrice.Min() : null);

                // Previous close: prefer captured; else derive from the last change%.
                decimal? previousClose = ordered.LastOrDefault(q => q.PreviousClose.HasValue)?.PreviousClose;
                var lastChange = ordered.LastOrDefault(q => q.ChangePct.HasValue)?.ChangePct;
                if (previousClose is null && lastPrice is { } lp && lastChange is { } lc && lc != -100m)
                {
                    previousClose = lp / (1 + lc / 100m);
                }

                decimal? changePct = lastChange
                    ?? (lastPrice is { } l && previousClose is { } pc && pc != 0m ? (l - pc) / pc * 100m : null);

                decimal? intradayRangePct = dayHigh is { } h && dayLow is { } lo && previousClose is { } p && p != 0m
                    ? Math.Round((h - lo) / p * 100m, 4)
                    : null;

                return new DailySymbolStat(
                    Symbol: g.Key.Symbol,
                    TradingDate: g.Key.Date,
                    FirstPrice: firstPrice,
                    LastPrice: lastPrice,
                    DayHigh: dayHigh,
                    DayLow: dayLow,
                    PreviousClose: previousClose,
                    ChangePct: changePct.HasValue ? Math.Round(changePct.Value, 4) : null,
                    IntradayRangePct: intradayRangePct,
                    MaxSnapshotDrawdownPct: MaxDrawdownPct(withPrice),
                    SnapshotCount: ordered.Count);
            })
            .ToList();
    }

    /// <summary>Largest peak-to-trough drop across the day's snapshot prices, as a positive %.</summary>
    private static decimal? MaxDrawdownPct(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 2)
        {
            return 0m;
        }

        decimal peak = prices[0];
        decimal worst = 0m;
        foreach (var price in prices)
        {
            if (price > peak)
            {
                peak = price;
            }
            else if (peak != 0m)
            {
                var drop = (peak - price) / peak * 100m;
                if (drop > worst)
                {
                    worst = drop;
                }
            }
        }

        return Math.Round(worst, 4);
    }
}
