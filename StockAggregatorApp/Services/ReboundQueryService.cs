using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

public interface IReboundQueryService
{
    Task<ReboundResponse> GetReboundAsync(string symbol, ReboundMode mode, decimal thresholdPct, CancellationToken cancellationToken = default);
    IReadOnlyList<EtfGroupDto> GetEtfGroups();
}

/// <summary>
/// Read-time rebound analysis over the backfilled daily history (dbo.DailyOhlc).
/// Cheap enough to run per request — one symbol is ~500–1,250 daily closes and a
/// single linear scan — so nothing is precomputed. Reports historical base rates
/// (median + range + n), never predictions.
/// </summary>
public sealed class ReboundQueryService : IReboundQueryService
{
    // A completed episode counts as "comparable" to the current one if it is at
    // least this many points shy of the current move (similar-or-deeper/higher).
    private const decimal ComparableBandPct = 3m;
    private const int ShortWindowDays = 90;
    private const int LongWindowDays = 180;

    private readonly IDailyOhlcReadRepository _repository;
    private readonly IReadOnlyList<EtfGroup> _groups;

    public ReboundQueryService(IDailyOhlcReadRepository repository, IOptions<EtfGroupOptions> groupOptions)
    {
        _repository = repository;
        _groups = groupOptions.Value.Groups;
    }

    public IReadOnlyList<EtfGroupDto> GetEtfGroups() =>
        _groups.Select(g => new EtfGroupDto(g.Etf, g.Description, g.Members)).ToList();

    public async Task<ReboundResponse> GetReboundAsync(
        string symbol,
        ReboundMode mode,
        decimal thresholdPct,
        CancellationToken cancellationToken = default)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var modeText = mode == ReboundMode.Trough ? "trough" : "surge";

        var bars = await _repository.GetClosesAsync(symbol, cancellationToken);
        if (bars.Count == 0)
        {
            return new ReboundResponse(symbol, modeText, thresholdPct, null, null, null, 0, null, null, Array.Empty<ReboundEpisodeDto>());
        }

        var lastBar = bars[^1];
        var scan = ReboundAnalysis.Scan(bars, thresholdPct, mode);

        var current = BuildCurrent(scan.Current, lastBar, mode);
        var baseRate = BuildBaseRate(scan.Completed, current?.MaxMovePct ?? thresholdPct);

        // Newest episodes first — most relevant at the top of the table.
        var episodes = scan.Completed
            .OrderByDescending(e => e.AnchorDate)
            .Select(e => new ReboundEpisodeDto(
                Fmt(e.AnchorDate), e.AnchorPrice, Fmt(e.ExtremeDate), e.ExtremePrice,
                e.MovePct, e.AnchorToExtremeDays, e.ExtremeToReversalDays, e.AnchorToReversalDays))
            .ToList();

        return new ReboundResponse(
            Symbol: symbol,
            Mode: modeText,
            ThresholdPct: thresholdPct,
            HistoryStart: Fmt(bars[0].TradingDate),
            AsOfDate: Fmt(lastBar.TradingDate),
            LastClose: lastBar.Close,
            BarCount: bars.Count,
            Current: current,
            BaseRate: baseRate,
            Episodes: episodes);
    }

    private static ReboundCurrentDto? BuildCurrent(ReboundAnalysis.Episode? e, DailyClose lastBar, ReboundMode mode)
    {
        if (e is null)
        {
            return null;
        }

        var trough = mode == ReboundMode.Trough;
        var currentMove = e.AnchorPrice == 0m
            ? 0m
            : Math.Round((trough ? e.AnchorPrice - lastBar.Close : lastBar.Close - e.AnchorPrice) / e.AnchorPrice * 100m, 2);

        return new ReboundCurrentDto(
            AnchorDate: Fmt(e.AnchorDate),
            AnchorPrice: e.AnchorPrice,
            ExtremeDate: Fmt(e.ExtremeDate),
            ExtremePrice: e.ExtremePrice,
            MaxMovePct: e.MovePct,
            CurrentMovePct: currentMove,
            DaysSinceAnchor: (lastBar.TradingDate - e.AnchorDate).Days,
            DaysSinceExtreme: (lastBar.TradingDate - e.ExtremeDate).Days);
    }

    private static ReboundBaseRateDto? BuildBaseRate(IReadOnlyList<ReboundAnalysis.Episode> completed, decimal referenceMovePct)
    {
        var floor = Math.Max(0m, referenceMovePct - ComparableBandPct);

        var reversalDays = completed
            .Where(e => e.Reversed && e.MovePct >= floor && e.ExtremeToReversalDays.HasValue)
            .Select(e => e.ExtremeToReversalDays!.Value)
            .OrderBy(d => d)
            .ToList();

        if (reversalDays.Count == 0)
        {
            return null;
        }

        return new ReboundBaseRateDto(
            ComparableMovePct: Math.Round(floor, 1),
            EpisodeCount: reversalDays.Count,
            MedianReversalDays: Median(reversalDays),
            MinReversalDays: reversalDays[0],
            MaxReversalDays: reversalDays[^1],
            ShortWindowDays: ShortWindowDays,
            ReversedWithinShort: reversalDays.Count(d => d <= ShortWindowDays),
            LongWindowDays: LongWindowDays,
            ReversedWithinLong: reversalDays.Count(d => d <= LongWindowDays));
    }

    // Assumes the input is already sorted ascending.
    private static int Median(IReadOnlyList<int> sorted)
    {
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (int)Math.Round((sorted[mid - 1] + sorted[mid]) / 2.0, MidpointRounding.AwayFromZero);
    }

    private static string Fmt(DateTime d) => d.ToString("yyyy-MM-dd");
}
