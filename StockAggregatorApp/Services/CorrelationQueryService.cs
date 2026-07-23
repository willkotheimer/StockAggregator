using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

public interface ICorrelationQueryService
{
    Task<CorrelationResponse> GetCorrelationsAsync(int windowDays, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-time pairwise correlation of the sector ETFs, from the backfilled daily
/// history (dbo.DailyOhlc). The rotation lens: which sectors move together and
/// which move oppositely. ~11 ETFs × a short daily series — a handful of small
/// reads and an O(n²) pass — so nothing is precomputed.
/// </summary>
public sealed class CorrelationQueryService : ICorrelationQueryService
{
    private const int TopPairs = 8;

    private readonly IDailyOhlcReadRepository _repository;
    private readonly IReadOnlyList<EtfGroup> _groups;

    public CorrelationQueryService(IDailyOhlcReadRepository repository, IOptions<EtfGroupOptions> groupOptions)
    {
        _repository = repository;
        _groups = groupOptions.Value.Groups;
    }

    public async Task<CorrelationResponse> GetCorrelationsAsync(int windowDays, CancellationToken cancellationToken = default)
    {
        var series = new List<(string Symbol, IReadOnlyList<DailyClose> Bars)>();
        foreach (var group in _groups)
        {
            var bars = await _repository.GetClosesAsync(group.Etf, cancellationToken);
            series.Add((group.Etf, bars));
        }

        var result = CorrelationAnalysis.Compute(series, windowDays);

        var descriptions = result.Symbols
            .Select(s => _groups.FirstOrDefault(g => string.Equals(g.Etf, s, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty)
            .ToList();

        var matrix = result.Matrix.Select(row => (IReadOnlyList<decimal?>)row.ToList()).ToList();

        var mostOpposing = result.Pairs
            .Take(TopPairs)
            .Select(p => new CorrelationPairDto(p.A, p.B, p.Corr))
            .ToList();

        // Most aligned = the far end, biggest correlation first (self-pairs excluded already).
        var mostAligned = result.Pairs
            .AsEnumerable()
            .Reverse()
            .Take(TopPairs)
            .Select(p => new CorrelationPairDto(p.A, p.B, p.Corr))
            .ToList();

        return new CorrelationResponse(
            WindowDays: result.WindowDays,
            AsOfDate: result.AsOf?.ToString("yyyy-MM-dd"),
            Symbols: result.Symbols,
            Descriptions: descriptions,
            Matrix: matrix,
            MostOpposing: mostOpposing,
            MostAligned: mostAligned);
    }
}
