using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services;
using StockAggregatorApp.Services.Projection;
using Xunit;

namespace StockAggregator.Tests.Projection;

/// <summary>
/// The follow-on test must not see the future, must find a link that is really there,
/// and must not find one in noise. Those three are pinned here, plus the binomial math
/// behind "significant".
/// </summary>
public class FollowOnTestTests
{
    private static readonly string[] Funds = ["SMH", "XLK", "XLE", "XLV"];

    private static readonly EtfGroup[] Groups = Funds.Select(f => new EtfGroup { Etf = f, Members = [] }).ToArray();

    private enum Plant
    {
        None,
        /// <summary>SMH has an occasional sharp drop; XLK has its own two trading days later.</summary>
        SmhThenXlk,
        /// <summary>Every fund drops together, then every fund bounces two days later: all boats.</summary>
        MarketBounce,
        /// <summary>SMH alone drops; XLE alone jumps two days later: money moving between funds.</summary>
        MoneyMoves,
        /// <summary>SMH alone drops; TLT (a destination, not a stock fund) jumps two days later.</summary>
        IntoBonds,
    }

    /// <summary>Seeded random walks for the funds (plus TLT) on weekdays from 2020, with one planted behavior.</summary>
    private static Dictionary<string, IReadOnlyList<DailyClose>> Walks(int days, Plant plant, int seed = 11)
    {
        var rng = new Random(seed);
        var dates = new List<DateTime>();
        for (var d = new DateTime(2020, 1, 1); dates.Count < days; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dates.Add(d);
        }

        string[] symbols = [.. Funds, "TLT"];
        var shocks = symbols.ToDictionary(f => f, _ => new double[days]);
        foreach (var f in symbols)
        {
            for (var i = 0; i < days; i++)
            {
                shocks[f][i] = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 0.02;
            }
        }

        for (var i = 0; plant != Plant.None && i + 2 < days; i++)
        {
            if (rng.NextDouble() >= 0.03) continue;
            switch (plant)
            {
                case Plant.SmhThenXlk:
                    shocks["SMH"][i] = -0.06;
                    shocks["XLK"][i + 2] = -0.06;
                    break;
                case Plant.MarketBounce:
                    foreach (var f in Funds) { shocks[f][i] = -0.06; shocks[f][i + 2] = 0.06; }
                    break;
                case Plant.MoneyMoves:
                    shocks["SMH"][i] = -0.06;
                    shocks["XLE"][i + 2] = 0.06;
                    break;
                case Plant.IntoBonds:
                    shocks["SMH"][i] = -0.06;
                    shocks["TLT"][i + 2] = 0.06;
                    break;
            }
        }

        return symbols.ToDictionary(f => f, f =>
        {
            var price = 100.0;
            var series = new List<DailyClose>();
            for (var i = 0; i < days; i++)
            {
                price *= Math.Exp(shocks[f][i]);
                series.Add(new DailyClose(dates[i], (decimal)Math.Round(price, 4)));
            }
            return (IReadOnlyList<DailyClose>)series;
        });
    }

    /// <summary>TLT's history is always there; it only counts when named a destination.</summary>
    private static ProjectionInput Input(Dictionary<string, IReadOnlyList<DailyClose>> history, bool withBonds = false) =>
        new(Groups, history, new Dictionary<string, IReadOnlyList<DateTime>>(), withBonds ? ["TLT"] : null);

