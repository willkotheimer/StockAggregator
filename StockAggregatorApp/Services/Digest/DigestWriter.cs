using System.Globalization;
using StockAggregatorApp.Services.Projection;

namespace StockAggregatorApp.Services.Digest;

/// <summary>One paragraph: a single signal, told in a few short sentences.</summary>
public sealed record DigestItem(IReadOnlyList<string> Sentences);

public sealed record DigestSection(string Heading, IReadOnlyList<DigestItem> Items);

/// <summary>How the pipeline behind today's email is doing. Shown to the operator only.</summary>
public sealed record DataHealth(
    IReadOnlyList<string> SnapshotLabelsToday,
    DateTime? LastRollupUtc,
    string? LastRollupStatus,
    DateTime? HistoryThrough,
    int? HistoryDaysBehind);

public sealed record DigestContent(
    string Subject,
    string Title,
    string Dateline,
    IReadOnlyList<DigestSection> Sections,
    IReadOnlyList<string> Disclaimer,
    string DataCheck);

/// <summary>
/// Turns signals into the email's words. Every sentence comes from a fixed template,
/// so the numbers in the email are always the computed numbers and the wording never
/// drifts into advice. The rules the templates must follow — short sentences, no
/// trading jargon, no advice words — are enforced by DigestWritingRulesTests.
/// </summary>
public static class DigestWriter
{
    public const string Title = "Midday Market Notes";

    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    public static readonly IReadOnlyList<string> Disclaimer =
    [
        "This email shows what prices did. It is not financial advice.",
        "Past patterns do not promise future results.",
        "Prices come from Yahoo Finance and can be a few minutes late.",
    ];

