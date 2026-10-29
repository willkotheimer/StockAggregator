using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services.Digest;

namespace StockAggregatorApp.Services.Projection;

/// <summary>How one span of days (5, 10 or 15) is doing: all guesses, each half, and whether it passed.</summary>
public sealed record FollowOnWindow(int Days, HitScore All, HitScore Older, HitScore Newer, bool Passed);

/// <summary>
/// One leader → follower link from the full tally: after <see cref="Events"/> big days for
/// the leader, the follower had its own big day within <see cref="Days"/> after
/// <see cref="Hits"/> of them. <see cref="BaseRate"/> is how often any span that long holds one.
/// <see cref="Guesses"/> is how the link did when it was used to guess, before its outcome was known.
/// </summary>
public sealed record FollowOnLink(
    string Leader,
    bool LeaderUp,
    string Follower,
    bool FollowerUp,
    int Days,
    int Events,
    int Hits,
    double Rate,
    double BaseRate,
    double PValue,
    HitScore Guesses);

/// <summary>A guess whose span hasn't finished yet: the leader's big day, and the follower's odds.</summary>
public sealed record FollowOnOpen(
    string Leader,
    bool LeaderUp,
    DateTime Origin,
    string Follower,
    bool FollowerUp,
    int Days,
    int DaysLeft,
    bool AlreadyHappened,
    int Events,
    int Hits,
    double Rate,
    double BaseRate);

public sealed record FollowOnScorecard(
    DateTime AsOf,
    DateTime? FirstGuess,
    double BigDayZ,
    DateTime SplitDate,
    HitScore All,
    // Guesses whose span ended in the last 20 trading days, and the 20 before that.
    HitScore Recent,
    HitScore PriorRecent,
    IReadOnlyList<FollowOnWindow> Windows,
    // Significant links on the full history, biggest lift over the base rate first.
    IReadOnlyList<FollowOnLink> Links,
    IReadOnlyList<FollowOnOpen> Open);

/// <summary>
/// The follow-on test: within 5, 10 or 15 trading days of a big up or down day in one
/// fund, how often does each other fund have a big day of its own — and does guessing
/// that it will beat knowing nothing?
///
/// A big day is the same bar for every fund: a daily move at least <see cref="BigDayZ"/>
/// standard deviations from that fund's own mean over the year before (p ≈ 0.05 under
/// a normal curve). Measuring in each fund's own deviations is what makes it equally
/// unusual for a calm fund and a wild one; a flat percent would be routine for one and
/// rare for the other.
///
/// The tally is a dictionary keyed by (leader, leader direction, follower, follower
/// direction, span), counting leader big days and how many were followed. Walking
/// forward one day at a time, a leader's big day only joins the tally once its span has
/// finished, so every guess is made from what was known that day. A link is used to
/// guess only when its rate beats the follower's base rate — how often ANY span that
/// long holds that big day — by a one-sided binomial test at <see cref="MaxPValue"/>.
/// Each guess is scored against the base rate it was made against, and passes on the
/// projection test's fixed bar: +10 points in both halves, on at least 100 guesses.
///
/// Leader big days are counted once per span: a second big day for the same fund and
/// direction before the first one's span has closed is part of the same episode.
/// </summary>
public static class FollowOnTest
{
    public static readonly IReadOnlyList<int> Windows = [5, 10, 15];

    /// <summary>Standard deviations from normal for a day to count as big. Same for every fund.</summary>
    public const double BigDayZ = 2.0;

    /// <summary>A year of daily moves before a day, for the fund's own normal.</summary>
    public const int VolatilityDays = ProjectionTest.NormalMoveDays;
    private const int MinVolatilitySamples = VolatilityDays / 2;

    /// <summary>Leader big days a link needs before it can be judged at all.</summary>
    public const int MinEvents = 20;

    /// <summary>How unlikely the link's rate must be by chance alone before it is used to guess.</summary>
    public const double MaxPValue = 0.01;

    private const int RecentDays = 20;
    private const int PassHalfTests = 30;
    public const int ListedLinks = 20;

    private readonly record struct LinkKey(string Leader, sbyte LeaderMove, string Follower, sbyte FollowerMove, int Days);
    private readonly record struct BaseKey(string Follower, sbyte Move, int Days);

    /// <summary>Leader big days seen, and how many were followed by the follower's big day.</summary>
    private sealed class Tally
    {
        public int Events;
        public int Hits;
        public double Rate => Events == 0 ? 0 : (double)Hits / Events;
    }

