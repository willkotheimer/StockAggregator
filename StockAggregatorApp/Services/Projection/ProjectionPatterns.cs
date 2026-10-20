using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services.Digest;

namespace StockAggregatorApp.Services.Projection;

/// <summary>One pattern the projection test can condition on, with its plain-English name.</summary>
public sealed record PatternDefinition(string Id, string Label);

/// <summary>
/// The daily-history versions of the dashboard and digest signals: each one answers
/// "was this true at the close on day i?" using only closes up to day i, so the
/// projection test can never peek past its origin date. The midday-only signals
/// (turnarounds, a fund rising on a few stocks) need intraday history we only have
/// since July 2026, so they aren't here.
/// </summary>
public static class ProjectionPatterns
{
    public const string MonthHigh = "month-high";
    public const string MonthLow = "month-low";
    public const string RangeBottom = "range-bottom";
    public const string RangeTop = "range-top";
    public const string FarBelowHigh = "far-below-high";
    public const string BigMove = "big-move";
    public const string SteadyClimber = "steady-climber";
    public const string StrongGroup = "strong-group";

    // Same windows as the live signals they mirror.
    private const int RangeDays = DigestAnalysis.MonthDays;
    private const int FarWindowDays = DigestAnalysis.FarFromHighWindowDays;
    private const int CrawlerWindowDays = 63; // the Crawlers tab's 3M window
    private const int TypicalDays = DigestAnalysis.TypicalMoveDays;

    public static readonly IReadOnlyList<PatternDefinition> All =
    [
        new(MonthHigh, "At a 1-month high"),
        new(MonthLow, "At a 1-month low"),
        new(RangeBottom, "Near the bottom of its usual range"),
        new(RangeTop, "Near the top of its usual range"),
        new(FarBelowHigh, "10% or more below its recent high"),
        new(BigMove, "Moved twice its normal day"),
        new(SteadyClimber, "A steady climber"),
        new(StrongGroup, "In the strongest group this month"),
    ];

    /// <summary>
    /// Every single pattern and every pair, for the user's "no more than two at once" rule.
    /// Pairs that can't both be true (a 1-month high and a 1-month low) simply never fire.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PatternDefinition>> Combinations()
    {
        var result = new List<IReadOnlyList<PatternDefinition>>();
        for (var a = 0; a < All.Count; a++)
        {
            result.Add([All[a]]);
            for (var b = a + 1; b < All.Count; b++)
            {
                result.Add([All[a], All[b]]);
            }
        }
        return result;
    }

    /// <summary>
    /// Each pattern's state on every day of one symbol's series. <paramref name="strongGroup"/>
    /// is the one pattern that depends on other symbols, so it's computed by the caller.
    /// </summary>
    public static Dictionary<string, bool[]> States(IReadOnlyList<DailyClose> series, bool[] strongGroup)
    {
        var n = series.Count;
        var bars = series as DailyClose[] ?? series.ToArray();
        var closes = bars.Select(b => b.Close).ToArray();
        var states = All.ToDictionary(p => p.Id, _ => new bool[n]);
        states[StrongGroup] = strongGroup;

        for (var i = 1; i < n; i++)
        {
            var price = closes[i];

            if (i >= RangeDays)
            {
                var prior = new ArraySegment<decimal>(closes, i - RangeDays, RangeDays);
                states[MonthHigh][i] = price > prior.Max();
                states[MonthLow][i] = price < prior.Min();

                // Same rule as the digest's range edges: a sideways month, a range wide
                // enough to matter, and today's price in its top or bottom tenth.
                var window = new decimal?[RangeDays + 1];
                for (var k = 0; k <= RangeDays; k++) window[k] = closes[i - RangeDays + k];
                var trend = RangeMatrixQueryService.Summarise(window);
                if (trend.Direction == TrendDirection.Channeling
                    && trend.BandPct >= DigestAnalysis.RangeEdgeMinBandPct
                    && trend.PositionInBand is { } position)
                {
                    states[RangeBottom][i] = position <= DigestAnalysis.RangeEdgeZonePct;
                    states[RangeTop][i] = position >= 100m - DigestAnalysis.RangeEdgeZonePct;
                }
            }

            // 10% under the highest close of the trailing ~200 trading days, as on the Rebound tab.
            var farStart = Math.Max(0, i - FarWindowDays + 1);
            var high = closes[farStart];
            for (var k = farStart; k <= i; k++) if (closes[k] > high) high = closes[k];
            states[FarBelowHigh][i] = high > 0m && (high - price) / high * 100m >= DigestAnalysis.FarFromHighPct;

            // Today's move at least twice the normal day and at least 1.5%, as in the digest.
            if (i > TypicalDays && closes[i - 1] > 0m)
            {
                var move = Math.Abs((price - closes[i - 1]) / closes[i - 1] * 100m);
                if (move >= DigestAnalysis.UnusualMinPct
                    && DigestAnalysis.TypicalMovePct(new ArraySegment<DailyClose>(bars, i - TypicalDays, TypicalDays)) is { } typical
                    && typical > 0m)
                {
                    states[BigMove][i] = move / typical >= DigestAnalysis.UnusualMinRatio;
                }
            }

            if (i >= CrawlerWindowDays)
            {
                var slice = new ArraySegment<DailyClose>(bars, i - CrawlerWindowDays + 1, CrawlerWindowDays);
                states[SteadyClimber][i] = CrawlerAnalysis.Compute(slice, CrawlerWindowDays).IsSteadyCrawler;
            }
        }

        return states;
    }

    /// <summary>
    /// For each fund, the days on which it had the best 20-trading-day return of all the
    /// funds. A member stock is "in the strongest group" on the days its fund is.
    /// </summary>
    public static Dictionary<string, HashSet<DateTime>> StrongestGroupDays(
        IReadOnlyList<EtfGroup> groups, IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> history)
    {
        var returnsByDate = new Dictionary<DateTime, List<(string Etf, decimal Return)>>();
        foreach (var group in groups)
        {
            if (!history.TryGetValue(group.Etf, out var fund)) continue;
            for (var i = RangeDays; i < fund.Count; i++)
            {
                var start = fund[i - RangeDays].Close;
                if (start <= 0m) continue;
                if (!returnsByDate.TryGetValue(fund[i].TradingDate, out var list))
                {
                    returnsByDate[fund[i].TradingDate] = list = new();
                }
                list.Add((group.Etf, fund[i].Close / start - 1m));
            }
        }

        var result = groups.ToDictionary(g => g.Etf, _ => new HashSet<DateTime>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (date, list) in returnsByDate)
        {
            // Only a real contest: a date most funds traded on (not a lone foreign holiday bar).
            if (list.Count * 2 < groups.Count) continue;
            result[list.MaxBy(x => x.Return).Etf].Add(date);
        }

        return result;
    }
}