    public static DigestContent Write(DigestSignals s, DataHealth health, ProjectionScorecard? scorecard = null)
    {
        var sections = new List<DigestSection>();

        var moving = new List<DigestItem>();
        if (s.Strongest is { } top)
        {
            moving.Add(Item(
                $"{Cap(DigestNames.GroupOf(top.Etf))} are the strongest group today.",
                $"Their fund ({top.Etf}) is {Move(top.ChangePct)}."));
        }
        if (s.Weakest is { } bottom)
        {
            moving.Add(Item(
                $"{Cap(DigestNames.GroupOf(bottom.Etf))} are the weakest.",
                $"Their fund ({bottom.Etf}) is {Move(bottom.ChangePct)}."));
        }

        var unusual = new List<DigestItem>();
        foreach (var u in s.Unusual)
        {
            unusual.Add(Item(
                $"{Cap(DigestNames.Of(u.Symbol))} is {Move(u.ChangePct)} today.",
                $"On a normal day it moves about {Pct(u.TypicalMovePct)}."));
        }
        foreach (var t in s.TurnArounds)
        {
            var earlier = t.EarlierPct > 0 ? "up" : "down";
            unusual.Add(Item(
                $"{Cap(DigestNames.Of(t.Symbol))} was {earlier} as much as {Pct(Math.Abs(t.EarlierPct))} earlier today.",
                Math.Round(t.NowPct, 1) == 0m ? "It is now flat." : $"It is now {Move(t.NowPct)}."));
        }

        var closer = new List<DigestItem>();
        foreach (var d in s.FundDivergences)
        {
            var sentences = new List<string>
            {
                $"{Cap(DigestNames.Of(d.Etf))} is {Move(d.EtfChangePct)}.",
                $"But {d.MembersFlatOrDown} of its {d.MembersTracked} stocks are flat or down.",
            };
            if (d.TopSymbol is { } topSymbol && d.TopChangePct is > 0m)
            {
                sentences.Add($"The biggest gain is from {DigestNames.Of(topSymbol)}, {Move(d.TopChangePct.Value)}.");
            }
            closer.Add(new DigestItem(sentences));
        }

        var month = new List<DigestItem>();
        foreach (var e in s.MonthExtremes)
        {
            month.Add(e.IsHigh
                ? Item($"{Cap(DigestNames.Of(e.Symbol))} is above its highest price of the past month.",
                       $"It is at {Money(e.Price)}. The old high was {Money(e.PreviousExtreme)}.")
                : Item($"{Cap(DigestNames.Of(e.Symbol))} is below its lowest price of the past month.",
                       $"It is at {Money(e.Price)}. The old low was {Money(e.PreviousExtreme)}."));
        }
        foreach (var r in s.RangeEdges)
        {
            month.Add(Item(
                $"{Cap(DigestNames.Of(r.Symbol))} has moved between {Money(r.Low)} and {Money(r.High)} for a month.",
                $"Today it is near the {(r.NearTop ? "top" : "bottom")} of that range."));
        }

        var before = new List<DigestItem>();
        foreach (var f in s.FarFromHighs)
        {
            var sentences = new List<string>
            {
                $"{Cap(DigestNames.Of(f.Symbol))} is now {Pct(f.BelowHighPct)} below its high of the past 10 months.",
            };
            var since = f.HistoryStart.ToString("MMMM yyyy", En);
            if (f.BaseRate is not { } rate)
            {
                sentences.Add($"Since {since}, it has not fallen this far before.");
            }
            else if (rate.EpisodeCount == 1)
            {
                sentences.Add($"Since {since}, it fell this far once.");
                sentences.Add($"It took {Days(rate.MedianReversalDays)} to get back to its old high.");
            }
            else
            {
                sentences.Add($"Since {since}, it fell this far {rate.EpisodeCount} times.");
                sentences.Add($"From the low point, it took {rate.MinReversalDays} to {Days(rate.MaxReversalDays)} to get back to its old high.");
                sentences.Add($"Usually it took about {Days(rate.MedianReversalDays)}.");
            }
            before.Add(new DigestItem(sentences));
        }

        if (unusual.Count + closer.Count + month.Count + before.Count == 0)
        {
            moving.Add(Item("Nothing else stands out so far today."));
        }

        Add(sections, "What is moving today", moving);
        Add(sections, "Moving more than normal", unusual);
        Add(sections, "Look closer", closer);
        Add(sections, "The past month", month);
        Add(sections, "Down from a recent high", before);
        if (scorecard is not null)
        {
            Add(sections, "How the price guesses are doing", Guesses(scorecard));
            if (scorecard.FollowOn is { } followOn)
            {
                var items = FollowOn(followOn);
                if (scorecard.FollowOnRelative is { } relative)
                {
                    items.AddRange(FollowOnRelative(relative));
                }
                Add(sections, "When one fund moves, what others do next", items);
            }
        }

        return new DigestContent(
            Subject: Subject(s),
            Title: Title,
            Dateline: Dateline(s),
            Sections: sections,
            Disclaimer: Disclaimer,
            DataCheck: DataCheck(health));
    }

    /// <summary>Top patterns listed in the email's readout.</summary>
    public const int TopPatterns = 5;

