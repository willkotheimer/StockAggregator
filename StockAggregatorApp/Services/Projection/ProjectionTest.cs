using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services.Digest;

namespace StockAggregatorApp.Services.Projection;

public sealed record ProjectionInput(
    IReadOnlyList<EtfGroup> Groups,
    // Each symbol's full daily close series, ascending.
    IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> History,
    // Earnings report dates (reported plus estimated) for symbols that have any; a missing symbol is "unknown".
    IReadOnlyDictionary<string, IReadOnlyList<DateTime>> EarningsDates);

/// <summary>Hit rates on a set of tests: the pattern's, and the no-pattern baseline's on the same tests.</summary>
public sealed record HitScore(int Tests, double? HitRate, double? BaselineHitRate)
{
    public double? Edge => HitRate - BaselineHitRate;
}

/// <summary>Hit rate for tests whose window crossed an event (a quarter end, an earnings report), and for the rest.</summary>
public sealed record EventSplit(HitScore With, HitScore Without);

public sealed record PatternScore(
    string Id,
    string Label,
    HitScore All,
    HitScore Older,
    HitScore Newer,
    EventSplit QuarterEnd,
    EventSplit Earnings,
    bool Passed);

public sealed record ProjectionScorecard(
    DateTime AsOf,
    int HorizonDays,
    DateTime SplitDate,
    // Tests whose target day fell in the last 20 trading days, and the 20 before that.
    HitScore Recent,
    HitScore PriorRecent,
    EventSplit QuarterEnd,
    EventSplit Earnings,
    // Patterns with enough tests to list, best edge first.
    IReadOnlyList<PatternScore> Patterns);

/// <summary>
/// The projection test: can a pattern, alone or paired with one other, place a stock's
/// price 20 trading days ahead inside a fixed window more often than knowing nothing?
///
/// For an origin day when the pattern is on, the projection is the median of what
/// followed every EARLIER time the pattern was on — the 20-day move of each past
/// match, across all stocks, scaled by that stock's normal 20-day move so calm and
/// wild stocks can share one pool. Only matches whose 20 days had fully finished by
/// the origin day count, so the test never sees its own future. The baseline is the
/// same thing with no pattern: every earlier 20-day move.
///
/// A projection is a hit when the real price lands within one normal 20-day move of
/// it. The window is the same width for the pattern and the baseline, so the only
/// way to win is a better-placed center. By construction the baseline lands about
/// half the time; edge is how far a pattern beats it on the same days.
///
/// This is a bootstrap over historical analogs. With a fixed-width window only the
/// middle of the analog distribution matters, which is why no path simulation is
/// run — scoring by interval coverage instead would use the same pools.
/// </summary>
public static class ProjectionTest
{
    public const int HorizonDays = 20;

    /// <summary>A year of history before an origin, for the stock's normal 20-day move.</summary>
    public const int NormalMoveDays = 250;

    /// <summary>Past matches needed before a pattern makes a projection at all.</summary>
    public const int MinPool = 30;

    /// <summary>Tests a pattern needs before it's listed.</summary>
    public const int MinListedTests = 30;

    // The bar for "real edge", fixed before seeing any results: beat the baseline by
    // 10 points in both the older and the newer half, on at least 100 tests.
    public const double PassEdge = 0.10;
    public const int PassTests = 100;
    private const int PassHalfTests = 30;

    private const int RecentDays = 20;

    internal sealed record Test(int Combo, DateTime Origin, DateTime Target, bool Hit, bool BaselineHit, bool QuarterEnd, bool? Earnings);

    public static ProjectionScorecard Run(ProjectionInput input) =>
        Score(Tests(input), ProjectionPatterns.Combinations());

    /// <summary>Every individual test, before scoring. Internal so the no-lookahead test can compare runs.</summary>
    internal static List<Test> Tests(ProjectionInput input)
    {
        var combos = ProjectionPatterns.Combinations();
        var strongDays = ProjectionPatterns.StrongestGroupDays(input.Groups, input.History);

        // (end date, combo or -1 for baseline, scaled move) — joins the pool on its end date.
        var events = new List<(DateTime End, int Combo, double Scaled)>();
        var candidates = new List<(DateTime Origin, int Combo, string Symbol, int Index, DateTime Target, double Actual, double Width)>();

        foreach (var (symbol, series) in input.History)
        {
            // Listings outside the US close on different days; leave them out, as the digest does.
            if (!DigestAnalysis.IsUsListing(symbol) || series.Count < NormalMoveDays + HorizonDays + 1)
            {
                continue;
            }

            var n = series.Count;
            var strong = new bool[n];
            var funds = input.Groups.Where(g => string.Equals(g.Etf, symbol, StringComparison.OrdinalIgnoreCase)
                || g.Members.Contains(symbol, StringComparer.OrdinalIgnoreCase)).Select(g => g.Etf).ToList();
            for (var i = 0; i < n; i++)
            {
                strong[i] = funds.Any(f => strongDays.TryGetValue(f, out var days) && days.Contains(series[i].TradingDate));
            }

            var states = ProjectionPatterns.States(series, strong);
            var forward = ForwardMoves(series);
            var width = NormalWidths(forward);

            for (var i = NormalMoveDays; i + HorizonDays < n; i++)
            {
                if (width[i] is not { } w || w <= 0 || forward[i] is not { } actual)
                {
                    continue;
                }

                var end = series[i + HorizonDays].TradingDate;
                events.Add((end, -1, actual / w));

                for (var k = 0; k < combos.Count; k++)
                {
                    if (combos[k].All(p => states[p.Id][i]))
                    {
                        events.Add((end, k, actual / w));
                        candidates.Add((series[i].TradingDate, k, symbol, i, end, actual, w));
                    }
                }
            }
        }

        return Sweep(events, candidates, combos.Count, input.EarningsDates);
    }

