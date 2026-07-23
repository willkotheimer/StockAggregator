namespace StockAggregatorApp.Models;

/// <summary>One symbol's range / volatility profile row in the Ranges table.</summary>
public sealed record RangeRow(
    string Symbol,
    bool IsEtf,
    int BarCount,
    string? HistoryStart,
    decimal? MedianDailyRangePct,
    decimal? MedianWeeklyRangePct,
    decimal? UpDayPct,
    decimal? MedianUpDayPct,
    decimal? MedianDownDayPct,
    decimal? TypicalGainBeforePullbackPct,
    int PullbackEpisodeCount);

public sealed record RangeResponse(
    string Etf,
    string Description,
    decimal PullbackPct,
    IReadOnlyList<RangeRow> Rows);
