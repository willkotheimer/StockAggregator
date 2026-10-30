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

    /// <summary>
    /// Seeded random walks for the funds on weekdays from 2020. With <paramref name="planted"/>,
    /// SMH has an occasional sharp drop and XLK has its own two trading days later.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<DailyClose>> Walks(int days, bool planted, int seed = 11)
    {
        var rng = new Random(seed);
        var dates = new List<DateTime>();
        for (var d = new DateTime(2020, 1, 1); dates.Count < days; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dates.Add(d);
        }

        var shocks = Funds.ToDictionary(f => f, _ => new double[days]);
        foreach (var f in Funds)
        {
            for (var i = 0; i < days; i++)
            {
                shocks[f][i] = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 0.02;
            }
        }
        if (planted)
        {
            for (var i = 0; i + 2 < days; i++)
            {
                if (rng.NextDouble() < 0.03)
                {
                    shocks["SMH"][i] = -0.06;
                    shocks["XLK"][i + 2] = -0.06;
                }
            }
        }

        return Funds.ToDictionary(f => f, f =>
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

    private static ProjectionInput Input(Dictionary<string, IReadOnlyList<DailyClose>> history) =>
        new(Groups, history, new Dictionary<string, IReadOnlyList<DateTime>>());

    [Fact]
    public void NeverLooksAhead_CuttingOffTheFutureLeavesEarlierGuessesUnchanged()
    {
        var full = Walks(1300, planted: true);
        var cutoff = full["SMH"][1000].TradingDate;
        var truncated = full.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DailyClose>)kv.Value.Where(b => b.TradingDate <= cutoff).ToList());

        var fromFull = FollowOnTest.Tests(Input(full)).Where(t => t.Target <= cutoff).ToList();
        var fromTruncated = FollowOnTest.Tests(Input(truncated)).ToList();

        Assert.NotEmpty(fromFull);
        Assert.Equal(fromTruncated, fromFull);
    }

    [Fact]
    public void FindsAPlantedLink_AndItsGuessesBeatNoPattern()
    {
        var card = FollowOnTest.Run(Input(Walks(1300, planted: true)));

        var link = Assert.Single(card.Links, l => l is { Leader: "SMH", LeaderUp: false, Follower: "XLK", FollowerUp: false, Days: 5 });
        Assert.True(link.Rate > 0.8, $"rate {link.Rate:P0}");
        Assert.True(link.BaseRate < 0.4, $"base rate {link.BaseRate:P0}");

        var five = card.Windows.Single(w => w.Days == 5);
        Assert.True(five.All.Edge > 0.3, $"edge {five.All.Edge:P0} over {five.All.Tests} guesses");
    }

    [Fact]
    public void FindsNothingToPass_InNoise()
    {
        var card = FollowOnTest.Run(Input(Walks(1300, planted: false)));

        Assert.All(card.Windows, w => Assert.False(w.Passed, $"{w.Days}-day passed on noise"));
        Assert.DoesNotContain(card.Links, l => l.Rate - l.BaseRate > 0.3);
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
