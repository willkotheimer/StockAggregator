using Microsoft.Extensions.Options;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services.Digest;

/// <summary>The finished digest: ready-to-send subject and bodies, plus what they were built from.</summary>
public sealed record DigestResponse(
    string? TradingDate,
    bool HasData,
    string Subject,
    string Html,
    string Text,
    DigestContent Content,
    DigestSignals? Signals);

public interface IDigestQueryService
{
    /// <summary>Builds the digest for a day (default: the latest day with snapshots).</summary>
    Task<DigestResponse> GetDigestAsync(DateTime? date, bool includeDataCheck, CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads today's snapshots and the daily history, runs <see cref="DigestAnalysis"/>,
/// and writes and renders the result.
///
/// History is stitched the same way as the trend matrix: official closes from
/// dbo.DailyOhlc wherever it has a bar, the nightly rollup's last price for the recent
/// days the manual backfill hasn't reached. Without the rollup half, every "past
/// month" comparison would quietly run against a month that ended weeks ago.
/// </summary>
public sealed class DigestQueryService : IDigestQueryService
{
    // Enough calendar days for ~2 years of trading days behind "what happened before".
    private const int HistoryCalendarDays = 1100;

    private const string DefaultDashboardUrl = "https://stockaggregator-web.azurewebsites.net/";

    private readonly IDigestReadRepository _digest;
    private readonly IDailyOhlcReadRepository _ohlc;
    private readonly IAnalyticsReadRepository _rollup;
    private readonly IReadOnlyList<EtfGroup> _groups;
    private readonly string _dashboardUrl;

    public DigestQueryService(
        IDigestReadRepository digest,
        IDailyOhlcReadRepository ohlc,
        IAnalyticsReadRepository rollup,
        IOptions<EtfGroupOptions> groupOptions,
        IConfiguration config)
    {
        _digest = digest;
        _ohlc = ohlc;
        _rollup = rollup;
        _groups = groupOptions.Value.Groups;
        _dashboardUrl = config["DashboardUrl"] ?? DefaultDashboardUrl;
    }

    public async Task<DigestResponse> GetDigestAsync(DateTime? date, bool includeDataCheck, CancellationToken cancellationToken = default)
    {
        var tradingDate = (date ?? await _digest.GetLatestSnapshotDateAsync(cancellationToken) ?? DateTime.UtcNow).Date;

        var snapshots = await _digest.GetSnapshotsForDateAsync(tradingDate, cancellationToken);
        var symbols = _groups
            .SelectMany(g => g.Members.Prepend(g.Etf))
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        var from = tradingDate.AddDays(-HistoryCalendarDays);
        var to = tradingDate.AddDays(-1);
        var official = await _ohlc.GetClosesForSymbolsAsync(symbols, from, to, cancellationToken);
        var recent = await _rollup.GetLastPricesForSymbolsAsync(symbols, from, to, cancellationToken);

        var health = await HealthAsync(snapshots, official, recent, tradingDate, cancellationToken);

        // The latest snapshot of the day for each symbol is "now".
        var live = snapshots
            .Where(q => q.Price is > 0m)
            .GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(q => q.CapturedAtUtc).Last())
            .ToDictionary(
                q => q.Symbol,
                q => new LiveQuote(q.Symbol, q.Price!.Value, q.ChangePct, q.DayHigh, q.DayLow, q.PreviousClose, q.CapturedAtUtc),
                StringComparer.OrdinalIgnoreCase);

        if (live.Count == 0)
        {
            var empty = DigestWriter.WriteEmpty(tradingDate, health);
            return Respond(tradingDate, false, empty, null, includeDataCheck);
        }

        var input = new DigestInput(tradingDate, _groups, live, Stitch(official, recent));
        var signals = DigestAnalysis.Compute(input);
        var content = DigestWriter.Write(signals, health);
        return Respond(tradingDate, true, content, signals, includeDataCheck);
    }

    private DigestResponse Respond(DateTime tradingDate, bool hasData, DigestContent content, DigestSignals? signals, bool includeDataCheck) =>
        new(
            tradingDate.ToString("yyyy-MM-dd"),
            hasData,
            content.Subject,
            DigestRenderer.Html(content, _dashboardUrl, includeDataCheck),
            DigestRenderer.Text(content, _dashboardUrl, includeDataCheck),
            content,
            signals);

    /// <summary>Official closes win; the rollup only fills dates the backfill hasn't reached.</summary>
    private static Dictionary<string, IReadOnlyList<DailyClose>> Stitch(
        IReadOnlyList<SymbolClose> official, IReadOnlyList<SymbolClose> recent)
    {
        var bySymbol = new Dictionary<string, SortedDictionary<DateTime, decimal>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in official)
        {
            Series(bySymbol, row.Symbol)[row.TradingDate.Date] = row.Close;
        }
        foreach (var row in recent)
        {
            Series(bySymbol, row.Symbol).TryAdd(row.TradingDate.Date, row.Close);
        }

        return bySymbol.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DailyClose>)kv.Value.Select(p => new DailyClose(p.Key, p.Value)).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static SortedDictionary<DateTime, decimal> Series(
        Dictionary<string, SortedDictionary<DateTime, decimal>> bySymbol, string symbol)
    {
        if (!bySymbol.TryGetValue(symbol, out var series))
        {
            series = new SortedDictionary<DateTime, decimal>();
            bySymbol[symbol] = series;
        }
        return series;
    }

    private async Task<DataHealth> HealthAsync(
        IReadOnlyList<SnapshotQuote> snapshots,
        IReadOnlyList<SymbolClose> official,
        IReadOnlyList<SymbolClose> recent,
        DateTime tradingDate,
        CancellationToken cancellationToken)
    {
        var labels = snapshots
            .GroupBy(q => q.RunLabel)
            .OrderBy(g => g.Min(q => q.CapturedAtUtc))
            .Select(g => g.Key)
            .ToList();

        var rollup = await _digest.GetLastRollupRunAsync(cancellationToken);
        var through = await _digest.GetHistoryThroughAsync(cancellationToken);

        // Trading days the rollup has that the backfill hasn't reached yet.
        int? behind = through is { } t
            ? recent.Select(r => r.TradingDate.Date).Where(d => d > t && d < tradingDate).Distinct().Count()
            : null;

        return new DataHealth(labels, rollup?.StartedAtUtc, rollup?.Status, through, behind);
    }
}
