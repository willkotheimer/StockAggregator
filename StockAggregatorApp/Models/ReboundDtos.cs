namespace StockAggregatorApp.Models;

/// <summary>One completed drawdown (trough) or run-up (surge) episode, for the table.</summary>
public sealed record ReboundEpisodeDto(
    string AnchorDate,      // peak date (trough) / low date (surge)
    decimal AnchorPrice,
    string ExtremeDate,     // trough date (trough) / peak date (surge)
    decimal ExtremePrice,
    decimal MovePct,        // depth (trough) / height (surge), always positive
    int AnchorToExtremeDays,
    int? ExtremeToReversalDays,
    int? AnchorToReversalDays);

/// <summary>The active, un-reversed episode the stock is in right now (or null).</summary>
public sealed record ReboundCurrentDto(
    string AnchorDate,
    decimal AnchorPrice,
    string ExtremeDate,
    decimal ExtremePrice,
    decimal MaxMovePct,     // furthest it has moved from the anchor so far
    decimal CurrentMovePct, // where it sits now vs the anchor
    int DaysSinceAnchor,
    int DaysSinceExtreme);

/// <summary>Historical base rate for episodes comparable to the current one.
/// Median + range + n — never a bare point estimate.</summary>
public sealed record ReboundBaseRateDto(
    decimal ComparableMovePct,   // "episodes at least this large"
    int EpisodeCount,
    int MedianReversalDays,      // extreme -> reversal (recovery/pullback)
    int MinReversalDays,
    int MaxReversalDays,
    int ShortWindowDays,
    int ReversedWithinShort,
    int LongWindowDays,
    int ReversedWithinLong);

public sealed record ReboundResponse(
    string Symbol,
    string Mode,                 // "trough" | "surge"
    decimal ThresholdPct,
    int CurrentWindowDays,       // rolling window the current trough/surge is measured against
    string? HistoryStart,
    string? AsOfDate,
    decimal? LastClose,
    int BarCount,
    ReboundCurrentDto? Current,
    ReboundBaseRateDto? BaseRate,
    IReadOnlyList<ReboundEpisodeDto> Episodes);

/// <summary>An ETF and its member symbols — drives the ETF pill row and, once
/// selected, the member-symbol pill row on the Rebound tab.</summary>
public sealed record EtfGroupDto(string Etf, string Description, IReadOnlyList<string> Members);
