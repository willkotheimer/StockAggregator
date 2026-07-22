using StockAggregatorApp.Models;

namespace StockAggregatorApp.Repositories;

public interface IQuoteReadRepository
{
    /// <summary>
    /// Reads all quote rows captured on or after <paramref name="sinceUtc"/>,
    /// ordered by capture time.
    /// </summary>
    Task<IReadOnlyList<QuoteRecord>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Distinct trading dates (UTC) that have captured quotes, ascending.</summary>
    Task<IReadOnlyList<DateTime>> GetAvailableDatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads all quote rows captured on the given trading dates.</summary>
    Task<IReadOnlyList<QuoteRecord>> GetForDatesAsync(IReadOnlyList<DateTime> dates, CancellationToken cancellationToken = default);
}