    internal sealed record Test(
        string Leader, sbyte LeaderMove, string Follower, sbyte FollowerMove, int Days,
        DateTime Origin, DateTime Target, bool Hit, double BaseRate);

    /// <summary>One fund's big days on the shared calendar, with prefix counts for O(1) span lookups.</summary>
    private sealed class FundDays
    {
        // Move[t]: +1 big up, -1 big down, 0 neither. The rest count days through t inclusive,
        // so the span (from, to] holds Known[to] - Known[from] judged days.
        public required sbyte[] Move;
        public required int[] Known;
        public required int[] Ups;
        public required int[] Downs;

        /// <summary>Every day in (from, from+days] was judged.</summary>
        public bool FullyKnown(int from, int days) => Known[from + days] - Known[from] == days;

        public bool Happened(int from, int days, sbyte move) => HappenedSoFar(from, from + days, move);

        /// <summary>A big day in the given direction somewhere in (from, to].</summary>
        public bool HappenedSoFar(int from, int to, sbyte move)
        {
            var counts = move > 0 ? Ups : Downs;
            return counts[to] - counts[from] > 0;
        }
    }

    public static FollowOnScorecard Run(ProjectionInput input)
    {
        var (tests, open, tallies, bases, calendar) = Sweep(input);
        return Score(tests, open, tallies, bases, calendar);
    }

    internal static List<Test> Tests(ProjectionInput input) => Sweep(input).Tests;

    /// <summary>The funds with a big day (either way) on <paramref name="date"/>. For tests.</summary>
    internal static List<string> BigDayFunds(ProjectionInput input, DateTime date)
    {
        var (funds, calendar, days) = Prepare(input);
        var at = calendar.IndexOf(date.Date);
        return at < 0 ? [] : funds.Where(f => days[f].Move[at] != 0).ToList();
    }

    /// <summary>The US-listed funds, the shared calendar of their trading days, and each fund's big days on it.</summary>
    private static (List<string> Funds, List<DateTime> Calendar, Dictionary<string, FundDays> Days) Prepare(ProjectionInput input)
    {
        var funds = input.Groups
            .Select(g => g.Etf.Trim().ToUpperInvariant())
            .Where(e => e.Length > 0 && DigestAnalysis.IsUsListing(e) && input.History.ContainsKey(e))
            .Distinct()
            .ToList();

        var calendar = funds
            .SelectMany(f => input.History[f].Select(b => b.TradingDate.Date))
            .Distinct()
            .Order()
            .ToList();
        var index = calendar.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var days = funds.ToDictionary(f => f, f => BigDays(input.History[f], index, calendar.Count));
        return (funds, calendar, days);
    }

    private static (List<Test> Tests, List<FollowOnOpen> Open, Dictionary<LinkKey, Tally> Tallies,
        Dictionary<BaseKey, Tally> Bases, IReadOnlyList<DateTime> Calendar) Sweep(ProjectionInput input)
    {
        var (funds, calendar, days) = Prepare(input);

        var tallies = new Dictionary<LinkKey, Tally>();
        var bases = new Dictionary<BaseKey, Tally>();
        // Per span: origin index -> the leader big days counted there (after the once-per-span rule).
        var counted = Windows.ToDictionary(w => w, _ => new Dictionary<int, List<(string Fund, sbyte Move)>>());
        var lastCounted = new Dictionary<(string Fund, sbyte Move, int Days), int>();
        var tests = new List<Test>();
        var open = new List<FollowOnOpen>();
        var n = calendar.Count;

        for (var t = 0; t < n; t++)
        {
            // 1. Every span that finished at today's close is now known: add it to the tallies.
            foreach (var w in Windows)
            {
                var s = t - w;
                if (s < 0) continue;

                foreach (var f in funds)
                {
                    var fd = days[f];
                    if (!fd.FullyKnown(s, w)) continue;
                    foreach (sbyte m in (sbyte[])[1, -1])
                    {
                        Add(bases, new BaseKey(f, m, w), fd.Happened(s, w, m));
                    }
                }

                if (!counted[w].TryGetValue(s, out var leaders)) continue;
                foreach (var (leader, lm) in leaders)
                {
                    foreach (var f in funds)
                    {
                        if (f == leader || !days[f].FullyKnown(s, w)) continue;
                        foreach (sbyte m in (sbyte[])[1, -1])
                        {
                            Add(tallies, new LinkKey(leader, lm, f, m, w), days[f].Happened(s, w, m));
                        }
                    }
                }
            }

            // 2. Today's big days: count them, and guess from links already shown to matter.
            foreach (var leader in funds)
            {
                var lm = days[leader].Move[t];
                if (lm == 0) continue;

                foreach (var w in Windows)
                {
                    if (lastCounted.TryGetValue((leader, lm, w), out var last) && t < last + w) continue;
                    lastCounted[(leader, lm, w)] = t;
                    if (!counted[w].TryGetValue(t, out var list)) counted[w][t] = list = [];
                    list.Add((leader, lm));

                    foreach (var f in funds)
                    {
                        if (f == leader) continue;
                        foreach (sbyte m in (sbyte[])[1, -1])
                        {
                            if (!tallies.TryGetValue(new LinkKey(leader, lm, f, m, w), out var link)
                                || !bases.TryGetValue(new BaseKey(f, m, w), out var baseline)
                                || !IsSignificant(link, baseline, out _))
                            {
                                continue;
                            }

                            if (t + w < n)
                            {
                                if (!days[f].FullyKnown(t, w)) continue;
                                tests.Add(new Test(leader, lm, f, m, w, calendar[t], calendar[t + w],
                                    days[f].Happened(t, w, m), baseline.Rate));
                            }
                            else
                            {
                                open.Add(new FollowOnOpen(leader, lm > 0, calendar[t], f, m > 0, w,
                                    DaysLeft: t + w - (n - 1),
                                    AlreadyHappened: days[f].HappenedSoFar(t, n - 1, m),
                                    link.Events, link.Hits, link.Rate, baseline.Rate));
                            }
                        }
                    }
                }
            }
        }

        return (tests, open, tallies, bases, calendar);
    }

