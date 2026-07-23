namespace StockAggregatorApp.Models;

public sealed record PricePoint(string Date, decimal Close);

public sealed record SymbolSeries(string Symbol, IReadOnlyList<PricePoint> Points);

/// <summary>Daily close series for the requested symbols over a trailing window,
/// for the comparison chart. The client aligns dates and normalises as needed.</summary>
public sealed record PriceHistoryResponse(int WindowDays, IReadOnlyList<SymbolSeries> Series);
