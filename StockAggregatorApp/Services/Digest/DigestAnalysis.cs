using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services.Digest;

/// <summary>One symbol's latest snapshot of the day: where it is right now.</summary>
public sealed record LiveQuote(
    string Symbol,
    decimal Price,
    decimal? ChangePct,
    decimal? DayHigh,
    decimal? DayLow,
    decimal? PreviousClose,
    DateTime CapturedAtUtc);

/// <summary>
/// Everything the signals are computed from. <see cref="History"/> holds each
/// symbol's daily closes strictly BEFORE <see cref="TradingDate"/>, ascending; today's
/// price comes only from <see cref="Live"/>, so it is never double-counted.
/// </summary>
public sealed record DigestInput(
    DateTime TradingDate,
    IReadOnlyList<EtfGroup> Groups,
    IReadOnlyDictionary<string, LiveQuote> Live,
    IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> History);

public sealed record GroupMove(string Etf, decimal ChangePct);

/// <summary>Today's move set against the symbol's typical daily move.</summary>
public sealed record UnusualMove(string Symbol, decimal ChangePct, decimal TypicalMovePct, decimal Ratio);

/// <summary>Was up (or down) at least the threshold earlier today, and has since crossed back.</summary>
public sealed record TurnAround(string Symbol, decimal EarlierPct, decimal NowPct);

/// <summary>A fund that is up while half or fewer of its stocks are.</summary>
public sealed record FundDivergence(
    string Etf, decimal EtfChangePct, int MembersTracked, int MembersFlatOrDown, string? TopSymbol, decimal? TopChangePct);

/// <summary>Today's price past the highest (or lowest) close of the prior month.</summary>
public sealed record MonthExtreme(string Symbol, bool IsHigh, decimal Price, decimal PreviousExtreme);

/// <summary>A sideways mover sitting near the top or bottom of its month-long range.</summary>
public sealed record RangeEdge(string Symbol, bool NearTop, decimal Low, decimal High, decimal Price);

/// <summary>Crossed far below its recent high today, with how comparable past drops ended.</summary>
public sealed record FarFromHigh(string Symbol, decimal BelowHighPct, DateTime HistoryStart, ReboundBaseRateDto? BaseRate);

public sealed record DigestSignals(
    DateTime TradingDate,
    DateTime PricesAtUtc,
    GroupMove? Strongest,
    GroupMove? Weakest,
    IReadOnlyList<UnusualMove> Unusual,
    IReadOnlyList<TurnAround> TurnArounds,
    IReadOnlyList<FundDivergence> FundDivergences,
    IReadOnlyList<MonthExtreme> MonthExtremes,
    IReadOnlyList<RangeEdge> RangeEdges,
    IReadOnlyList<FarFromHigh> FarFromHighs);

/// <summary>
/// The midday digest's signals, computed from today's live prices plus daily history.
/// Pure and dependency-free like the other *Analysis classes, so every threshold is
/// pinned by a unit test rather than checked by eye in an inbox.
///
/// Two kinds of signal:
///   midday     — today so far: group moves, unusually large moves, turnarounds,
///                funds rising on a few stocks.
///   multi-day  — today's price set against the last month (new highs/lows, range
///                edges) and the last ~10 months (far below the recent high).
/// </summary>
public static class DigestAnalysis
{
    /// <summary>About one month of trading days: the window for highs, lows and ranges.</summary>
    public const int MonthDays = 20;

    /// <summary>Trading days of history behind "a normal day" for each symbol (~1 year).</summary>
    public const int TypicalMoveDays = 250;

    // A move must be at least this big, and this many times the symbol's normal day.
    public const decimal UnusualMinPct = 1.5m;
    public const decimal UnusualMinRatio = 2m;

    /// <summary>How far it must have been up (or down) earlier for a turnaround to count.</summary>
    public const decimal TurnAroundMinPct = 1.5m;

    /// <summary>A range narrower than this is too flat for "near the top/bottom" to mean anything.</summary>
    public const decimal RangeEdgeMinBandPct = 4m;
    public const decimal RangeEdgeZonePct = 10m;

    // Matches the Rebound tab: 10% below the high of the trailing ~200 trading days.
    public const decimal FarFromHighPct = 10m;
    public const int FarFromHighWindowDays = 200;

    /// <summary>
    /// A same-day move bigger than this is treated as bad data, not news. Thin OTC
    /// listings occasionally report a session low far from any real trade (SSNLF showed
    /// a 37.7% intraday drop on a flat day), and one bad print shouldn't headline the email.
    /// </summary>
    public const decimal MaxPlausibleMovePct = 25m;

    private const int MinTypicalSamples = 40;
    private const int MinReboundBars = 120;

    // Caps per list, so a busy day still reads as a short email.
    private const int MaxUnusual = 5;
    private const int MaxTurnArounds = 3;
    private const int MaxMonthExtremes = 4;
    private const int MaxRangeEdges = 3;
    private const int MaxFarFromHighs = 5;

