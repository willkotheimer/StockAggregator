namespace StockAggregatorApp;

/// <summary>
/// Every query-string key read by a cached GET. The output-cache policy varies on
/// exactly this list, so a key missing from it makes the cache serve one response
/// for every value of that parameter — the endpoint silently stops responding to it.
///
/// This is asserted against the controllers' [FromQuery] parameters by
/// OutputCacheVaryTests, so adding a query parameter without listing it here fails
/// the build rather than shipping a filter that quietly does nothing.
/// </summary>
public static class CachedQueryKeys
{
    public static readonly string[] All =
    [
        "days",
        "dates",
        "date",
        "window",
        "symbols",
        "mode",
        "threshold",
        "pullback",
        "from",
        "to",
        "dow",
    ];
}
