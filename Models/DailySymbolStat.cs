namespace StockAggregator.Models;

/// <summary>
/// One symbol's rollup for one trading day, derived from that day's snapshots.
/// The keystone the other analytics build on. Rebuildable from raw StockQuotes.
/// </summary>
public sealed record DailySymbolStat(
    string Symbol,
    DateTime TradingDate,
    decimal? FirstPrice,
    decimal? LastPrice,
    decimal? DayHigh,
    decimal? DayLow,
    decimal? PreviousClose,
    decimal? ChangePct,
    decimal? IntradayRangePct,
    decimal? MaxSnapshotDrawdownPct,
    int SnapshotCount);
