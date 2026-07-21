namespace StockAggregatorApp.Models;

/// <summary>
/// One snapshot column: a single capture (a date + one of the four daily times).
/// <see cref="Key"/> is what row cells are keyed by, and columns are grouped by
/// <see cref="Date"/> (weekday) on the client.
/// </summary>
public sealed record SnapshotColumn(
    string Key,
    string Date,
    string RunLabel,
    DateTime CapturedAtUtc);

/// <summary>Price + change percent for one symbol at one snapshot.</summary>
public sealed record QuoteCell(
    decimal? Price,
    decimal? ChangePercent);

/// <summary>
/// One row in the grid. Rows come ordered as: an ETF, then its member symbols,
/// then the next ETF, and so on. <see cref="Cells"/> is keyed by
/// <see cref="SnapshotColumn.Key"/>; a missing key means no capture for that slot.
/// <see cref="Id"/> is unique per row (a symbol may appear under two ETFs);
/// <see cref="GroupEtf"/> is the ETF this row belongs to (equal to
/// <see cref="Symbol"/> for the ETF row itself).
/// </summary>
public sealed record SymbolRow(
    string Id,
    string Symbol,
    bool IsEtf,
    string GroupEtf,
    string? Description,
    IReadOnlyDictionary<string, QuoteCell> Cells);

/// <summary>The full grid: the snapshot columns and one row per symbol.</summary>
public sealed record WeekQuotesResponse(
    IReadOnlyList<SnapshotColumn> Snapshots,
    IReadOnlyList<SymbolRow> Rows);
