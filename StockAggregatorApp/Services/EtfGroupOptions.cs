namespace StockAggregatorApp.Services;

/// <summary>One ETF and the member symbols shown beneath it, in display order.</summary>
public sealed class EtfGroup
{
    public string Etf { get; set; } = string.Empty;

    /// <summary>Short label of what the ETF holds, e.g. "Semiconductors".</summary>
    public string Description { get; set; } = string.Empty;

    public List<string> Members { get; set; } = new();
}

/// <summary>
/// Ordered ETF groupings (bound from the "EtfGroups" config array). Drives the
/// grouped layout: each ETF row is followed by its member symbols. There is no
/// grouping in the quote data itself, so it comes from config.
/// </summary>
public sealed class EtfGroupOptions
{
    public const string SectionName = "EtfGroups";

    public List<EtfGroup> Groups { get; set; } = new();
}