    private static bool Has(IEnumerable<FollowOnLink> links, string leader, bool leaderUp, string follower, bool followerUp) =>
        links.Any(l => l.Leader == leader && l.LeaderUp == leaderUp && l.Follower == follower && l.FollowerUp == followerUp);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeverLooksAhead_CuttingOffTheFutureLeavesEarlierGuessesUnchanged(bool relative)
    {
        var full = Walks(1300, Plant.MoneyMoves);
        var cutoff = full["SMH"][1000].TradingDate;
        var truncated = full.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DailyClose>)kv.Value.Where(b => b.TradingDate <= cutoff).ToList());

        var fromFull = FollowOnTest.Tests(Input(full, withBonds: true), relative).Where(t => t.Target <= cutoff).ToList();
        var fromTruncated = FollowOnTest.Tests(Input(truncated, withBonds: true), relative).ToList();

        Assert.NotEmpty(fromFull);
        Assert.Equal(fromTruncated, fromFull);
    }

    [Fact]
    public void FindsAPlantedLink_AndItsGuessesBeatNoPattern()
    {
        var card = FollowOnTest.Run(Input(Walks(1300, Plant.SmhThenXlk)));

        var link = Assert.Single(card.Links, l => l is { Leader: "SMH", LeaderUp: false, Follower: "XLK", FollowerUp: false, Days: 5 });
        Assert.True(link.Rate > 0.8, $"rate {link.Rate:P0}");
        Assert.True(link.BaseRate < 0.4, $"base rate {link.BaseRate:P0}");

        var five = card.Windows.Single(w => w.Days == 5);
        Assert.True(five.All.Edge > 0.3, $"edge {five.All.Edge:P0} over {five.All.Tests} guesses");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindsNothingToPass_InNoise(bool relative)
    {
        var card = FollowOnTest.Run(Input(Walks(1300, Plant.None), withBonds: true), relative);

        Assert.All(card.Windows, w => Assert.False(w.Passed, $"{w.Days}-day passed on noise"));
        Assert.DoesNotContain(card.Links, l => l.Rate - l.BaseRate > 0.3);
    }

    [Fact]
    public void MarketWideBounce_FoolsRaw_ButNotRelative()
    {
        // All boats: everything drops, then everything bounces. Raw sees "SMH down, then XLK up".
        var history = Walks(1300, Plant.MarketBounce);
        var raw = FollowOnTest.Run(Input(history));
        var relative = FollowOnTest.Run(Input(history), relative: true);

        Assert.True(Has(raw.Links, "SMH", false, "XLK", true), "raw should find the bounce");
        Assert.False(Has(relative.Links, "SMH", false, "XLK", true), "relative should see nothing move between funds");

        // And the self-link gives it away: SMH bounces after its own drops as often as XLK does.
        var self = Assert.Single(raw.SelfLinks!, l => l is { Leader: "SMH", LeaderUp: false, FollowerUp: true, Days: 5 });
        Assert.True(self.Rate > 0.8, $"self rate {self.Rate:P0}");
    }

    [Fact]
    public void MoneyMovingBetweenFunds_IsFoundByRelative()
    {
        var relative = FollowOnTest.Run(Input(Walks(1300, Plant.MoneyMoves)), relative: true);

        Assert.True(Has(relative.Links, "SMH", false, "XLE", true));
        Assert.True(relative.Windows.Single(w => w.Days == 5).All.Edge > 0.2);
        Assert.True(relative.Relative);
    }

    [Fact]
    public void Destinations_JoinAsFollowers_OnlyWhenNamed()
    {
        var history = Walks(1300, Plant.IntoBonds);

        Assert.True(Has(FollowOnTest.Run(Input(history, withBonds: true), relative: true).Links, "SMH", false, "TLT", true));
        Assert.DoesNotContain(FollowOnTest.Run(Input(history)).Links, l => l.Follower == "TLT" || l.Leader == "TLT");
    }

    [Fact]
    public void Destinations_StayOutOfTheMarketAverage()
    {
        // A huge TLT day must not move the stock funds' average and make them look big against it.
        var history = Walks(400, Plant.None);
        var day = history["TLT"][380].TradingDate;
        var before = FollowOnTest.BigDayFunds(Input(history, withBonds: true), day, relative: true);
        history["TLT"] = history["TLT"].Select((b, i) => i < 380 ? b : new DailyClose(b.TradingDate, b.Close * 1.5m)).ToList();

        var after = FollowOnTest.BigDayFunds(Input(history, withBonds: true), day, relative: true);

        Assert.Contains("TLT", after);
        Assert.Equal(before.Where(Funds.Contains), after.Where(Funds.Contains));
    }

    [Fact]
    public void Destinations_AreNeverPricePatternSubjects()
    {
        var history = Walks(700, Plant.None);
        var without = history.Where(kv => kv.Key != "TLT").ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(
            ProjectionTest.Tests(new ProjectionInput(Groups, without, new Dictionary<string, IReadOnlyList<DateTime>>())),
            ProjectionTest.Tests(Input(history, withBonds: true)));
    }

    [Fact]
    public void ABigDayIsJudgedAgainstEachFundsOwnNormal()
    {
        // Both funds drop 3% on the same day. For a fund that swings 0.5% a day that is big;
        // for one that swings 3% a day it is ordinary.
        var dates = Enumerable.Range(0, 300).Select(i => new DateTime(2020, 1, 1).AddDays(i)).ToList();
        IReadOnlyList<DailyClose> Series(double swing)
        {
            var price = 100.0;
            return dates.Select((d, i) =>
            {
                price *= Math.Exp(i == 299 ? -0.03 : i % 2 == 0 ? swing : -swing);
                return new DailyClose(d, (decimal)price);
            }).ToList();
        }
        var history = new Dictionary<string, IReadOnlyList<DailyClose>> { ["XLV"] = Series(0.005), ["SMH"] = Series(0.03) };

        var big = FollowOnTest.BigDayFunds(new ProjectionInput(
            [new EtfGroup { Etf = "XLV" }, new EtfGroup { Etf = "SMH" }], history, new Dictionary<string, IReadOnlyList<DateTime>>()), dates[^1]);

        Assert.Equal(["XLV"], big);
    }

    [Theory]
    [InlineData(0, 10, 0.3, 1.0)]
    [InlineData(11, 10, 0.3, 0.0)]
    [InlineData(10, 10, 0.5, 0.0009765625)]       // 0.5^10
    [InlineData(8, 10, 0.5, 0.0546875)]           // (45 + 10 + 1) / 1024
    [InlineData(1, 5, 0.2, 0.67232)]              // 1 - 0.8^5
    public void BinomialUpperTail_MatchesHandComputedValues(int k, int n, double p, double expected)
    {
        Assert.Equal(expected, FollowOnTest.BinomialUpperTail(k, n, p), precision: 6);
    }

    [Fact]
    public void BinomialUpperTail_StaysFiniteOnLongTallies()
    {
        var tail = FollowOnTest.BinomialUpperTail(700, 2000, 0.3);
        Assert.InRange(tail, 0.0, 1.0);
        Assert.True(tail < 0.01);
    }
}
