namespace StockAggregatorApp.Services.Projection;

/// <summary>
/// Fills in earnings dates older than the data we have. The source (Nasdaq) gives only
/// each company's last four reports, about a year, while the projection test runs over
/// five. Companies report in nearly the same week every year, so each known date is
/// carried back in steps of 52 weeks (which keeps the weekday). The few days of drift
/// that builds up over the years is small next to a 20-trading-day window.
/// </summary>
public static class EarningsCalendar
{
    private const int WeeksPerYear = 52;

    /// <summary>
    /// Reported dates plus estimates back to <paramref name="earliestNeeded"/>, sorted.
    /// Estimates are only added before the earliest reported date, so they never sit
    /// beside a real report from the same quarter.
    /// </summary>
    public static IReadOnlyList<DateTime> WithEstimates(IEnumerable<DateTime> reported, DateTime earliestNeeded)
    {
        var known = reported.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
        if (known.Count == 0) return known;

        var firstKnown = known[0];
        var all = new SortedSet<DateTime>(known);
        foreach (var date in known)
        {
            for (var shifted = date.AddDays(-7 * WeeksPerYear); shifted >= earliestNeeded; shifted = shifted.AddDays(-7 * WeeksPerYear))
            {
                // Keep clear of the real reports: an estimate within a month of one is the same quarter.
                if (shifted < firstKnown.AddDays(-30)) all.Add(shifted);
            }
        }

        return all.ToList();
    }
}
