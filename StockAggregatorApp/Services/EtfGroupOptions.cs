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

/// <summary>
/// Funds outside the groups (bound from the "RotationDestinations" config array) where
/// money leaving stocks can go: defensives, bonds, gold. Read only by the follow-on test;
/// the email and the dashboard leave them out, so they never change what readers see.
/// They still need to be in the Functions app's StockSymbols to get daily prices.
/// </summary>
public sealed class RotationDestinationOptions
{
    public const string SectionName = "RotationDestinations";

    public List<string> Symbols { get; set; } = new();

    public bool Contains(string symbol) => Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase);
}
