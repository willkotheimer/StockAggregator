using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

public interface IRangeMatrixQueryService
{
    Task<RangeMatrixResponse> GetMatrixAsync(
        DateTime from, DateTime to, int? dayOfWeek, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetTradingDatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Symbol x date grid of daily closing prices over a date range, stitched from two
/// sources so it is both deep and current:
///
///   dbo.DailyOhlc         — official closes, years of history, but only moves when
///                           the manual backfill is run, so its recent end goes stale.
///   dbo.DailySymbolStats  — the nightly rollup's LastPrice, self-maintaining but only
///                           as old as intraday capture.
///
/// DailyOhlc wins wherever it has a bar, since a true close beats a near-close; the
/// rollup fills only the dates it has not reached. The seam therefore heals on its
/// own whenever a backfill runs, and no date is ever sourced from both.
/// </summary>
public sealed class RangeMatrixQueryService : IRangeMatrixQueryService
{
    /// <summary>Ceiling on columns, so a multi-year range cannot produce an unusable table.</summary>
    public const int MaxDates = 180;

    /// <summary>A net move smaller than this is never called a trend, however tidy the path.</summary>
    private const decimal TrendThresholdPct = 2m;

    /// <summary>
    /// A net move must also cover at least this fraction of the low-to-high band.
    /// Without it, a symbol that oscillates 8% and happens to close near the top of
    /// its channel reads as a 6% uptrend when it is really mean-reverting.
    /// </summary>
    private const decimal TrendBandShare = 0.5m;

    private const string OtherGroup = "Other";

    private readonly IDailyOhlcReadRepository _repository;
    private readonly IAnalyticsReadRepository _rollup;
    private readonly IReadOnlyList<EtfGroup> _groups;

    public RangeMatrixQueryService(
        IDailyOhlcReadRepository repository,
        IAnalyticsReadRepository rollup,
        IOptions<EtfGroupOptions> groupOptions)
    {
        _repository = repository;
        _rollup = rollup;
        _groups = groupOptions.Value.Groups;
    }

    public async Task<IReadOnlyList<string>> GetTradingDatesAsync(CancellationToken cancellationToken = default)
    {
        // Union of both sources, so the picker offers recent days the backfill has
        // not reached as well as the deep history only the backfill has.
        var history = await _repository.GetTradingDatesAsync(cancellationToken);
        var recent = await _rollup.GetRollupDatesAsync(cancellationToken);

        return history
            .Concat(recent)
            .Select(d => d.Date)
            .Distinct()
            .OrderBy(d => d)
            .Select(d => d.ToString("yyyy-MM-dd"))
            .ToList();
    }

    public async Task<RangeMatrixResponse> GetMatrixAsync(
        DateTime from, DateTime to, int? dayOfWeek, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Every symbol under any ETF, plus the ETFs themselves. A symbol can sit in
        // two groups (TSM is in both SMH and VEU), so query it once and fan out later.
        var symbols = _groups
            .SelectMany(g => g.Members.Prepend(g.Etf))
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        var official = await _repository.GetClosesForSymbolsAsync(symbols, from, to, cancellationToken);
        var rollup = await _rollup.GetLastPricesForSymbolsAsync(symbols, from, to, cancellationToken);

        var bySymbol = official
            .GroupBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(c => c.TradingDate.Date, c => c.Close),
                StringComparer.OrdinalIgnoreCase);

        // Rollup prices fill gaps only — an official close already present for that
        // symbol and date always wins.
        foreach (var row in rollup)
        {
            if (!bySymbol.TryGetValue(row.Symbol, out var byDate))
            {
                byDate = new Dictionary<DateTime, decimal>();
                bySymbol[row.Symbol] = byDate;
            }

            if (!byDate.ContainsKey(row.TradingDate.Date))
            {
                byDate[row.TradingDate.Date] = row.Close;
            }
        }

        // Column dates: trading days actually present in either source, optionally kept
        // to one weekday. Trimmed from the *start* so the most recent dates survive a cap.
        var dates = official
            .Select(c => c.TradingDate.Date)
            .Concat(rollup.Select(c => c.TradingDate.Date))
            .Distinct()
            .Where(d => dayOfWeek is null || (int)d.DayOfWeek == dayOfWeek.Value)
            .OrderBy(d => d)
            .ToList();

        if (dates.Count > MaxDates)
        {
            dates = dates.Skip(dates.Count - MaxDates).ToList();
        }

        var rows = new List<RangeMatrixRow>();
        foreach (var group in _groups)
        {
            rows.Add(BuildRow(group.Etf, isEtf: true, group.Etf, group.Description, dates, bySymbol));
            foreach (var member in group.Members)
            {
                rows.Add(BuildRow(member, isEtf: false, group.Etf, null, dates, bySymbol));
            }
        }

        // Anything with history but no configured group still deserves a row.
        var grouped = rows.Select(r => r.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in bySymbol.Keys.Where(s => !grouped.Contains(s)).OrderBy(s => s, StringComparer.Ordinal))
        {
            rows.Add(BuildRow(symbol, isEtf: false, OtherGroup, null, dates, bySymbol));
        }

        // Flag where official closes run out, so the client can label the tail rather
        // than quietly presenting 14:30 CT prices as closing prints.
        var officialDates = official.Select(c => c.TradingDate.Date).ToHashSet();
        var provisionalFrom = dates.Where(d => !officialDates.Contains(d)).OrderBy(d => d).FirstOrDefault();

        return new RangeMatrixResponse(
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            dayOfWeek,
            dates.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
            provisionalFrom == default ? null : provisionalFrom.ToString("yyyy-MM-dd"),
            rows);
    }

    private static RangeMatrixRow BuildRow(
        string symbol,
        bool isEtf,
        string groupEtf,
        string? description,
        IReadOnlyList<DateTime> dates,
        IReadOnlyDictionary<string, Dictionary<DateTime, decimal>> bySymbol)
    {
        bySymbol.TryGetValue(symbol, out var byDate);

        var closes = new List<decimal?>(dates.Count);
        var changes = new List<decimal?>(dates.Count);
        decimal? previous = null;

        foreach (var date in dates)
        {
            decimal? close = byDate is not null && byDate.TryGetValue(date, out var value) ? value : null;
            closes.Add(close);

            // Versus the previous *populated* column, not the previous calendar day:
            // a Fridays-only selection therefore shows week-over-week moves.
            changes.Add(close is not null && previous is > 0
                ? (close.Value / previous.Value - 1m) * 100m
                : null);

            if (close is not null)
            {
                previous = close;
            }
        }

        // Id must be unique per row: a symbol under two ETFs yields two rows.
        var id = isEtf ? symbol : $"{groupEtf}:{symbol}";
        return new RangeMatrixRow(id, symbol, isEtf, groupEtf, description, closes, changes, Summarise(closes));
    }

    /// <summary>
    /// Classifies the row's path over the selected columns. See <see cref="TrendSummary"/>
    /// for what each number means.
    /// </summary>
    public static TrendSummary Summarise(IReadOnlyList<decimal?> closes)
    {
        var present = closes.Where(c => c is > 0).Select(c => c!.Value).ToList();
        if (present.Count < 2)
        {
            return new TrendSummary(TrendDirection.Unknown, null, null, null);
        }

        var first = present[0];
        var last = present[^1];
        var low = present.Min();
        var high = present.Max();

        var netPct = (last / first - 1m) * 100m;
        var bandPct = low > 0 ? (high - low) / low * 100m : 0m;
        var positionInBand = high > low ? (last - low) / (high - low) * 100m : 50m;

        var decisive = Math.Abs(netPct) >= TrendThresholdPct
            && Math.Abs(netPct) >= bandPct * TrendBandShare;

        var direction = !decisive
            ? TrendDirection.Channeling
            : netPct > 0 ? TrendDirection.Up : TrendDirection.Down;

        return new TrendSummary(
            direction,
            Math.Round(netPct, 2),
            Math.Round(bandPct, 2),
            Math.Round(positionInBand, 1));
    }
}
