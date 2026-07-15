using System.Text.Json.Serialization;

namespace StockAggregator.Models;

/// <summary>
/// One quote row as returned by Financial Modeling Prep. Property names are
/// matched case-insensitively during deserialization, so they line up with the
/// JSON fields regardless of casing.
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
}
