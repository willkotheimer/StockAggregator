using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services;
using StockAggregatorApp.Services.Projection;
using Xunit;

namespace StockAggregator.Tests.Projection;

/// <summary>
/// The projection test is only worth anything if it can't see the future and its
/// baseline behaves as designed. Those two properties are pinned here; the rest are
/// the calendar rules it tags windows with.
/// </summary>
public class ProjectionTestTests
{
    private static readonly EtfGroup[] Groups =
    [
        new() { Etf = "XLK", Description = "Technology", Members = ["AAA", "BBB", "CCC"] },
        new() { Etf = "XLE", Description = "Energy", Members = ["DDD", "EEE", "FFF"] },
    ];

    /// <summary>Seeded random walks, one per symbol, on weekdays from 2020.</summary>
    private static Dictionary<string, IReadOnlyList<DailyClose>> RandomWalks(int days, int seed = 7)
    {
        var rng = new Random(seed);
        var symbols = Groups.SelectMany(g => g.Members.Prepend(g.Etf)).ToList();
        var dates = new List<DateTime>();
        for (var d = new DateTime(2020, 1, 1); dates.Count < days; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dates.Add(d);
        }

        var history = new Dictionary<string, IReadOnlyList<DailyClose>>();
        foreach (var symbol in symbols)
        {
            var price = 100.0;
            var series = new List<DailyClose>();
            foreach (var date in dates)
            {
                // Normal-ish daily moves of about 1.5%.
                var shock = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 0.03;
                price *= Math.Exp(shock);
                series.Add(new DailyClose(date, (decimal)Math.Round(price, 4)));
            }
            history[symbol] = series;
        }
        return history;
    }

    private static ProjectionInput Input(Dictionary<string, IReadOnlyList<DailyClose>> history) =>
        new(Groups, history, new Dictionary<string, IReadOnlyList<DateTime>>());