    public static DigestSignals Compute(DigestInput input)
    {
        // Listings outside the US (7203.T trades in Tokyo) are closed at a US midday,
        // so their "today" price is stale and every comparison against it misleads.
        var live = input.Live.Values
            .Where(q => IsUsListing(q.Symbol) && q.Price > 0m)
            .ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);

        var pricesAt = live.Count > 0 ? live.Values.Max(q => q.CapturedAtUtc) : input.TradingDate;

        var (strongest, weakest) = GroupMoves(input.Groups, live);
        var unusual = UnusualMoves(live, input.History);
        var turnArounds = TurnArounds(live, unusual.Select(u => u.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var divergences = FundDivergences(input.Groups, live);
        var extremes = MonthExtremes(live, input.History);
        var edges = RangeEdges(live, input.History, extremes.Select(e => e.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var farFromHighs = FarFromHighs(input.TradingDate, live, input.History);

        return new DigestSignals(
            input.TradingDate, pricesAt, strongest, weakest, unusual, turnArounds, divergences, extremes, edges, farFromHighs);
    }

    public static bool IsUsListing(string symbol) => !symbol.Contains('.');

    private static decimal? Change(LiveQuote q) =>
        q.ChangePct ?? (q.PreviousClose is { } pc and > 0m ? (q.Price - pc) / pc * 100m : null);

    private static (GroupMove? Strongest, GroupMove? Weakest) GroupMoves(
        IReadOnlyList<EtfGroup> groups, IReadOnlyDictionary<string, LiveQuote> live)
    {
        var moves = groups
            .Select(g => live.TryGetValue(g.Etf, out var q) && Change(q) is { } c ? new GroupMove(g.Etf, c) : null)
            .OfType<GroupMove>()
            .OrderByDescending(m => m.ChangePct)
            .ToList();

        return moves.Count switch
        {
            0 => (null, null),
            1 => (moves[0], null),
            _ => (moves[0], moves[^1]),
        };
    }

    /// <summary>
    /// The size of a normal day for a symbol: the median absolute close-to-close move
    /// over the last year. Median, not mean, so one crash day doesn't redefine normal.
    /// </summary>
    public static decimal? TypicalMovePct(IReadOnlyList<DailyClose> history)
    {
        var start = Math.Max(1, history.Count - TypicalMoveDays);
        var moves = new List<decimal>();
        for (var i = start; i < history.Count; i++)
        {
            var prev = history[i - 1].Close;
            if (prev > 0m)
            {
                moves.Add(Math.Abs((history[i].Close - prev) / prev * 100m));
            }
        }

        if (moves.Count < MinTypicalSamples)
        {
            return null;
        }

        moves.Sort();
        var mid = moves.Count / 2;
        return moves.Count % 2 == 1 ? moves[mid] : (moves[mid - 1] + moves[mid]) / 2m;
    }

    private static List<UnusualMove> UnusualMoves(
        IReadOnlyDictionary<string, LiveQuote> live, IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> history)
    {
        var result = new List<UnusualMove>();
        foreach (var q in live.Values)
        {
            if (Change(q) is not { } change || !history.TryGetValue(q.Symbol, out var bars)
                || TypicalMovePct(bars) is not { } typical || typical <= 0m)
            {
                continue;
            }

            var ratio = Math.Abs(change) / typical;
            if (Math.Abs(change) >= UnusualMinPct && Math.Abs(change) <= MaxPlausibleMovePct && ratio >= UnusualMinRatio)
            {
                result.Add(new UnusualMove(q.Symbol, change, typical, ratio));
            }
        }

        return result.OrderByDescending(u => u.Ratio).Take(MaxUnusual).ToList();
    }

    private static List<TurnAround> TurnArounds(IReadOnlyDictionary<string, LiveQuote> live, HashSet<string> exclude)
    {
        var result = new List<(TurnAround Turn, decimal Swing)>();
        foreach (var q in live.Values)
        {
            if (exclude.Contains(q.Symbol) || Change(q) is not { } now || q.PreviousClose is not { } pc || pc <= 0m)
            {
                continue;
            }

            // Up at least the threshold at the session high, and now at or below yesterday's close…
            if (q.DayHigh is { } high && (high - pc) / pc * 100m is var up && up >= TurnAroundMinPct && up <= MaxPlausibleMovePct && now <= 0m)
            {
                result.Add((new TurnAround(q.Symbol, up, now), up - now));
            }
            // …or the mirror image: down at the low, back at or above it now.
            else if (q.DayLow is { } low && (low - pc) / pc * 100m is var down && down <= -TurnAroundMinPct && down >= -MaxPlausibleMovePct && now >= 0m)
            {
                result.Add((new TurnAround(q.Symbol, down, now), now - down));
            }
        }

        return result.OrderByDescending(r => r.Swing).Take(MaxTurnArounds).Select(r => r.Turn).ToList();
    }

    private static List<FundDivergence> FundDivergences(IReadOnlyList<EtfGroup> groups, IReadOnlyDictionary<string, LiveQuote> live)
    {
        var result = new List<FundDivergence>();
        foreach (var group in groups)
        {
            if (!live.TryGetValue(group.Etf, out var etfQuote) || Change(etfQuote) is not { } etfChange)
            {
                continue;
            }

            var members = group.Members
                .Where(m => !string.Equals(m, group.Etf, StringComparison.OrdinalIgnoreCase))
                .Select(m => (Symbol: m, Change: live.TryGetValue(m, out var q) ? Change(q) : null))
                .Where(m => m.Change.HasValue)
                .Select(m => (m.Symbol, Change: m.Change!.Value))
                .ToList();

            var up = members.Count(m => m.Change > 0m);
            if (!AnalyticsQueryService.IsHiddenSignal(etfChange, members.Count, up))
            {
                continue;
            }

            var top = members.OrderByDescending(m => m.Change).FirstOrDefault();
            result.Add(new FundDivergence(
                group.Etf, etfChange, members.Count, members.Count - up,
                members.Count > 0 ? top.Symbol : null, members.Count > 0 ? top.Change : null));
        }

        return result.OrderByDescending(d => d.EtfChangePct).ToList();
    }

    private static List<MonthExtreme> MonthExtremes(
        IReadOnlyDictionary<string, LiveQuote> live, IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> history)
    {
        var result = new List<(MonthExtreme Extreme, decimal Beyond)>();
        foreach (var q in live.Values)
        {
            if (!history.TryGetValue(q.Symbol, out var bars) || bars.Count < MonthDays)
            {
                continue;
            }

            var month = bars.Skip(bars.Count - MonthDays).Select(b => b.Close).ToList();
            var high = month.Max();
            var low = month.Min();

            if (q.Price > high && high > 0m)
            {
                result.Add((new MonthExtreme(q.Symbol, true, q.Price, high), (q.Price - high) / high));
            }
            else if (q.Price < low && low > 0m)
            {
                result.Add((new MonthExtreme(q.Symbol, false, q.Price, low), (low - q.Price) / low));
            }
        }

        // Highs first, then lows; each ordered by how far past the old mark it is.
        return result
            .OrderByDescending(r => r.Extreme.IsHigh)
            .ThenByDescending(r => r.Beyond)
            .Take(MaxMonthExtremes)
            .Select(r => r.Extreme)
            .ToList();
    }

    private static List<RangeEdge> RangeEdges(
        IReadOnlyDictionary<string, LiveQuote> live,
        IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> history,
        HashSet<string> exclude)
    {
        var result = new List<(RangeEdge Edge, decimal Distance)>();
        foreach (var q in live.Values)
        {
            if (exclude.Contains(q.Symbol) || !history.TryGetValue(q.Symbol, out var bars) || bars.Count < MonthDays)
            {
                continue;
            }

            // The month before today, plus today's price, classified with the same rule
            // as the dashboard's trend matrix — only sideways movers have a "range".
            var window = bars.Skip(bars.Count - MonthDays).Select(b => (decimal?)b.Close).Append(q.Price).ToList();
            var trend = RangeMatrixQueryService.Summarise(window);
            if (trend.Direction != TrendDirection.Channeling
                || trend.BandPct is not { } band || band < RangeEdgeMinBandPct
                || trend.PositionInBand is not { } position)
            {
                continue;
            }

            var low = window.Min()!.Value;
            var high = window.Max()!.Value;
            if (position >= 100m - RangeEdgeZonePct)
            {
                result.Add((new RangeEdge(q.Symbol, true, low, high, q.Price), 100m - position));
            }
            else if (position <= RangeEdgeZonePct)
            {
                result.Add((new RangeEdge(q.Symbol, false, low, high, q.Price), position));
            }
        }

        return result.OrderBy(r => r.Distance).Take(MaxRangeEdges).Select(r => r.Edge).ToList();
    }

    private static List<FarFromHigh> FarFromHighs(
        DateTime tradingDate,
        IReadOnlyDictionary<string, LiveQuote> live,
        IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> history)
    {
        var result = new List<FarFromHigh>();
        foreach (var q in live.Values)
        {
            if (!history.TryGetValue(q.Symbol, out var bars) || bars.Count < MinReboundBars)
            {
                continue;
            }

            var withToday = bars.Append(new DailyClose(tradingDate, q.Price)).ToList();
            var now = ReboundAnalysis.CurrentInWindow(withToday, FarFromHighWindowDays, FarFromHighPct, ReboundMode.Trough);

            // Only the day it crosses the line. A stock that was already this far down
            // yesterday was in yesterday's email, and repeating it daily is noise.
            var yesterday = ReboundAnalysis.CurrentInWindow(bars, FarFromHighWindowDays, FarFromHighPct, ReboundMode.Trough);
            if (now is null || yesterday is not null || now.AnchorPrice <= 0m)
            {
                continue;
            }

            var belowHigh = (now.AnchorPrice - q.Price) / now.AnchorPrice * 100m;
            var scan = ReboundAnalysis.Scan(withToday, FarFromHighPct, ReboundMode.Trough);
            var baseRate = ReboundQueryService.BuildBaseRate(scan.Completed, now.MovePct);

            result.Add(new FarFromHigh(q.Symbol, belowHigh, bars[0].TradingDate, baseRate));
        }

        return result.OrderByDescending(f => f.BelowHighPct).Take(MaxFarFromHighs).ToList();
    }
}
