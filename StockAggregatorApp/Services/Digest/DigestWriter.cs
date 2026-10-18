using System.Globalization;

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

    public static DigestContent Write(DigestSignals s, DataHealth health)
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

        return new DigestContent(
            Subject: Subject(s),
            Title: Title,
            Dateline: Dateline(s),
            Sections: sections,
            Disclaimer: Disclaimer,
            DataCheck: DataCheck(health));
    }

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