    /// <summary>ln(close[i+20] / close[i]) — the move the projection is trying to place.</summary>
    private static double?[] ForwardMoves(IReadOnlyList<DailyClose> series)
    {
        var forward = new double?[series.Count];
        for (var i = 0; i + HorizonDays < series.Count; i++)
        {
            var from = series[i].Close;
            var to = series[i + HorizonDays].Close;
            if (from > 0m && to > 0m) forward[i] = Math.Log((double)(to / from));
        }
        return forward;
    }

    /// <summary>
    /// The window half-width at each origin: the stock's median absolute 20-day move over
    /// the year before, counting only windows that had finished by that day.
    /// </summary>
    private static double?[] NormalWidths(double?[] forward)
    {
        var widths = new double?[forward.Length];
        var buffer = new List<double>(NormalMoveDays);
        for (var i = NormalMoveDays; i < forward.Length; i++)
        {
            buffer.Clear();
            for (var j = i - NormalMoveDays; j <= i - HorizonDays; j++)
            {
                if (forward[j] is { } m) buffer.Add(Math.Abs(m));
            }
            if (buffer.Count >= NormalMoveDays / 2)
            {
                buffer.Sort();
                widths[i] = Median(buffer);
            }
        }
        return widths;
    }

    private static List<Test> Sweep(
        List<(DateTime End, int Combo, double Scaled)> events,
        List<(DateTime Origin, int Combo, string Symbol, int Index, DateTime Target, double Actual, double Width)> candidates,
        int comboCount,
        IReadOnlyDictionary<string, IReadOnlyList<DateTime>> earnings)
    {
        events.Sort((a, b) => a.End.CompareTo(b.End));
        // Fully ordered (List.Sort isn't stable), so the same data always gives the same tests in the same order.
        candidates.Sort((a, b) =>
            a.Origin != b.Origin ? a.Origin.CompareTo(b.Origin)
            : a.Symbol != b.Symbol ? string.CompareOrdinal(a.Symbol, b.Symbol)
            : a.Combo.CompareTo(b.Combo));

        var baseline = new RunningMedian();
        var pools = Enumerable.Range(0, comboCount).Select(_ => new RunningMedian()).ToArray();
        var lastTested = new Dictionary<(int Combo, string Symbol), int>();
        var tests = new List<Test>();
        var e = 0;

        foreach (var c in candidates)
        {
            // Everything that had finished by the close on the origin day is known.
            while (e < events.Count && events[e].End <= c.Origin)
            {
                (events[e].Combo < 0 ? baseline : pools[events[e].Combo]).Add(events[e].Scaled);
                e++;
            }

            // One test per stock per window: the next one only once this window has closed,
            // so 20 consecutive days of the same pattern count once, not 20 times.
            if (lastTested.TryGetValue((c.Combo, c.Symbol), out var last) && c.Index < last + HorizonDays)
            {
                continue;
            }

            var pool = pools[c.Combo];
            if (pool.Count < MinPool || baseline.Count < MinPool)
            {
                continue;
            }

            var center = pool.Median * c.Width;
            var baseCenter = baseline.Median * c.Width;
            tests.Add(new Test(
                c.Combo,
                c.Origin,
                c.Target,
                Math.Abs(c.Actual - center) <= c.Width,
                Math.Abs(c.Actual - baseCenter) <= c.Width,
                CrossesQuarterEnd(c.Origin, c.Target),
                CrossesEarnings(c.Symbol, c.Origin, c.Target, earnings)));
            lastTested[(c.Combo, c.Symbol)] = c.Index;
        }

        return tests;
    }

