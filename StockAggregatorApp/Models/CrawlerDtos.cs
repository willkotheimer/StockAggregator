namespace StockAggregatorApp.Models;

/// <summary>One symbol's steady-crawler row in the screener.</summary>
public sealed record CrawlerRow(
    string Symbol,
    string Etf,
    int BarCount,
    decimal? ReturnPct,
    decimal? MaxDrawdownPct,
    decimal? UpDayPct,
    decimal? Steadiness,
    decimal? WeeklyDriftPct,
    bool IsSteadyCrawler,
    IReadOnlyList<decimal> Spark);

public sealed record CrawlerResponse(
    int WindowDays,
    string? AsOfDate,
    IReadOnlyList<CrawlerRow> Rows);
