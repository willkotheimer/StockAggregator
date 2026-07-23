using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

public interface IRangeQueryService
{
    Task<RangeResponse?> GetRangesAsync(string etf, decimal pullbackPct, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-time range / volatility profile for an ETF and its members, computed from
/// the backfilled daily history (dbo.DailyOhlc). One request reads the ETF plus
/// its ~7 members (a handful of small daily series) and runs a linear pass each —
/// cheap enough that nothing is precomputed.
/// </summary>
public sealed class RangeQueryService : IRangeQueryService
{
    private readonly IDailyOhlcReadRepository _repository;
    private readonly IReadOnlyList<EtfGroup> _groups;

    public RangeQueryService(IDailyOhlcReadRepository repository, IOptions<EtfGroupOptions> groupOptions)
    {
        _repository = repository;
        _groups = groupOptions.Value.Groups;
    }

    public async Task<RangeResponse?> GetRangesAsync(string etf, decimal pullbackPct, CancellationToken cancellationToken = default)
    {
        var group = _groups.FirstOrDefault(g => string.Equals(g.Etf, etf, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return null;
        }

        var rows = new List<RangeRow>();

        // The ETF itself first (a benchmark), then each member.
        rows.Add(await BuildRowAsync(group.Etf, isEtf: true, pullbackPct, cancellationToken));
        foreach (var member in group.Members)
        {
            rows.Add(await BuildRowAsync(member, isEtf: false, pullbackPct, cancellationToken));
        }

        // Members ranked by how much they typically move; the ETF pinned on top.
        var ranked = rows
            .OrderByDescending(r => r.IsEtf)
            .ThenByDescending(r => r.MedianDailyRangePct ?? decimal.MinValue)
            .ToList();

        return new RangeResponse(group.Etf, group.Description, pullbackPct, ranked);
    }

    private async Task<RangeRow> BuildRowAsync(string symbol, bool isEtf, decimal pullbackPct, CancellationToken cancellationToken)
    {
        var bars = await _repository.GetBarsAsync(symbol, cancellationToken);
        var s = RangeAnalysis.Compute(bars, pullbackPct);

        return new RangeRow(
            Symbol: symbol,
            IsEtf: isEtf,
            BarCount: s.BarCount,
            HistoryStart: s.HistoryStart?.ToString("yyyy-MM-dd"),
            MedianDailyRangePct: s.MedianDailyRangePct,
            MedianWeeklyRangePct: s.MedianWeeklyRangePct,
            UpDayPct: s.UpDayPct,
            MedianUpDayPct: s.MedianUpDayPct,
            MedianDownDayPct: s.MedianDownDayPct,
            TypicalGainBeforePullbackPct: s.TypicalGainBeforePullbackPct,
            PullbackEpisodeCount: s.PullbackEpisodeCount);
    }
}
