namespace StockAggregatorApp.Models;

public sealed record CorrelationPairDto(string A, string B, decimal Corr);

/// <summary>
/// Pairwise correlation of the sector ETFs' daily returns — the heatmap matrix plus
/// the most-opposing and most-aligned pairs. Matrix is row-major over Symbols; a null
/// cell means the pair lacked enough overlapping history.
/// </summary>
public sealed record CorrelationResponse(
    int WindowDays,
    string? AsOfDate,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> Descriptions,
    IReadOnlyList<IReadOnlyList<decimal?>> Matrix,
    IReadOnlyList<CorrelationPairDto> MostOpposing,
    IReadOnlyList<CorrelationPairDto> MostAligned);
