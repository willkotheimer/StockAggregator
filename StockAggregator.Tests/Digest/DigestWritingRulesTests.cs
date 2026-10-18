using System.Text.RegularExpressions;
using StockAggregatorApp.Models;
using StockAggregatorApp.Services.Digest;
using Xunit;

namespace StockAggregator.Tests.Digest;

/// <summary>
/// The digest is written in plain English for readers who don't trade. These rules
/// keep it that way as templates are added: every sentence a reader can see is
/// checked for length, trading jargon, and advice words. A template that breaks a
/// rule fails here, not in someone's inbox.
/// </summary>
public class DigestWritingRulesTests
{
    private const int MaxWordsPerSentence = 20;

    // Jargon from the dashboard, and words that turn a description into advice.
    private static readonly string[] Banned =
    [
        "breakout", "break out", "channel", "rotation", "breadth", "gap", "momentum", "reversion",
        "drawdown", "correlation", "bullish", "bearish", "median", "base rate", "volatil", "trough",
        "surge", "rally", "signal", "ETF", "ticker", "R²",
        "buy", "sell", "should", "opportunit", "recommend", "you must", "guarantee",
    ];

    private static readonly DateTime Day = new(2026, 10, 16);

    /// <summary>A digest with every kind of signal, so every template is exercised.</summary>
    private static DigestContent Everything() => DigestWriter.Write(
        new DigestSignals(
            Day,
            Day.AddHours(16).AddMinutes(32),
            Strongest: new GroupMove("SMH", 1.23m),
            Weakest: new GroupMove("XLE", -0.94m),
            Unusual: [new UnusualMove("JNJ", -2.04m, 0.81m, 2.5m), new UnusualMove("MU", 4.1m, 1.9m, 2.2m)],
            TurnArounds: [new TurnAround("NVDA", 2.1m, -0.4m), new TurnAround("BA", -2.5m, 0.3m), new TurnAround("GE", 1.8m, 0.01m)],
            FundDivergences:
            [
                new FundDivergence("SMH", 1.1m, 7, 5, "NVDA", 3.2m),
                new FundDivergence("VEU", 0.6m, 6, 6, "ASML", -0.1m),
            ],
            MonthExtremes: [new MonthExtreme("JNJ", true, 182.4m, 180.1m), new MonthExtreme("BA", false, 151.2m, 153.9m)],
            RangeEdges: [new RangeEdge("KO", false, 61m, 66m, 61.2m), new RangeEdge("BRK-B", true, 1180m, 1260m, 1255m)],
            FarFromHighs:
            [
                new FarFromHigh("MU", 11.2m, new DateTime(2024, 5, 1), new ReboundBaseRateDto(8m, 6, 34, 12, 80, 90, 5, 180, 6)),
                new FarFromHigh("AMD", 10.4m, new DateTime(2024, 5, 1), new ReboundBaseRateDto(7m, 1, 21, 21, 21, 90, 1, 180, 1)),
                new FarFromHigh("TSLA", 10.1m, new DateTime(2024, 5, 1), null),
            ]),
        new DataHealth(["08:30 CT", "11:00 CT", "11:30 CT"], Day.AddHours(-2), "Success", Day.AddDays(-14), 9));

    private static DigestContent Quiet() => DigestWriter.Write(
        new DigestSignals(Day, Day.AddHours(16), new GroupMove("XLK", 0.3m), new GroupMove("XLE", -0.2m), [], [], [], [], [], []),
        new DataHealth([], null, null, null, null));

    /// <summary>Everything a reader sees, one sentence at a time. The data check is operator-only, so it's left out.</summary>
    private static IEnumerable<string> ReaderSentences(DigestContent c) =>
        new[] { c.Subject, c.Title }
            .Concat(c.Sections.Select(s => s.Heading))
            .Concat(c.Sections.SelectMany(s => s.Items).SelectMany(i => i.Sentences))
            .Concat(c.Disclaimer)
            .SelectMany(text => Regex.Split(text, @"(?<=[.!?])\s+"))
            .Where(s => s.Length > 0);

