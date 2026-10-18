using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services;
using StockAggregatorApp.Services.Digest;
using Xunit;

namespace StockAggregator.Tests.Digest;

/// <summary>
/// Each midday signal's threshold, pinned. A wrong threshold still produces a
/// plausible-looking email, so these are the only real check on what gets sent.
/// </summary>
public class DigestAnalysisTests
{
    private static readonly DateTime Today = new(2026, 10, 16);
    private static readonly DateTime Now = Today.AddHours(16).AddMinutes(30); // 11:30 CT in UTC

    private static readonly EtfGroup Chips = new() { Etf = "SMH", Description = "Semiconductors", Members = ["NVDA", "AMD", "MU", "AMAT"] };

    private static LiveQuote Quote(string symbol, decimal price, decimal previousClose, decimal? high = null, decimal? low = null) =>
        new(symbol, price, (price - previousClose) / previousClose * 100m, high ?? Math.Max(price, previousClose),
            low ?? Math.Min(price, previousClose), previousClose, Now);

    /// <summary>Daily closes ending the day before <see cref="Today"/>, one per weekday.</summary>
    private static IReadOnlyList<DailyClose> History(IEnumerable<decimal> closes)
    {
        var list = closes.ToList();
        var dates = new List<DateTime>();
        var d = Today.AddDays(-1);
        while (dates.Count < list.Count)
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dates.Add(d);
            d = d.AddDays(-1);
        }
        dates.Reverse();
        return list.Select((c, i) => new DailyClose(dates[i], c)).ToList();
    }

    /// <summary>Alternates ±<paramref name="dailyPct"/> around 100, so a normal day is exactly that size.</summary>
    private static IReadOnlyList<DailyClose> Wobble(int days, decimal dailyPct)
    {
        var closes = new List<decimal> { 100m };
        for (var i = 1; i < days; i++)
        {
            closes.Add(i % 2 == 1 ? closes[^1] * (1 + dailyPct / 100m) : closes[^1] / (1 + dailyPct / 100m));
        }
        return History(closes);
    }

    private static DigestSignals Run(IEnumerable<LiveQuote> live, Dictionary<string, IReadOnlyList<DailyClose>>? history = null, params EtfGroup[] groups) =>
        DigestAnalysis.Compute(new DigestInput(
            Today,
            groups.Length > 0 ? groups : [Chips],
            live.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase),
            history ?? new Dictionary<string, IReadOnlyList<DailyClose>>()));

    [Fact]
    public void TypicalMove_IsTheMedianAbsoluteDailyMove()
    {
        var typical = DigestAnalysis.TypicalMovePct(Wobble(100, 1m));

        Assert.NotNull(typical);
        Assert.InRange(typical!.Value, 0.98m, 1.01m);
    }

    [Fact]
    public void TypicalMove_NeedsEnoughHistory()
    {
        Assert.Null(DigestAnalysis.TypicalMovePct(Wobble(20, 1m)));
    }

    [Fact]
    public void Unusual_FlagsAMoveTwiceItsNormalDay()
    {
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = Wobble(100, 1m) };

        var signals = Run([Quote("MU", 102.5m, 100m)], history);

        var move = Assert.Single(signals.Unusual);
        Assert.Equal("MU", move.Symbol);
        Assert.InRange(move.Ratio, 2.4m, 2.6m);
    }

    [Fact]
    public void Unusual_IgnoresABigMoveForAStockThatAlwaysMovesBig()
    {
        // 3% is huge for most stocks but a normal day for this one.
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = Wobble(100, 3m) };

        Assert.Empty(Run([Quote("MU", 103m, 100m)], history).Unusual);
    }

    [Fact]
    public void Unusual_IgnoresASmallMoveEvenForACalmStock()
    {
        // 1.2% is 4x this stock's normal day, but under the 1.5% floor.
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = Wobble(100, 0.3m) };

        Assert.Empty(Run([Quote("MU", 101.2m, 100m)], history).Unusual);
    }

    [Fact]
    public void TurnAround_UpEarlierNowDown()
    {
        var signals = Run([Quote("NVDA", 99.6m, 100m, high: 102.1m)]);

        var turn = Assert.Single(signals.TurnArounds);
        Assert.Equal(2.1m, Math.Round(turn.EarlierPct, 1));
        Assert.True(turn.NowPct < 0m);
    }

    [Fact]
    public void TurnAround_DownEarlierNowUp()
    {
        var signals = Run([Quote("NVDA", 100.3m, 100m, low: 97.5m)]);

        var turn = Assert.Single(signals.TurnArounds);
        Assert.Equal(-2.5m, Math.Round(turn.EarlierPct, 1));
    }

    [Fact]
    public void TurnAround_NotWhenStillOnTheSameSide()
    {
        // Was up 2.1%, gave some back, but still up: not a turnaround.
        Assert.Empty(Run([Quote("NVDA", 100.8m, 100m, high: 102.1m)]).TurnArounds);
    }

    [Fact]
    public void FundDivergence_FundUpWhileMostStocksAreNot()
    {
        var live = new[]
        {
            Quote("SMH", 101m, 100m),
            Quote("NVDA", 104m, 100m),
            Quote("AMD", 99.5m, 100m),
            Quote("MU", 99m, 100m),
            Quote("AMAT", 100m, 100m),
        };

        var divergence = Assert.Single(Run(live).FundDivergences);
        Assert.Equal(4, divergence.MembersTracked);
        Assert.Equal(3, divergence.MembersFlatOrDown);
        Assert.Equal("NVDA", divergence.TopSymbol);
    }

    [Fact]
    public void FundDivergence_NotWhenMostStocksAreUpToo()
    {
        var live = new[]
        {
            Quote("SMH", 101m, 100m), Quote("NVDA", 104m, 100m), Quote("AMD", 101m, 100m),
            Quote("MU", 101m, 100m), Quote("AMAT", 99m, 100m),
        };

        Assert.Empty(Run(live).FundDivergences);
    }

    [Fact]
    public void Groups_StrongestAndWeakestFunds()
    {
        var energy = new EtfGroup { Etf = "XLE", Description = "Energy", Members = ["XOM"] };
        var signals = Run([Quote("SMH", 101.2m, 100m), Quote("XLE", 99.1m, 100m)], null, Chips, energy);

        Assert.Equal("SMH", signals.Strongest!.Etf);
        Assert.Equal("XLE", signals.Weakest!.Etf);
    }

    [Fact]
    public void MonthHigh_WhenTodayIsAboveTheLast20Closes()
    {
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = History(Enumerable.Range(0, 30).Select(i => 100m + i % 5)) };

        var extreme = Assert.Single(Run([Quote("MU", 105m, 104m)], history).MonthExtremes);
        Assert.True(extreme.IsHigh);
        Assert.Equal(104m, extreme.PreviousExtreme);
    }

    [Fact]
    public void MonthHigh_IgnoresHighsOlderThanAMonth()
    {
        // A 120 close 25 days ago is outside the 20-day window, so 105 is still a month high.
        var closes = Enumerable.Range(0, 30).Select(i => i == 5 ? 120m : 100m + i % 5);
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = History(closes) };

        Assert.Single(Run([Quote("MU", 105m, 104m)], history).MonthExtremes);
    }

    [Fact]
    public void RangeEdge_SidewaysStockNearTheBottomOfItsRange()
    {
        // Swings 100 <-> 106 for a month: a sideways range, and 100.2 sits at its bottom.
        // 26 closes so the 20-day window starts on a 100, matching where it ends.
        var closes = Enumerable.Range(0, 26).Select(i => i % 2 == 0 ? 100m : 106m);
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = History(closes) };

        var edge = Assert.Single(Run([Quote("MU", 100.2m, 106m)], history).RangeEdges);
        Assert.False(edge.NearTop);
        Assert.Equal(100m, edge.Low);
        Assert.Equal(106m, edge.High);
    }

    [Fact]
    public void RangeEdge_NotForATrendingStock()
    {
        // A steady climb ends at the top of its band, but it's a trend, not a range.
        var closes = Enumerable.Range(0, 25).Select(i => 100m + i);
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = History(closes) };

        Assert.Empty(Run([Quote("MU", 123.5m, 124m)], history).RangeEdges);
    }

    [Fact]
    public void FarFromHigh_OnlyOnTheDayItCrossesTheLine()
    {
        // Flat at 100 for 150 days, then 92 yesterday: 8% down, not yet 10%.
        var closes = Enumerable.Repeat(100m, 150).Append(92m);
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = History(closes) };

        var crossed = Assert.Single(Run([Quote("MU", 89m, 92m)], history).FarFromHighs);
        Assert.Equal(11m, Math.Round(crossed.BelowHighPct, 1));

        // Already 11% down yesterday: it was in yesterday's email, so it isn't repeated.
        var already = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = History(Enumerable.Repeat(100m, 150).Append(89m)) };
        Assert.Empty(Run([Quote("MU", 88m, 89m)], already).FarFromHighs);
    }

    [Fact]
    public void FarFromHigh_CountsPastDropsThatRecovered()
    {
        // One earlier 12% drop that recovered 30 days after its low, then flat, then today's drop.
        var closes = new List<decimal>();
        closes.AddRange(Enumerable.Repeat(100m, 40));
        closes.AddRange(Enumerable.Repeat(88m, 30));
        closes.AddRange(Enumerable.Repeat(100m, 120));
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["MU"] = History(closes) };

        var far = Assert.Single(Run([Quote("MU", 88m, 100m)], history).FarFromHighs);
        Assert.NotNull(far.BaseRate);
        Assert.Equal(1, far.BaseRate!.EpisodeCount);
    }

    [Fact]
    public void ImplausibleIntradayMovesAreTreatedAsBadData()
    {
        // Seen live: SSNLF reported a session low 37.7% under its previous close on a flat day.
        Assert.Empty(Run([Quote("SSNLF", 100m, 100m, low: 62.3m)]).TurnArounds);
    }

    [Fact]
    public void ACompanyThatGoesByItsTickerIsNotNamedTwice()
    {
        Assert.Equal("TJX", DigestNames.Of("TJX"));
        Assert.Equal("Micron (MU)", DigestNames.Of("MU"));
        // Only an exact match counts: "Meta" is a name, not a repeat of "META".
        Assert.Equal("Meta (META)", DigestNames.Of("META"));
    }

    [Fact]
    public void NonUsListingsAreLeftOut()
    {
        // Tokyo is closed at a US midday, so Toyota's "today" price is stale.
        var group = new EtfGroup { Etf = "VEU", Description = "International", Members = ["7203.T"] };
        var signals = Run([Quote("7203.T", 2900m, 2800m, high: 2950m)], null, group);

        Assert.Empty(signals.TurnArounds);
        Assert.Null(signals.Strongest);
    }
}