    private static ProjectionScorecard Score(List<Test> tests, IReadOnlyList<IReadOnlyList<PatternDefinition>> combos)
    {
        if (tests.Count == 0)
        {
            var none = new HitScore(0, null, null);
            return new ProjectionScorecard(DateTime.MinValue, HorizonDays, DateTime.MinValue, none, none,
                new EventSplit(none, none), new EventSplit(none, none), []);
        }

        var first = tests.Min(t => t.Origin);
        var last = tests.Max(t => t.Origin);
        var split = first.AddTicks((last - first).Ticks / 2).Date;

        var patterns = new List<PatternScore>();
        foreach (var group in tests.GroupBy(t => t.Combo))
        {
            var list = group.ToList();
            var all = Hits(list);
            var older = Hits(list.Where(t => t.Origin < split));
            var newer = Hits(list.Where(t => t.Origin >= split));
            if (all.Tests < MinListedTests) continue;

            var passed = all.Tests >= PassTests
                && older.Tests >= PassHalfTests && newer.Tests >= PassHalfTests
                && older.Edge >= PassEdge && newer.Edge >= PassEdge;

            var combo = combos[group.Key];
            patterns.Add(new PatternScore(
                string.Join("+", combo.Select(p => p.Id)),
                Label(combo),
                all, older, newer, QuarterEnd(list), Earnings(list), passed));
        }

        // The last 20 distinct target days, and the 20 before: is accuracy rising or falling?
        var targets = tests.Select(t => t.Target).Distinct().OrderByDescending(d => d).ToList();
        var recent = targets.Take(RecentDays).ToHashSet();
        var prior = targets.Skip(RecentDays).Take(RecentDays).ToHashSet();

        return new ProjectionScorecard(
            AsOf: targets[0],
            HorizonDays: HorizonDays,
            SplitDate: split,
            Recent: Hits(tests.Where(t => recent.Contains(t.Target))),
            PriorRecent: Hits(tests.Where(t => prior.Contains(t.Target))),
            QuarterEnd: QuarterEnd(tests),
            Earnings: Earnings(tests),
            Patterns: patterns
                .OrderByDescending(p => p.All.Edge)
                .ThenByDescending(p => p.All.Tests)
                .ToList());
    }

    /// <summary>"At a 1-month high, plus a steady climber".</summary>
    private static string Label(IReadOnlyList<PatternDefinition> combo) =>
        combo.Count == 1
            ? combo[0].Label
            : $"{combo[0].Label}, plus {char.ToLowerInvariant(combo[1].Label[0])}{combo[1].Label[1..]}";

    private static HitScore Hits(IEnumerable<Test> tests)
    {
        var list = tests as IReadOnlyCollection<Test> ?? tests.ToList();
        return list.Count == 0
            ? new HitScore(0, null, null)
            : new HitScore(list.Count, list.Average(t => t.Hit ? 1.0 : 0.0), list.Average(t => t.BaselineHit ? 1.0 : 0.0));
    }

    private static EventSplit QuarterEnd(IReadOnlyList<Test> tests) =>
        new(Hits(tests.Where(t => t.QuarterEnd)), Hits(tests.Where(t => !t.QuarterEnd)));

    // Tests on stocks with no earnings data are left out of both sides rather than guessed.
    private static EventSplit Earnings(IReadOnlyList<Test> tests) =>
        new(Hits(tests.Where(t => t.Earnings == true)), Hits(tests.Where(t => t.Earnings == false)));

    /// <summary>True when (origin, target] contains a calendar quarter end: Mar 31, Jun 30, Sep 30 or Dec 31.</summary>
    public static bool CrossesQuarterEnd(DateTime origin, DateTime target)
    {
        for (var year = origin.Year; year <= target.Year; year++)
        {
            foreach (var month in new[] { 3, 6, 9, 12 })
            {
                var end = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                if (end > origin.Date && end <= target.Date) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True/false when the window does/doesn't contain an earnings report; null when the
    /// stock has no earnings data. Funds don't report earnings, so they're always false.
    /// </summary>
    public static bool? CrossesEarnings(string symbol, DateTime origin, DateTime target, IReadOnlyDictionary<string, IReadOnlyList<DateTime>> earnings)
    {
        if (DigestNames.IsFund(symbol)) return false;
        if (!earnings.TryGetValue(symbol, out var dates) || dates.Count == 0) return null;
        return dates.Any(d => d > origin.Date && d <= target.Date);
    }

    private static double Median(List<double> sorted)
    {
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}

/// <summary>A median that updates in O(log n) per value: a max-heap of the lower half, a min-heap of the upper.</summary>
public sealed class RunningMedian
{
    private readonly PriorityQueue<double, double> _lower = new(); // max-heap via negated priority
    private readonly PriorityQueue<double, double> _upper = new();

    public int Count => _lower.Count + _upper.Count;

    public void Add(double value)
    {
        if (_lower.Count == 0 || value <= _lower.Peek()) _lower.Enqueue(value, -value);
        else _upper.Enqueue(value, value);

        if (_lower.Count > _upper.Count + 1) { var v = _lower.Dequeue(); _upper.Enqueue(v, v); }
        else if (_upper.Count > _lower.Count) { var v = _upper.Dequeue(); _lower.Enqueue(v, -v); }
    }

    public double Median => Count == 0
        ? double.NaN
        : _lower.Count > _upper.Count ? _lower.Peek() : (_lower.Peek() + _upper.Peek()) / 2;
}
