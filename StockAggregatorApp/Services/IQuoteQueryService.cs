using StockAggregatorApp.Models;

namespace StockAggregatorApp.Services;

public interface IQuoteQueryService
{
    /// <summary>
    /// Returns the most recent <paramref name="tradingDays"/> days of snapshots
    /// pivoted into a grid: snapshot columns plus one row per symbol (ETFs first).
    /// </summary>
    Task<WeekQuotesResponse> GetWeekAsync(int tradingDays = 5, CancellationToken cancellationToken = default);

    /// <summary>Distinct trading dates (yyyy-MM-dd) that have captured quotes.</summary>
    Task<IReadOnlyList<string>> GetAvailableDatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Snapshots + ETF-grouped rows for the given trading dates.</summary>
    Task<WeekQuotesResponse> GetDaysAsync(IReadOnlyList<DateTime> dates, CancellationToken cancellationToken = default);
}
