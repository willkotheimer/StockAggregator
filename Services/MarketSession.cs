using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Tells a live US session from a closed market using the quotes themselves. On a
/// market holiday Yahoo still answers, with the previous session's prices, so a
/// fetch alone can't tell — but each quote's last-update time stays on that earlier day.
/// </summary>
public static class MarketSession
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    /// <summary>
    /// True when at least half of the US listings were updated today (Eastern time).
    /// Non-US listings (7203.T) are ignored: Tokyo's session says nothing about New York's.
    /// </summary>
    public static bool IsOpenToday(IReadOnlyList<StockQuote> quotes, DateTimeOffset now)
    {
        var today = TimeZoneInfo.ConvertTime(now, Eastern).Date;

        var times = quotes
            .Where(q => !q.Symbol.Contains('.') && q.RegularMarketTime.HasValue)
            .Select(q => TimeZoneInfo.ConvertTime(q.RegularMarketTime!.Value, Eastern).Date)
            .ToList();

        return times.Count > 0 && times.Count(d => d == today) * 2 >= times.Count;
    }
}
