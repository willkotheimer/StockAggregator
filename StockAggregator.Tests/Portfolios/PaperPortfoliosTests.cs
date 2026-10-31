using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services.Portfolios;
using Xunit;

namespace StockAggregator.Tests.Portfolios;

/// <summary>
/// The practice portfolios are only fair if every mix gets the same money on the same
/// days at real closes, and nothing before the start date counts. Those rules, and
/// Test 2's swing, are pinned here with numbers small enough to check by hand.
/// </summary>
public class PaperPortfoliosTests
{
    private static readonly DateTime Start = PaperPortfolios.Start; // Fri Oct 2, 2026

    /// <summary>Weekday closes from a week before the start through <paramref name="through"/>, from a price function.</summary>
    private static IReadOnlyList<DailyClose> Series(DateTime through, Func<DateTime, decimal> price, params DateTime[] closed)
    {
        var list = new List<DailyClose>();
        for (var d = Start.AddDays(-7); d <= through; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || closed.Contains(d)) continue;
            list.Add(new DailyClose(d, price(d)));
        }
        return list;
    }

    private static Dictionary<string, IReadOnlyList<DailyClose>> Flat(DateTime through, params (string Symbol, decimal Price)[] symbols) =>
        symbols.ToDictionary(s => s.Symbol, s => Series(through, _ => s.Price));

    private static PortfolioResult Only(PortfolioDefinition def, Dictionary<string, IReadOnlyList<DailyClose>> history, DateTime asOf,
        IReadOnlyDictionary<string, decimal>? latest = null) =>
        Assert.Single(PaperPortfolios.Run([def], history, asOf, latest).Ranked);

    [Fact]
    public void Contributions_EveryOtherFridayFromTheStart()
    {
        // Oct 2, Oct 16, Oct 30: three by Oct 30's close; still two the day before.
        var history = Flat(new DateTime(2026, 10, 30), ("VOO", 100m));
        var def = new PortfolioDefinition(1, "S&P 500 only", [new("VOO", 1m)]);

        Assert.Equal(3, Only(def, history, new DateTime(2026, 10, 30)).Contributions);
        Assert.Equal(2, Only(def, history, new DateTime(2026, 10, 29)).Contributions);
        Assert.Equal(500m, Only(def, history, new DateTime(2026, 10, 29)).Contributed);
    }

    [Fact]
    public void AHolidayFriday_GoesInOnTheNextTradingDay()
    {
        var holiday = new DateTime(2026, 10, 16);
        var history = new Dictionary<string, IReadOnlyList<DailyClose>>
        {
            ["VOO"] = Series(new DateTime(2026, 10, 19), d => d < new DateTime(2026, 10, 19) ? 100m : 125m, holiday),
        };
        var def = new PortfolioDefinition(1, "S&P 500 only", [new("VOO", 1m)]);

        var r = Only(def, history, new DateTime(2026, 10, 19));

        // $250 at 100 on Oct 2, then $250 at Monday's 125: 2.5 + 2 = 4.5 shares.
        Assert.Equal(2, r.Contributions);
        Assert.Equal(4.5m, Assert.Single(r.Holdings).Shares);
    }

    [Fact]
    public void Contributions_SplitByWeight_InFractionalShares_ValuedAtTheLatestPrice()
    {
        var history = Flat(new DateTime(2026, 10, 2), ("VOO", 500m), ("WMT", 100m), ("XOM", 125m));
        var def = new PortfolioDefinition(3, "S&P 500 + Walmart + Exxon", [new("VOO", 0.5m), new("WMT", 0.25m), new("XOM", 0.25m)]);

        var r = Only(def, history, new DateTime(2026, 10, 2), new Dictionary<string, decimal> { ["VOO"] = 550m });

        Assert.Equal(0.25m, r.Holdings.Single(h => h.Symbol == "VOO").Shares);
        Assert.Equal(0.625m, r.Holdings.Single(h => h.Symbol == "WMT").Shares);
        Assert.Equal(0.5m, r.Holdings.Single(h => h.Symbol == "XOM").Shares);
        // VOO's half is now worth 0.25 * 550 = 137.50; the rest is unchanged.
        Assert.Equal(262.50m, r.Value);
        Assert.Equal(5m, r.GainPct);
    }

    [Fact]
    public void NothingBeforeTheStartCounts()
    {
        // A huge run in the week before the start must not matter.
        var history = new Dictionary<string, IReadOnlyList<DailyClose>>
        {
            ["VOO"] = Series(new DateTime(2026, 10, 2), d => d < Start ? 10m : 100m),
        };
        var def = new PortfolioDefinition(1, "S&P 500 only", [new("VOO", 1m)]);

        var r = Only(def, history, new DateTime(2026, 10, 2));

        Assert.Equal(2.5m, Assert.Single(r.Holdings).Shares);
        Assert.Equal(0m, r.Gain);
    }

    [Fact]
    public void Swing_MovesIntoTheStockBelowTheLine_AndBackToCashAboveTheOther()
    {
        // SNDK: 1400 at the start (cash waits), 1200 from Oct 7 (moves in), 1600 from Oct 14 (moves out).
        decimal Sndk(DateTime d) => d < new DateTime(2026, 10, 7) ? 1400m : d < new DateTime(2026, 10, 14) ? 1200m : 1600m;
        var history = new Dictionary<string, IReadOnlyList<DailyClose>>
        {
            ["VOO"] = Series(new DateTime(2026, 10, 15), _ => 100m),
            ["SNDK"] = Series(new DateTime(2026, 10, 15), Sndk),
        };
        var def = new PortfolioDefinition(2, "S&P 500 + Sandisk swing",
            [new("VOO", 0.5m), new(PaperPortfolios.Cash, 0.5m)], new SwingRule("SNDK", 1300m, 1500m));

        var waiting = Only(def, history, new DateTime(2026, 10, 6));
        Assert.False(waiting.SwingInStock);
        Assert.DoesNotContain(waiting.Holdings, h => h.Symbol == "SNDK");

        var holding = Only(def, history, new DateTime(2026, 10, 8));
        Assert.True(holding.SwingInStock);
        Assert.Equal(new DateTime(2026, 10, 7), holding.LastSwing);
        Assert.DoesNotContain(holding.Holdings, h => h.Symbol == PaperPortfolios.Cash);

        var sold = Only(def, history, new DateTime(2026, 10, 15));
        Assert.False(sold.SwingInStock);
        Assert.Equal(new DateTime(2026, 10, 14), sold.LastSwing);
        // ~$125 went in at 1200 and came out at 1600: about $166.67 in cash, plus a few days' yield.
        Assert.InRange(sold.Holdings.Single(h => h.Symbol == PaperPortfolios.Cash).Value, 166.6m, 166.9m);
    }

    [Fact]
    public void Swing_NewCashMovesInWhileTheStockStaysBelowTheLine()
    {
        var history = Flat(new DateTime(2026, 10, 16), ("VOO", 100m), ("SNDK", 1000m));
        var def = new PortfolioDefinition(2, "S&P 500 + Sandisk swing",
            [new("VOO", 0.5m), new(PaperPortfolios.Cash, 0.5m)], new SwingRule("SNDK", 1300m, 1500m));

        var r = Only(def, history, new DateTime(2026, 10, 16));

        // Both $125 halves landed in cash and moved straight in: 0.25 shares, a hair more for the interest.
        Assert.InRange(r.Holdings.Single(h => h.Symbol == "SNDK").Shares, 0.25m, 0.2502m);
        Assert.DoesNotContain(r.Holdings, h => h.Symbol == PaperPortfolios.Cash);
    }

    [Fact]
    public void Cash_EarnsItsYield()
    {
        // No swing trigger: SNDK stays between the lines, so the cash half just sits.
        var history = Flat(new DateTime(2026, 11, 2), ("VOO", 100m), ("SNDK", 1400m));
        var def = new PortfolioDefinition(2, "S&P 500 + Sandisk swing",
            [new("VOO", 0.5m), new(PaperPortfolios.Cash, 0.5m)], new SwingRule("SNDK", 1300m, 1500m));

        var cash = Only(def, history, new DateTime(2026, 10, 15)).Holdings.Single(h => h.Symbol == PaperPortfolios.Cash).Value;

        // $125 for 13 days at 4% a year: about 18 cents.
        Assert.InRange(cash, 125.17m, 125.19m);
    }

    [Fact]
    public void Leaderboard_RanksByValue_AndSaysWhenTheNextMoneyGoesIn()
    {
        var history = new Dictionary<string, IReadOnlyList<DailyClose>>
        {
            ["VOO"] = Series(new DateTime(2026, 10, 29), _ => 100m),
            ["MU"] = Series(new DateTime(2026, 10, 29), d => d < new DateTime(2026, 10, 20) ? 100m : 150m),
        };
        PortfolioDefinition[] defs =
        [
            new(1, "S&P 500 only", [new("VOO", 1m)]),
            new(4, "S&P 500 + Micron", [new("VOO", 0.5m), new("MU", 0.5m)]),
        ];

        var board = PaperPortfolios.Run(defs, history, new DateTime(2026, 10, 29));

        Assert.Equal([4, 1], board.Ranked.Select(r => r.Number));
        Assert.Equal(new DateTime(2026, 10, 30), board.NextContribution);
        Assert.Equal(new DateTime(2026, 10, 29), board.ThroughClose);
    }

    [Fact]
    public void TheEightPortfolios_AreAsSpecified()
    {
        Assert.Equal(Enumerable.Range(1, 8), PaperPortfolios.All.Select(p => p.Number));
        Assert.All(PaperPortfolios.All, p => Assert.Equal(1m, p.Legs.Sum(l => l.Weight)));
        Assert.Equal(["VOO", "SNDK", "WMT", "XOM", "MU", "SMH", "LLY", "UNH", "VUG"], PaperPortfolios.PricedSymbols);
        Assert.Equal(new SwingRule("SNDK", 1300m, 1500m), PaperPortfolios.All.Single(p => p.Number == 2).Swing);
    }
}
