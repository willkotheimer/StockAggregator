using System.Reflection;

namespace StockAggregatorApp.Services.Projection;

/// <summary>
/// Where a variant stands against the pass bar. The rules are fixed here, before the
/// history accumulates, so "close" can't drift toward whatever happens to look good.
/// </summary>
public enum ThesisStatus
{
    /// <summary>Beat no pattern by the full bar in both halves.</summary>
    Passed,

    /// <summary>Not passed, but at least +5 points in both halves on 30+ tests each.</summary>
    Close,

    /// <summary>At least +5 points overall, but a half has too few tests to judge.</summary>
    TooFewTests,

    NoEdge,
}

/// <summary>
/// One variant of one thesis on one day: its score and status. Every thesis reports in
/// this one shape so wins and near misses from different ideas can be lined up later.
/// </summary>
public sealed record ThesisLedgerEntry(
    DateTime AsOf,
    string Thesis,
    string Variant,
    string Label,
    int Tests,
    double? HitRate,
    double? BaselineHitRate,
    double? Edge,
    int OlderTests,
    double? OlderEdge,
    int NewerTests,
    double? NewerEdge,
    string Status,
    string CodeVersion);

/// <summary>
/// Flattens a projection scorecard into ledger rows, one per scored variant. Stored once
/// a day (dbo.ThesisLedger), so a variant's history survives the variant being changed
/// or pruned. The individual guesses behind a row aren't stored: the tests are
/// deterministic, so the code version plus the price history rebuilds them.
/// </summary>
public static class ThesisLedger
{
    public const string PricePattern = "price-pattern";
    public const string FollowOn = "follow-on";
    public const string FollowOnRelative = "follow-on-relative";

    public const double CloseEdge = 0.05;
    private const int CloseHalfTests = 30;

    /// <summary>The build that produced the scores: version plus git commit, when the build stamped one.</summary>
    public static string CodeVersion { get; } =
        typeof(ThesisLedger).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static IReadOnlyList<ThesisLedgerEntry> Entries(ProjectionScorecard card, string? codeVersion = null)
    {
        var version = codeVersion ?? CodeVersion;
        var entries = card.Patterns
            .Select(p => Entry(card.AsOf, PricePattern, p.Id, p.Label, p.All, p.Older, p.Newer, p.Passed, version))
            .ToList();

        if (card.FollowOn is { } f)
        {
            entries.AddRange(f.Windows.Select(w => Entry(
                f.AsOf, FollowOn, $"{w.Days}d", $"Within {w.Days} days of another fund's big day",
                w.All, w.Older, w.Newer, w.Passed, version)));
        }
        if (card.FollowOnRelative is { } r)
        {
            entries.AddRange(r.Windows.Select(w => Entry(
                r.AsOf, FollowOnRelative, $"{w.Days}d", $"Within {w.Days} days of another fund's big day against the market",
                w.All, w.Older, w.Newer, w.Passed, version)));
        }

        return entries;
    }

    public static ThesisStatus Status(HitScore all, HitScore older, HitScore newer, bool passed)
    {
        if (passed) return ThesisStatus.Passed;
        if (older.Tests >= CloseHalfTests && newer.Tests >= CloseHalfTests
            && older.Edge >= CloseEdge && newer.Edge >= CloseEdge)
        {
            return ThesisStatus.Close;
        }
        if (all.Edge >= CloseEdge && (older.Tests < CloseHalfTests || newer.Tests < CloseHalfTests))
        {
            return ThesisStatus.TooFewTests;
        }
        return ThesisStatus.NoEdge;
    }

    private static ThesisLedgerEntry Entry(
        DateTime asOf, string thesis, string variant, string label,
        HitScore all, HitScore older, HitScore newer, bool passed, string version) =>
        new(asOf, thesis, variant, label,
            all.Tests, all.HitRate, all.BaselineHitRate, all.Edge,
            older.Tests, older.Edge, newer.Tests, newer.Edge,
            Status(all, older, newer, passed).ToString(), version);
}