    private static void Add<TKey>(Dictionary<TKey, Tally> tallies, TKey key, bool hit) where TKey : notnull
    {
        if (!tallies.TryGetValue(key, out var tally)) tallies[key] = tally = new Tally();
        tally.Events++;
        if (hit) tally.Hits++;
    }

    private static bool IsSignificant(Tally link, Tally baseline, out double pValue)
    {
        pValue = 1;
        if (link.Events < MinEvents || baseline.Events < MinEvents || link.Rate <= baseline.Rate) return false;
        pValue = BinomialUpperTail(link.Hits, link.Events, baseline.Rate);
        return pValue <= MaxPValue;
    }

    /// <summary>
    /// Each day's move judged against the fund's own year before: z = (r - mean) / sd of
    /// the prior <see cref="VolatilityDays"/> daily log returns. Mapped onto the shared
    /// calendar; days the fund has no bar (or not enough history) stay unjudged.
    /// </summary>
    private static FundDays BigDays(IReadOnlyList<DailyClose> series, Dictionary<DateTime, int> index, int n)
    {
        var move = new sbyte[n];
        var judged = new bool[n];
        var returns = new List<double>(series.Count);

        for (var i = 1; i < series.Count; i++)
        {
            var (from, to) = (series[i - 1].Close, series[i].Close);
            if (from <= 0m || to <= 0m) continue;
            var r = Math.Log((double)(to / from));

            var window = returns.Count > VolatilityDays ? returns.GetRange(returns.Count - VolatilityDays, VolatilityDays) : returns;
            if (window.Count >= MinVolatilitySamples)
            {
                var mean = window.Average();
                var sd = Math.Sqrt(window.Sum(x => (x - mean) * (x - mean)) / (window.Count - 1));
                if (sd > 0 && index.TryGetValue(series[i].TradingDate.Date, out var at))
                {
                    var z = (r - mean) / sd;
                    judged[at] = true;
                    move[at] = z >= BigDayZ ? (sbyte)1 : z <= -BigDayZ ? (sbyte)-1 : (sbyte)0;
                }
            }
            returns.Add(r);
        }

        var known = new int[n];
        var ups = new int[n];
        var downs = new int[n];
        for (var i = 0; i < n; i++)
        {
            known[i] = (i > 0 ? known[i - 1] : 0) + (judged[i] ? 1 : 0);
            ups[i] = (i > 0 ? ups[i - 1] : 0) + (move[i] > 0 ? 1 : 0);
            downs[i] = (i > 0 ? downs[i - 1] : 0) + (move[i] < 0 ? 1 : 0);
        }
        return new FundDays { Move = move, Known = known, Ups = ups, Downs = downs };
    }

