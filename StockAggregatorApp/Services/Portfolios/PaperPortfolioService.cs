using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services.Digest;

namespace StockAggregatorApp.Services.Portfolios;

public interface IPaperPortfolioService
{
    /// <summary>
    /// The leaderboard with rules applied through the last close before <paramref name="asOf"/>
    /// (or through today, when null), valued at <paramref name="latestPrices"/> where given.
    /// </summary>
    Task<PortfolioLeaderboard> GetAsync(
        DateTime? asOf = null,
        IReadOnlyDictionary<string, decimal>? latestPrices = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Loads the closes the practice portfolios need, stitched like the digest's history, and runs the rules.</summary>
public sealed class PaperPortfolioService : IPaperPortfolioService
{
    private readonly IDailyOhlcReadRepository _ohlc;
    private readonly IAnalyticsReadRepository _rollup;

    public PaperPortfolioService(IDailyOhlcReadRepository ohlc, IAnalyticsReadRepository rollup)
    {
        _ohlc = ohlc;
        _rollup = rollup;
    }

    public async Task<PortfolioLeaderboard> GetAsync(
        DateTime? asOf = null,
        IReadOnlyDictionary<string, decimal>? latestPrices = null,
        CancellationToken cancellationToken = default)
    {
        // With a day given (the digest's), only closes strictly before it are known; today's
        // price comes from latestPrices, and today's contribution goes in at today's close.
        var through = asOf is { } d ? d.Date.AddDays(-1) : DateTime.UtcNow.Date;
        var from = PaperPortfolios.Start.AddDays(-7);

        var symbols = PaperPortfolios.PricedSymbols;
        var official = await _ohlc.GetClosesForSymbolsAsync(symbols, from, through, cancellationToken);
        var recent = await _rollup.GetLastPricesForSymbolsAsync(symbols, from, through, cancellationToken);
        var history = DigestQueryService.Stitch(official, recent);

        var board = PaperPortfolios.Run(history, through, latestPrices);
        return asOf is { } day ? board with { ValuedAt = day.Date } : board;
    }
}