    /// <summary>
    /// The projection test's readout: overall accuracy and its direction, the best
    /// patterns, and how quarter ends and earnings reports affect the guesses.
    /// </summary>
    private static List<DigestItem> Guesses(ProjectionScorecard card)
    {
        var items = new List<DigestItem>();
        if (card.Recent.HitRate is not { } recent || card.Recent.BaselineHitRate is not { } recentBase)
        {
            return items;
        }

        items.Add(Item(
            $"Each day we guess where prices will be {card.HorizonDays} trading days later, then check.",
            "A guess lands when the price ends within a normal month's move of it."));

        var sentences = new List<string>
        {
            $"In the last 20 trading days, {Whole(recent)} of guesses landed.",
            $"Guessing with no pattern landed {Whole(recentBase)}.",
        };
        if (card.PriorRecent.HitRate is { } prior)
        {
            var (now, then) = (Math.Round(recent * 100), Math.Round(prior * 100));
            sentences.Add(now == then
                ? "That is the same as the 20 days before."
                : $"That is {(now > then ? "up" : "down")} from {Whole(prior)} the 20 days before.");
        }
        items.Add(new DigestItem(sentences));

        var top = card.Patterns.Take(TopPatterns).ToList();
        if (top.Count > 0)
        {
            items.Add(Item("The best patterns so far:"));
            foreach (var p in top)
            {
                items.Add(Item(
                    $"{p.Label}.",
                    $"{Whole(p.All.HitRate ?? 0)} landed, vs {Whole(p.All.BaselineHitRate ?? 0)} with no pattern, over {p.All.Tests.ToString("N0", En)} tests."));
            }
        }

        var passed = card.Patterns.Count(p => p.Passed);
        items.Add(Item(
            passed switch
            {
                0 => "No pattern has passed the full test yet.",
                1 => "1 pattern has passed the full test.",
                _ => $"{passed} patterns have passed the full test.",
            },
            "Passing means beating no pattern by 10 points, in both the older and the newer years."));

        if (card.QuarterEnd.With.HitRate is { } qWith && card.QuarterEnd.Without.HitRate is { } qWithout)
        {
            items.Add(Item($"Guesses that crossed a quarter end landed {Whole(qWith)}. Others landed {Whole(qWithout)}."));
        }
        if (card.Earnings.With.HitRate is { } eWith && card.Earnings.Without.HitRate is { } eWithout)
        {
            items.Add(Item($"Guesses that crossed an earnings report landed {Whole(eWith)}. Others landed {Whole(eWithout)}."));
        }

        return items;
    }

    // Caps for the follow-on readout, so it stays a few lines.
    public const int TopLinks = 3;
    public const int TopOpen = 3;

    /// <summary>
    /// The follow-on test's readout: what a big day is, how the guesses are doing per
    /// span, the strongest links, and the guesses still waiting on their outcome.
    /// </summary>
    private static List<DigestItem> FollowOn(FollowOnScorecard card)
    {
        var items = new List<DigestItem>
        {
            Item(
                "When a fund has a big up or down day, we check what other funds do next.",
                $"A big day is a move at least {Times(card.BigDayZ)} the fund's usual daily swing.",
                "If a fund often had its own big day soon after, we guess it will again."),
        };

        if (card.FirstGuess is not { } first || card.All.HitRate is not { } all || card.All.BaselineHitRate is not { } allBase)
        {
            items.Add(Item("There is not enough history to make these guesses yet."));
            return items;
        }

        var progress = new List<string>
        {
            $"Since {first.ToString("MMMM yyyy", En)}, we made {card.All.Tests.ToString("N0", En)} of these guesses.",
            $"{Whole(all)} came true. With no pattern, we would expect {Whole(allBase)}.",
        };
        if (card.Recent.HitRate is { } recent && card.Recent.BaselineHitRate is { } recentBase)
        {
            progress.Add($"In the last 20 trading days, {Whole(recent)} came true, vs {Whole(recentBase)} with no pattern.");
        }
        items.Add(new DigestItem(progress));

        foreach (var w in card.Windows.Where(w => w.All.Tests > 0))
        {
            items.Add(Item(
                $"Within {w.Days} days: {Whole(w.All.HitRate ?? 0)} came true, vs {Whole(w.All.BaselineHitRate ?? 0)} with no pattern.",
                $"That is over {w.All.Tests.ToString("N0", En)} guesses."));
        }

        var passed = card.Windows.Where(w => w.Passed).Select(w => $"{w.Days}-day").ToList();
        items.Add(Item(
            passed.Count == 0
                ? "No span of days has passed the full test yet."
                : $"The {string.Join(" and ", passed)} guesses have passed the full test.",
            "Passing means beating no pattern by 10 points, in both the older and the newer years."));

        // One line per leader and follower: the span with the biggest lift.
        var links = card.Links
            .GroupBy(l => (l.Leader, l.LeaderUp, l.Follower, l.FollowerUp))
            .Select(g => g.First())
            .Take(TopLinks)
            .ToList();
        if (links.Count > 0)
        {
            items.Add(Item("The strongest links so far:"));
            foreach (var l in links)
            {
                items.Add(Item(
                    $"{Cap(DigestNames.Of(l.Leader))} had {l.Events} big {UpDown(l.LeaderUp)} days.",
                    $"{Cap(DigestNames.Of(l.Follower))} had a big {UpDown(l.FollowerUp)} day within {l.Days} days after {l.Hits} of them.",
                    $"That is {Whole(l.Rate)}, vs {Whole(l.BaseRate)} in any {l.Days} days."));
            }
        }

        var open = card.Open
            .GroupBy(o => o.Follower)
            .Select(g => g.First())
            .Take(TopOpen)
            .ToList();
        if (open.Count > 0)
        {
            items.Add(Item("Guesses still open:"));
            foreach (var o in open)
            {
                items.Add(Item(
                    $"{Cap(DigestNames.Of(o.Leader))} had a big {UpDown(o.LeaderUp)} day on {o.Origin.ToString("MMM d", En)}.",
                    $"Next we watch {DigestNames.Of(o.Follower)} for a big {UpDown(o.FollowerUp)} day.",
                    $"After past days like that, it had one within {o.Days} days {Whole(o.Rate)} of the time.",
                    $"In any {o.Days} days, that happens {Whole(o.BaseRate)} of the time.",
                    o.AlreadyHappened
                        ? "It has already happened this time."
                        : o.DaysLeft == 1 ? "1 trading day is left." : $"{o.DaysLeft} trading days are left."));
            }
        }

        return items;
    }

