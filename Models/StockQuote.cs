using System.Text.Json.Serialization;

namespace StockAggregator.Models;

/// <summary>
/// One quote row. Populated by <see cref="Services.QuoteFetcher"/> from the Yahoo
/// Finance chart response (ChangesPercentage is computed from price vs. previous close).
/// </summary>
public class StockQuote
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("changesPercentage")]
    public decimal? ChangesPercentage { get; set; }

    [JsonPropertyName("volume")]
    public long? Volume { get; set; }

    /// <summary>Session high so far (Yahoo regularMarketDayHigh).</summary>
    public decimal? DayHigh { get; set; }

    /// <summary>Session low so far (Yahoo regularMarketDayLow).</summary>
    public decimal? DayLow { get; set; }

    /// <summary>Prior session close (Yahoo chartPreviousClose).</summary>
    public decimal? PreviousClose { get; set; }
}