    /// <summary>P(X ≥ k) for X ~ Binomial(n, p), summed in log space so long tallies don't underflow.</summary>
    public static double BinomialUpperTail(int k, int n, double p)
    {
        if (k <= 0) return 1;
        if (k > n || p <= 0) return 0;
        if (p >= 1) return 1;

        var (logP, logQ) = (Math.Log(p), Math.Log(1 - p));
        var logPmf = n * logQ; // P(X = 0)
        var tail = 0.0;
        for (var i = 0; i < n; i++)
        {
            if (i >= k) tail += Math.Exp(logPmf);
            logPmf += Math.Log((double)(n - i) / (i + 1)) + logP - logQ;
        }
        tail += Math.Exp(logPmf); // i = n
        return Math.Min(1, tail);
    }

    private static FollowOnScorecard Score(
        List<Test> tests,
        List<FollowOnOpen> open,
        Dictionary<LinkKey, Tally> tallies,
        Dictionary<BaseKey, Tally> bases,
        IReadOnlyList<DateTime> calendar)
    {
        var asOf = calendar.Count > 0 ? calendar[^1] : DateTime.MinValue;
        var first = tests.Count > 0 ? tests.Min(t => t.Origin) : (DateTime?)null;
        var last = tests.Count > 0 ? tests.Max(t => t.Origin) : DateTime.MinValue;
        var split = first is { } f ? f.AddTicks((last - f).Ticks / 2).Date : DateTime.MinValue;

        var windows = Windows.Select(w =>
        {
            var list = tests.Where(t => t.Days == w).ToList();
            var all = Hits(list);
            var older = Hits(list.Where(t => t.Origin < split));
            var newer = Hits(list.Where(t => t.Origin >= split));
            var passed = all.Tests >= ProjectionTest.PassTests
                && older.Tests >= PassHalfTests && newer.Tests >= PassHalfTests
                && older.Edge >= ProjectionTest.PassEdge && newer.Edge >= ProjectionTest.PassEdge;
            return new FollowOnWindow(w, all, older, newer, passed);
        }).ToList();

        var targets = tests.Select(t => t.Target).Distinct().OrderByDescending(d => d).ToList();
        var recent = targets.Take(RecentDays).ToHashSet();
        var prior = targets.Skip(RecentDays).Take(RecentDays).ToHashSet();

        var guessesByLink = tests
            .GroupBy(t => new LinkKey(t.Leader, t.LeaderMove, t.Follower, t.FollowerMove, t.Days))
            .ToDictionary(g => g.Key, g => Hits(g));

        var links = new List<FollowOnLink>();
        foreach (var (key, link) in tallies)
        {
            if (!bases.TryGetValue(new BaseKey(key.Follower, key.FollowerMove, key.Days), out var baseline)
                || !IsSignificant(link, baseline, out var p))
            {
                continue;
            }
            links.Add(new FollowOnLink(key.Leader, key.LeaderMove > 0, key.Follower, key.FollowerMove > 0, key.Days,
                link.Events, link.Hits, link.Rate, baseline.Rate, p,
                guessesByLink.GetValueOrDefault(key, new HitScore(0, null, null))));
        }

        return new FollowOnScorecard(
            AsOf: asOf,
            FirstGuess: first,
            BigDayZ: BigDayZ,
            SplitDate: split,
            All: Hits(tests),
            Recent: Hits(tests.Where(t => recent.Contains(t.Target))),
            PriorRecent: Hits(tests.Where(t => prior.Contains(t.Target))),
            Windows: windows,
            Links: links
                .OrderByDescending(l => l.Rate - l.BaseRate)
                .ThenBy(l => l.PValue)
                .ThenBy(l => l.Leader, StringComparer.Ordinal).ThenBy(l => l.Follower, StringComparer.Ordinal)
                .ThenBy(l => l.Days)
                .Take(ListedLinks)
                .ToList(),
            Open: open
                .OrderByDescending(o => o.Rate - o.BaseRate)
                .ThenBy(o => o.Leader, StringComparer.Ordinal).ThenBy(o => o.Follower, StringComparer.Ordinal)
                .ToList());
    }

    // The baseline for each guess is the base rate it was made against: what knowing nothing expects.
    private static HitScore Hits(IEnumerable<Test> tests)
    {
        var list = tests as IReadOnlyCollection<Test> ?? tests.ToList();
        return list.Count == 0
            ? new HitScore(0, null, null)
            : new HitScore(list.Count, list.Average(t => t.Hit ? 1.0 : 0.0), list.Average(t => t.BaseRate));
    }
}