    /// <summary>Links shown from the relative measure.</summary>
    public const int TopRelativeLinks = 2;

    /// <summary>
    /// The same test with each fund measured against the stock funds' average, so a day
    /// when everything moves together doesn't count: progress, and the strongest links.
    /// </summary>
    private static List<DigestItem> FollowOnRelative(FollowOnScorecard card)
    {
        var items = new List<DigestItem>
        {
            Item(
                "We also check each fund against the average of the stock funds.",
                "That way, a day when everything moves together does not count."),
        };

        if (card.All.HitRate is not { } all || card.All.BaselineHitRate is not { } allBase)
        {
            items.Add(Item("Measured that way, there are no guesses yet."));
            return items;
        }

        var sentences = new List<string>
        {
            $"Measured that way, {Whole(all)} of {card.All.Tests.ToString("N0", En)} guesses came true, vs {Whole(allBase)} with no pattern.",
        };
        var passed = card.Windows.Where(w => w.Passed).Select(w => $"{w.Days}-day").ToList();
        sentences.Add(passed.Count == 0
            ? "None of these has passed the full test yet."
            : $"The {string.Join(" and ", passed)} guesses have passed the full test.");
        items.Add(new DigestItem(sentences));

        var links = card.Links
            .GroupBy(l => (l.Leader, l.LeaderUp, l.Follower, l.FollowerUp))
            .Select(g => g.First())
            .Take(TopRelativeLinks)
            .ToList();
        foreach (var l in links)
        {
            items.Add(Item(
                $"{Cap(DigestNames.Of(l.Leader))} {(l.LeaderUp ? "pulled sharply ahead of" : "fell sharply behind")} the market {l.Events} times.",
                $"{Cap(DigestNames.Of(l.Follower))} {(l.FollowerUp ? "pulled ahead of" : "fell behind")} the market within {l.Days} days after {l.Hits} of them.",
                $"That is {Whole(l.Rate)}, vs {Whole(l.BaseRate)} in any {l.Days} days."));
        }

        return items;
    }

    private static string UpDown(bool up) => up ? "up" : "down";

