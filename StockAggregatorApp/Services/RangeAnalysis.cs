using System.Globalization;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

/// <summary>
/// Pure range / volatility profile for one symbol's daily-bar history — the
/// "how much does this move, and how much does it typically give before a
/// pullback" numbers behind the Ranges tab. Medians (not means) throughout, so a
/// single outlier day doesn't distort the typical picture. Dependency-free and
/// unit-testable, like <see cref="ReboundAnalysis"/>.
/// </summary>
public static class RangeAnalysis
{
    public sealed record RangeStats(
        int BarCount,
        DateTime? HistoryStart,
        decimal? MedianDailyRangePct,
        decimal? MedianWeeklyRangePct,
        decimal? UpDayPct,
        decimal? MedianUpDayPct,
        decimal? MedianDownDayPct,
        decimal? TypicalGainBeforePullbackPct,
        int PullbackEpisodeCount);

    public static RangeStats Compute(IReadOnlyList<DailyBar> bars, decimal pullbackPct)
    {
        if (bars.Count == 0)
        {
            return new RangeStats(0, null, null, null, null, null, null, null, 0);
        }

        var dailyRanges = new List<decimal>();
        foreach (var b in bars)
        {
            if (b.Open is { } o and > 0m && b.High is { } h && b.Low is { } l)
            {
                dailyRanges.Add((h - l) / o * 100m);
            }
        }

        // Daily returns from consecutive closes.
        var upDays = new List<decimal>();
        var downDays = new List<decimal>();
        for (var i = 1; i < bars.Count; i++)
        {
            var prev = bars[i - 1].Close;
            if (prev == 0m) continue;
            var ret = (bars[i].Close - prev) / prev * 100m;
            if (ret > 0m) upDays.Add(ret);
            else if (ret < 0m) downDays.Add(-ret);
        }

        var returnDays = upDays.Count + downDays.Count;
        decimal? upDayPct = returnDays > 0 ? Math.Round((decimal)upDays.Count / returnDays * 100m, 1) : null;

        var (gain, episodes) = GainBeforePullback(bars, pullbackPct);

        return new RangeStats(
            BarCount: bars.Count,
            HistoryStart: bars[0].TradingDate,
            MedianDailyRangePct: Median(dailyRanges),
            MedianWeeklyRangePct: Median(WeeklyRanges(bars)),
            UpDayPct: upDayPct,
            MedianUpDayPct: Median(upDays),
            MedianDownDayPct: Median(downDays),
            TypicalGainBeforePullbackPct: gain,
            PullbackEpisodeCount: episodes);
    }

    private static List<decimal> WeeklyRanges(IReadOnlyList<DailyBar> bars)
    {
        var ranges = new List<decimal>();
        foreach (var week in bars.GroupBy(b => (ISOWeek.GetYear(b.TradingDate), ISOWeek.GetWeekOfYear(b.TradingDate))))
        {
            var days = week.ToList();
            var high = days.Where(d => d.High.HasValue).Select(d => d.High!.Value).DefaultIfEmpty(decimal.MinValue).Max();
            var low = days.Where(d => d.Low.HasValue).Select(d => d.Low!.Value).DefaultIfEmpty(decimal.MaxValue).Min();
            var open = days[0].Open ?? days[0].Close;
            if (high > decimal.MinValue && low < decimal.MaxValue && open > 0m)
            {
                ranges.Add((high - low) / open * 100m);
            }
        }

        return ranges;
    }

    /// <summary>
    /// Median run-up (local low → local peak) that accumulated before price fell
    /// at least <paramref name="pullbackPct"/> from that peak — the "typical gain
    /// before a pullback" a profit-taker would see.
    /// </summary>
    private static (decimal? Median, int Count) GainBeforePullback(IReadOnlyList<DailyBar> bars, decimal pullbackPct)
    {
        var p = pullbackPct / 100m;
        var gains = new List<decimal>();

        var runLow = bars[0].Close;
        var runPeak = bars[0].Close;
        foreach (var b in bars)
        {
            var c = b.Close;
            if (c > runPeak) runPeak = c;

            if (runPeak > runLow && runPeak > 0m && (runPeak - c) / runPeak >= p)
            {
                if (runLow > 0m)
                {
                    gains.Add((runPeak - runLow) / runLow * 100m);
                }
                runLow = c;
                runPeak = c;
            }
            else if (c < runLow)
            {
                runLow = c;
            }
        }

        return (Median(gains), gains.Count);
    }

    private static decimal? Median(List<decimal> values)
    {
        if (values.Count == 0) return null;
        values.Sort();
        var mid = values.Count / 2;
        var m = values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2m;
        return Math.Round(m, 2);
    }
}
