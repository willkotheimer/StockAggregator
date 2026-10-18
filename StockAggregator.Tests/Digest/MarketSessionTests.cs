using StockAggregator.Models;
using StockAggregator.Services;
using Xunit;

namespace StockAggregator.Tests.Digest;

/// <summary>The digest must stay silent on market holidays, when Yahoo still answers with old prices.</summary>
public class MarketSessionTests
{
    // Monday 2026-10-19, 11:30 CT = 16:30 UTC.
    private static readonly DateTimeOffset Midday = new(2026, 10, 19, 16, 30, 0, TimeSpan.Zero);

    private static StockQuote Updated(string symbol, DateTimeOffset at) =>
        new() { Symbol = symbol, Price = 100m, RegularMarketTime = at };

    [Fact]
    public void Open_WhenUsQuotesUpdatedToday()
    {
        var quotes = new[] { Updated("AAPL", Midday.AddMinutes(-1)), Updated("MSFT", Midday.AddMinutes(-2)) };

        Assert.True(MarketSession.IsOpenToday(quotes, Midday));
    }

    [Fact]
    public void Closed_WhenQuotesAreStillFromTheLastSession()
    {
        // A holiday Monday: Yahoo's prices are Friday's close.
        var friday = new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
        var quotes = new[] { Updated("AAPL", friday), Updated("MSFT", friday) };

        Assert.False(MarketSession.IsOpenToday(quotes, Midday));
    }

    [Fact]
    public void TokyoTradingDoesNotMeanNewYorkIsOpen()
    {
        var friday = new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
        var quotes = new[] { Updated("AAPL", friday), Updated("7203.T", Midday.AddHours(-10)) };

        Assert.False(MarketSession.IsOpenToday(quotes, Midday));
    }

    [Fact]
    public void Closed_WhenNothingCameBack()
    {
        Assert.False(MarketSession.IsOpenToday([], Midday));
    }
}
