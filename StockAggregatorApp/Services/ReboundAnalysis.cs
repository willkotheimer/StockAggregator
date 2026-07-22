using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

/// <summary>Trough = drawdown-then-recovery (when does it go back up).
/// Surge = run-up-then-pullback (when does it go back down).</summary>
public enum ReboundMode
{
    Trough,
    Surge,
}

/// <summary>
/// Pure, dependency-free episode detection over a daily-close series. Kept
/// separate from the query service so it can be unit-tested and reasoned about
/// on its own. Both modes use one scan with the comparisons flipped:
///
///   Trough: anchor = running peak; an episode opens when close falls
///           >= threshold below it, tracks the lowest close (the trough), and
///           "reverses" (recovers) when close regains the peak.
///   Surge:  anchor = running low; an episode opens when close rises
///           >= threshold above it, tracks the highest close (the peak), and
///           "reverses" (pulls back) when close returns to the low.
///
/// Move %, and the recovery/pullback framing, are always expressed as positive
/// magnitudes so the UI treats the two modes symmetrically.
/// </summary>
public static class ReboundAnalysis
{
    public sealed record Episode(
        DateTime AnchorDate,
        decimal AnchorPrice,
        DateTime ExtremeDate,
        decimal ExtremePrice,
        DateTime? ReversalDate,
        bool Reversed,
        decimal MovePct,
        int AnchorToExtremeDays,
        int? ExtremeToReversalDays,
        int? AnchorToReversalDays);

    public sealed record ScanResult(IReadOnlyList<Episode> Completed, Episode? Current);

    public static ScanResult Scan(IReadOnlyList<DailyClose> bars, decimal thresholdPct, ReboundMode mode)
    {
        var completed = new List<Episode>();
        if (bars.Count == 0)
        {
            return new ScanResult(completed, null);
        }

        var trough = mode == ReboundMode.Trough;
        var t = thresholdPct / 100m;

        var anchorPrice = bars[0].Close;
        var anchorDate = bars[0].TradingDate;
        var inEpisode = false;
        var extremePrice = anchorPrice;
        var extremeDate = anchorDate;

        // In-anchor-direction new extreme (higher peak for trough, lower low for surge).
        bool ExtendsAnchor(decimal c) => trough ? c > anchorPrice : c < anchorPrice;
        // Move away from the anchor by at least the threshold — opens an episode.
        bool Triggers(decimal c) => trough ? c <= anchorPrice * (1 - t) : c >= anchorPrice * (1 + t);
        // Deeper trough / higher peak while inside an episode.
        bool MoreExtreme(decimal c) => trough ? c < extremePrice : c > extremePrice;
        // Back to the anchor level — the recovery (trough) / pullback (surge).
        bool Reverses(decimal c) => trough ? c >= anchorPrice : c <= anchorPrice;

        for (var i = 1; i < bars.Count; i++)
        {
            var (date, c) = (bars[i].TradingDate, bars[i].Close);

            if (!inEpisode)
            {
                if (ExtendsAnchor(c))
                {
                    anchorPrice = c;
                    anchorDate = date;
                }
                else if (Triggers(c))
                {
                    inEpisode = true;
                    extremePrice = c;
                    extremeDate = date;
                }

                continue;
            }

            if (MoreExtreme(c))
            {
                extremePrice = c;
                extremeDate = date;
            }

            if (Reverses(c))
            {
                completed.Add(BuildEpisode(anchorDate, anchorPrice, extremeDate, extremePrice, date, trough));
                inEpisode = false;
                anchorPrice = c;
                anchorDate = date;
            }
        }

        Episode? current = inEpisode
            ? BuildEpisode(anchorDate, anchorPrice, extremeDate, extremePrice, null, trough)
            : null;

        return new ScanResult(completed, current);
    }

    private static Episode BuildEpisode(
        DateTime anchorDate,
        decimal anchorPrice,
        DateTime extremeDate,
        decimal extremePrice,
        DateTime? reversalDate,
        bool trough)
    {
        var movePct = anchorPrice == 0m
            ? 0m
            : Math.Round((trough ? anchorPrice - extremePrice : extremePrice - anchorPrice) / anchorPrice * 100m, 2);

        return new Episode(
            AnchorDate: anchorDate,
            AnchorPrice: anchorPrice,
            ExtremeDate: extremeDate,
            ExtremePrice: extremePrice,
            ReversalDate: reversalDate,
            Reversed: reversalDate.HasValue,
            MovePct: movePct,
            AnchorToExtremeDays: (extremeDate - anchorDate).Days,
            ExtremeToReversalDays: reversalDate is { } r1 ? (r1 - extremeDate).Days : null,
            AnchorToReversalDays: reversalDate is { } r2 ? (r2 - anchorDate).Days : null);
    }
}