    [Fact]
    public void NeverLooksAhead_CuttingOffTheFutureLeavesEarlierTestsUnchanged()
    {
        var full = RandomWalks(900);
        var cutoff = full["AAA"][700].TradingDate;
        var truncated = full.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DailyClose>)kv.Value.Where(b => b.TradingDate <= cutoff).ToList());

        // Every test that was fully decided by the cutoff must come out identical either way.
        var fromFull = ProjectionTest.Tests(Input(full)).Where(t => t.Target <= cutoff).ToList();
        var fromTruncated = ProjectionTest.Tests(Input(truncated)).ToList();

        Assert.NotEmpty(fromFull);
        Assert.Equal(fromTruncated, fromFull);
    }

    [Fact]
    public void Baseline_LandsAboutHalfTheTime()
    {
        // The window is one median 20-day move wide, so knowing nothing lands ~50%.
        var card = ProjectionTest.Run(Input(RandomWalks(1100)));
        var all = card.QuarterEnd.With.Tests + card.QuarterEnd.Without.Tests;

        Assert.True(all > 500, $"only {all} tests");
        var baseline = (card.QuarterEnd.With.BaselineHitRate!.Value * card.QuarterEnd.With.Tests
            + card.QuarterEnd.Without.BaselineHitRate!.Value * card.QuarterEnd.Without.Tests) / all;
        Assert.InRange(baseline, 0.40, 0.60);
    }

    [Fact]
    public void RandomWalks_HaveNoPassingPattern()
    {
        // Pure noise must never clear the bar; if it does, the bar or the test is broken.
        var card = ProjectionTest.Run(Input(RandomWalks(1100, seed: 11)));

        Assert.DoesNotContain(card.Patterns, p => p.Passed);
    }

    [Fact]
    public void AtMostTwoPatternsAreCombined()
    {
        var combos = ProjectionPatterns.Combinations();

        Assert.All(combos, c => Assert.InRange(c.Count, 1, 2));
        Assert.Equal(8 + 28, combos.Count);
    }

    [Theory]
    [InlineData("2026-03-20", "2026-04-17", true)]   // crosses Mar 31
    [InlineData("2026-04-01", "2026-04-29", false)]
    [InlineData("2026-06-30", "2026-07-28", false)]  // origin on the quarter end doesn't count
    [InlineData("2026-06-02", "2026-06-30", true)]   // target on it does
    [InlineData("2025-12-10", "2026-01-09", true)]   // Dec 31 across a year end
    public void QuarterEnds(string origin, string target, bool expected)
    {
        Assert.Equal(expected, ProjectionTest.CrossesQuarterEnd(DateTime.Parse(origin), DateTime.Parse(target)));
    }

    [Fact]
    public void Earnings_FundsNeverReport_UnknownStocksAreLeftOut()
    {
        var earnings = new Dictionary<string, IReadOnlyList<DateTime>> { ["MSFT"] = [new DateTime(2026, 7, 29)] };
        var origin = new DateTime(2026, 7, 15);
        var target = new DateTime(2026, 8, 12);

        Assert.True(ProjectionTest.CrossesEarnings("MSFT", origin, target, earnings));
        Assert.False(ProjectionTest.CrossesEarnings("MSFT", target, target.AddDays(28), earnings));
        Assert.False(ProjectionTest.CrossesEarnings("XLK", origin, target, earnings));
        Assert.Null(ProjectionTest.CrossesEarnings("SSNLF", origin, target, earnings));
    }

    [Fact]
    public void EarningsEstimates_CarryKnownDatesBackByYears()
    {
        var reported = new[] { new DateTime(2026, 1, 28), new DateTime(2026, 4, 29), new DateTime(2026, 7, 29), new DateTime(2025, 10, 29) };

        var dates = EarningsCalendar.WithEstimates(reported, new DateTime(2024, 1, 1));

        // Each known date, plus one and two 52-week steps back (2024-01-03 is the last that fits).
        Assert.Contains(new DateTime(2025, 1, 29), dates);   // 2026-01-28 minus 52 weeks
        Assert.Contains(new DateTime(2024, 10, 30), dates);  // 2025-10-29 minus 52 weeks
        Assert.Contains(new DateTime(2024, 1, 31), dates);   // two steps back
        Assert.All(dates, d => Assert.True(d >= new DateTime(2024, 1, 1)));
        // Same weekday as the report it came from.
        Assert.Equal(DayOfWeek.Wednesday, new DateTime(2025, 1, 29).DayOfWeek);
    }

    [Fact]
    public void EarningsEstimates_NeverCrowdARealReport()
    {
        // 2026-10-28 back 52 weeks is 2025-10-29, the real report itself: no second copy,
        // and nothing estimated within the month before the earliest real date.
        var reported = new[] { new DateTime(2025, 10, 29), new DateTime(2026, 10, 28) };

        var dates = EarningsCalendar.WithEstimates(reported, new DateTime(2025, 1, 1));

        Assert.DoesNotContain(dates, d => d > new DateTime(2025, 9, 29) && d < new DateTime(2025, 10, 29));
    }

    [Fact]
    public void RunningMedian_MatchesASortedMedian()
    {
        var rng = new Random(3);
        var median = new RunningMedian();
        var values = new List<double>();
        for (var i = 0; i < 501; i++)
        {
            var v = rng.NextDouble() * 10 - 5;
            median.Add(v);
            values.Add(v);

            var sorted = values.OrderBy(x => x).ToList();
            var expected = sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            Assert.Equal(expected, median.Median, 12);
        }
    }

    [Fact]
    public void Refresher_RunsBeforeTheMiddayEmail_OnWeekdays()
    {
        // Friday 2026-10-16 at 16:00 UTC (11:00 CDT) is past 10:45, so next is Monday 10:45 CDT = 15:45 UTC.
        var friday = new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.Zero);

        var next = friday + ProjectionScorecardRefresher.TimeUntilNextRefresh(friday);

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 15, 45, 0, TimeSpan.Zero), next);
    }
}
