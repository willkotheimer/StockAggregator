using StockAggregatorApp.Models;

namespace StockAggregatorApp.Repositories;

public interface IQuoteReadRepository
{
    /// <summary>
    /// Reads all quote rows captured on or after <paramref name="sinceUtc"/>,
    /// ordered by capture time.
    /// </summary>
    Task<IReadOnlyList<QuoteRecord>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
}
