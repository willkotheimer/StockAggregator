namespace StockAggregatorApp.Models;

/// <summary>One quote row as read from the database.</summary>
public sealed record QuoteRecord(
    string Symbol,
    decimal? Price,
    decimal? ChangePercent,
    long? Volume,
    DateTime CapturedAtUtc,
    string RunLabel);
