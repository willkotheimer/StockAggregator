using StockAggregatorApp.Models;

namespace StockAggregatorApp.Services;

public interface IQuoteQueryService
{
    /// <summary>
    /// Returns the most recent <paramref name="tradingDays"/> days of snapshots
    /// pivoted into a grid: snapshot columns plus one row per symbol (ETFs first).
    /// </summary>
    Task<WeekQuotesResponse> GetWeekAsync(int tradingDays = 5, CancellationToken cancellationToken = default);
}
