using System.Text.Json.Serialization;

namespace StockAggregatorApp.Models;

/// <summary>
/// How a symbol behaved across the selected dates. Serialized by name so the client
/// reads "Up"/"Channeling" rather than an ordinal; the converter is applied here
/// rather than globally so no other endpoint's payload shifts.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrendDirection>))]
public enum TrendDirection
{
    /// <summary>Not enough closes in the range to judge.</summary>
    Unknown,
    Up,
    Down,
    /// <summary>Oscillating without a decisive net move — the mean-reversion case.</summary>
    Channeling,
}

/// <summary>
/// Trend summary for one row over the selected dates.
/// <see cref="NetPct"/> is first close to last close; <see cref="BandPct"/> is the
/// low-to-high spread. A move only counts as Up/Down when it is both decisive in
/// absolute terms and large relative to the band — otherwise the endpoints just
/// happened to land near the extremes of a channel.
/// <see cref="PositionInBand"/> (0–100) is where the last close sits between the
/// range low and high: low values in a channel are stretched-to-the-downside
/// (reversion candidates), high values alongside a strong NetPct read as momentum.
/// </summary>
public sealed record TrendSummary(
    TrendDirection Direction,
    decimal? NetPct,
    decimal? BandPct,
    decimal? PositionInBand);

/// <summary>
/// One row of the range matrix: closes aligned to the response's Dates array.
/// <see cref="Closes"/> and <see cref="Changes"/> are positional — index i is the
/// date at Dates[i], null where the symbol has no bar that day. Changes are versus
/// the previous *selected* date, so a Fridays-only range yields week-over-week moves.
/// </summary>
public sealed record RangeMatrixRow(
    string Id,
    string Symbol,
    bool IsEtf,
    string GroupEtf,
    string? Description,
    IReadOnlyList<decimal?> Closes,
    IReadOnlyList<decimal?> Changes,
    TrendSummary Trend);

/// <summary>
/// The close-only matrix: one column per selected date, one row per symbol.
/// <c>DayOfWeek</c> is the weekday filter applied within the range (0=Sun..6=Sat),
/// or null for every trading day in it.
/// </summary>
public sealed record RangeMatrixResponse(
    string From,
    string To,
    int? DayOfWeek,
    IReadOnlyList<string> Dates,
    IReadOnlyList<RangeMatrixRow> Rows);