    public static IEnumerable<object[]> Digests() => [[nameof(Everything)], [nameof(Quiet)]];

    private static DigestContent Build(string name) => name == nameof(Quiet) ? Quiet() : Everything();

    [Theory]
    [MemberData(nameof(Digests))]
    public void EverySentenceIsShort(string digest)
    {
        foreach (var sentence in ReaderSentences(Build(digest)))
        {
            var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(words <= MaxWordsPerSentence, $"{words} words: \"{sentence}\"");
        }
    }

    [Theory]
    [MemberData(nameof(Digests))]
    public void NoJargonOrAdviceWords(string digest)
    {
        foreach (var sentence in ReaderSentences(Build(digest)))
        {
            foreach (var word in Banned)
            {
                var pattern = @"\b" + Regex.Escape(word);
                Assert.False(Regex.IsMatch(sentence, pattern, RegexOptions.IgnoreCase), $"\"{word}\" in: \"{sentence}\"");
            }
        }
    }

    [Fact]
    public void EveryTemplateIsUsed()
    {
        // Guards the two tests above: if a template stops appearing, they'd pass vacuously.
        var text = string.Join(" ", ReaderSentences(Everything()));

        Assert.Contains("strongest group", text);
        Assert.Contains("On a normal day", text);
        Assert.Contains("as much as", text);
        Assert.Contains("flat or down", text);
        Assert.Contains("highest price of the past month", text);
        Assert.Contains("lowest price of the past month", text);
        Assert.Contains("near the bottom of that range", text);
        Assert.Contains("fell this far 6 times", text);
        Assert.Contains("fell this far once", text);
        Assert.Contains("has not fallen this far", text);
    }

    [Fact]
    public void CompaniesAreNamedNotJustTickers()
    {
        var text = string.Join(" ", ReaderSentences(Everything()));

        Assert.Contains("Micron (MU)", text);
        Assert.Contains("The chip stock fund (SMH)", text);
        Assert.Contains("Chip stocks are the strongest group today.", text);
    }

    [Fact]
    public void NumbersAreTheComputedNumbers()
    {
        var text = string.Join(" ", ReaderSentences(Everything()));

        Assert.Contains("Johnson & Johnson (JNJ) is down 2.0% today.", text);
        Assert.Contains("On a normal day it moves about 0.8%.", text);
        Assert.Contains("It is at $182.40. The old high was $180.10.", text);
        Assert.Contains("between $1,180 and $1,260", text);
        Assert.Contains("it took 12 to 80 days", text);
        Assert.Contains("Usually it took about 34 days.", text);
        Assert.Contains("It is now flat.", text);
    }

    [Fact]
    public void QuietDay_SaysSo()
    {
        Assert.Contains("Nothing else stands out so far today.", string.Join(" ", ReaderSentences(Quiet())));
    }

    [Fact]
    public void Subject_LeadsWithTheStrongestGroup()
    {
        Assert.Equal("Midday Friday, Oct 16: chip stocks lead, up 1.2%, 1 stock at a 1-month high", Everything().Subject);
    }

    [Fact]
    public void DataCheck_FlagsStaleHistory()
    {
        var check = Everything().DataCheck;

        Assert.Contains("3 price checks saved today (08:30, 11:00, 11:30 CT).", check);
        Assert.Contains("9 trading days back. Run the backfill.", check);
    }

    [Fact]
    public void Html_ColorsMovesAndOmitsDataCheckWhenAsked()
    {
        var html = DigestRenderer.Html(Everything(), "https://example.test/", includeDataCheck: false);

        Assert.Contains("<span style=\"color:#127a3e;font-weight:600;\">up 1.2%</span>", html);
        Assert.Contains("Johnson &amp; Johnson", html);
        Assert.DoesNotContain("Data check", html);
        Assert.Contains("Data check", DigestRenderer.Html(Everything(), "https://example.test/", includeDataCheck: true));
    }
}
