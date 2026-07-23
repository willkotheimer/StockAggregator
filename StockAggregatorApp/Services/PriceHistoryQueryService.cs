using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

public interface IPriceHistoryQueryService
{
    Task<PriceHistoryResponse> GetHistoryAsync(IReadOnlyList<string> symbols, int windowDays, CancellationToken cancellationToken = default);
}

/// <summary>
/// Daily close history for a handful of symbols over a trailing window, from
/// dbo.DailyOhlc — feeds the comparison chart. Caps the symbol count so a single
/// request stays small; read-time, no precompute.
/// </summary>
public sealed class PriceHistoryQueryService : IPriceHistoryQueryService
{
    private const int MaxSymbols = 8;

    private readonly IDailyOhlcReadRepository _repository;

    public PriceHistoryQueryService(IDailyOhlcReadRepository repository)
    {
        _repository = repository;
    }

    public async Task<PriceHistoryResponse> GetHistoryAsync(IReadOnlyList<string> symbols, int windowDays, CancellationToken cancellationToken = default)
    {
        var wanted = symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .Take(MaxSymbols)
            .ToList();

        var series = new List<SymbolSeries>();
        foreach (var symbol in wanted)
        {
            var bars = await _repository.GetClosesAsync(symbol, cancellationToken);
            var window = bars.Count > windowDays ? bars.Skip(bars.Count - windowDays) : bars;
            var points = window
                .Select(b => new PricePoint(b.TradingDate.ToString("yyyy-MM-dd"), b.Close))
                .ToList();
            series.Add(new SymbolSeries(symbol, points));
        }

        return new PriceHistoryResponse(windowDays, series);
    }
}