    /// <summary>2 -> "twice"; 2.5 -> "2.5 times".</summary>
    private static string Times(double z) => z == 2 ? "twice" : $"{z.ToString("0.#", En)} times";

    /// <summary>0.4718 -> "47%".</summary>
    private static string Whole(double rate) => Math.Round(rate * 100, MidpointRounding.AwayFromZero).ToString("0", En) + "%";

    /// <summary>The digest for a day with no prices yet: still says plainly why it's empty.</summary>
    public static DigestContent WriteEmpty(DateTime tradingDate, DataHealth health) => new(
        Subject: $"Midday {tradingDate.ToString("dddd, MMM d", En)}: no prices yet",
        Title: Title,
        Dateline: tradingDate.ToString("dddd, MMMM d", En),
        Sections: [new DigestSection("What is moving today", [Item("No prices were saved today, so there is nothing to show yet.")])],
        Disclaimer: Disclaimer,
        DataCheck: DataCheck(health));

    public static string Subject(DigestSignals s)
    {
        var day = s.TradingDate.ToString("dddd, MMM d", En);
        if (s.Strongest is not { } top)
        {
            return $"Midday {day}: market notes";
        }

        var subject = $"Midday {day}: {DigestNames.GroupOf(top.Etf)} lead, {Move(top.ChangePct)}";
        var highs = s.MonthExtremes.Count(e => e.IsHigh);
        if (highs > 0)
        {
            subject += highs == 1 ? ", 1 stock at a 1-month high" : $", {highs} stocks at a 1-month high";
        }

        return subject;
    }

    private static string Dateline(DigestSignals s)
    {
        var at = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(s.PricesAtUtc, DateTimeKind.Utc), Central);
        return $"{s.TradingDate.ToString("dddd, MMMM d", En)} · prices at {at.ToString("h:mm tt", En)} CT";
    }

    private static string DataCheck(DataHealth h)
    {
        var parts = new List<string>();

        var labels = h.SnapshotLabelsToday;
        parts.Add(labels.Count == 0
            ? "No price checks saved today."
            : $"{labels.Count} price {(labels.Count == 1 ? "check" : "checks")} saved today ({string.Join(", ", labels.Select(l => l.Replace(" CT", "")))} CT).");

        parts.Add(h.LastRollupUtc is { } ran
            ? $"Last nightly summary: {h.LastRollupStatus ?? "unknown"}, {TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ran, DateTimeKind.Utc), Central).ToString("ddd h:mm tt", En)} CT."
            : "No nightly summary found.");

        if (h.HistoryThrough is { } through)
        {
            var behind = h.HistoryDaysBehind ?? 0;
            parts.Add(behind > 5
                ? $"Daily history ends {through.ToString("MMM d", En)}, {behind} trading days back. Run the backfill."
                : $"Daily history ends {through.ToString("MMM d", En)}.");
        }

        return "Data check: " + string.Join(" ", parts);
    }

    private static void Add(List<DigestSection> sections, string heading, List<DigestItem> items)
    {
        if (items.Count > 0)
        {
            sections.Add(new DigestSection(heading, items));
        }
    }

    private static DigestItem Item(params string[] sentences) => new(sentences);

    /// <summary>"up 1.2%" / "down 0.9%" / "flat".</summary>
    public static string Move(decimal pct)
    {
        var rounded = Math.Round(pct, 1, MidpointRounding.AwayFromZero);
        return rounded switch
        {
            > 0m => $"up {Pct(rounded)}",
            < 0m => $"down {Pct(-rounded)}",
            _ => "flat",
        };
    }

    public static string Pct(decimal pct) =>
        Math.Round(pct, 1, MidpointRounding.AwayFromZero).ToString("0.0", En) + "%";

    public static string Money(decimal price) =>
        price >= 1000m ? price.ToString("$#,##0", En) : price.ToString("$0.00", En);

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    private static string Cap(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
