namespace StockAggregator.Models;

/// <summary>
/// One daily OHLC bar for a symbol, as returned by Yahoo's historical chart
/// endpoint. The source of truth for historical daily prices (dbo.DailyOhlc).
/// No intraday path — that's what the live snapshots capture going forward.
/// </summary>
public sealed record DailyOhlcBar(
    string Symbol,
    DateTime TradingDate,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? Close,
    decimal? AdjClose,
    long? Volume);
