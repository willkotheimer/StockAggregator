using StockAggregatorApp.Services.Projection;
using Xunit;

namespace StockAggregator.Tests.Projection;

/// <summary>
/// The ledger's status rules decide what counts as a near miss across every thesis, so
/// their edges are pinned: a status that shifts quietly would rewrite the history.
/// </summary>
public class ThesisLedgerTests
{
    private static HitScore Score(int tests, double edge) => new(tests, 0.30 + edge, 0.30);

    [Theory]
    [InlineData(90, 0.12, 256, 0.096, false, ThesisStatus.Close)]        // the follow-on 5-day span on 2026-10-28
    [InlineData(90, 0.12, 256, 0.049, false, ThesisStatus.NoEdge)]       // one half under +5
    [InlineData(29, 0.30, 256, 0.20, false, ThesisStatus.TooFewTests)]   // strong, but a half too thin to judge
    [InlineData(6, -0.22, 105, 0.24, false, ThesisStatus.TooFewTests)]   // the 10-day span: thin older half
    [InlineData(500, 0.15, 500, 0.15, true, ThesisStatus.Passed)]
    [InlineData(186, 0.054, 239, 0.0, false, ThesisStatus.NoEdge)]       // best price pattern on 2026-10-28
    public void Status_FollowsTheFixedRules(int olderTests, double olderEdge, int newerTests, double newerEdge, bool passed, ThesisStatus expected)
    {
        var older = Score(olderTests, olderEdge);
        var newer = Score(newerTests, newerEdge);
        var all = new HitScore(
            olderTests + newerTests,
            (older.HitRate * olderTests + newer.HitRate * newerTests) / (olderTests + newerTests),
            0.30);

        Assert.Equal(expected, ThesisLedger.Status(all, older, newer, passed));
    }

    [Fact]
    public void Entries_CoverEveryPatternAndEverySpan_InOneShape()
    {
        var none = new HitScore(0, null, null);
        var split = new EventSplit(none, none);
        var day = new DateTime(2026, 10, 28);
        var card = new ProjectionScorecard(day, 20, day.AddYears(-1), none, none, split, split,
            [new PatternScore("month-high", "At a 1-month high", Score(200, 0.01), Score(100, 0.01), Score(100, 0.01), split, split, false)],
            new FollowOnScorecard(day, day.AddYears(-2), 2.0, day.AddYears(-1), none, none, none,
                [new FollowOnWindow(5, Score(346, 0.10), Score(90, 0.12), Score(256, 0.096), false)], [], []));

        var entries = ThesisLedger.Entries(card, codeVersion: "1.0.0+abc123");

        Assert.Equal(["price-pattern/month-high", "follow-on/5d"], entries.Select(e => $"{e.Thesis}/{e.Variant}"));
        Assert.Equal(["NoEdge", "Close"], entries.Select(e => e.Status));
        Assert.All(entries, e => Assert.Equal("1.0.0+abc123", e.CodeVersion));
    }
}
