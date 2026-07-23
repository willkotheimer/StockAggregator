using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

/// <summary>
/// Pure "steady crawler" profile for one symbol over a trailing window. A steady
/// crawler grinds upward on a smooth line with little give-back — the opposite of
/// a name that lurches up and down to the same place. Steadiness is the R² of a
/// linear fit of log-price vs time (1 = a perfectly straight climb/decline, 0 =
/// noise); paired with a positive return and a shallow max drawdown it flags the
/// textbook crawler. Dependency-free and unit-testable.
/// </summary>
public static class CrawlerAnalysis
{
    private const int SparkPoints = 24;

    public sealed record Stats(
        int BarCount,
        DateTime? Start,
        decimal? ReturnPct,
        decimal? MaxDrawdownPct,
        decimal? UpDayPct,
        decimal? Steadiness,       // R² of log-price trend, 0..1
        decimal? WeeklyDriftPct,   // the fitted trend's implied %/week
        bool IsSteadyCrawler,
        decimal CrawlScore,
        IReadOnlyList<decimal> Spark);

    // Flag thresholds for a "textbook" steady crawler.
    private const double MinSteadiness = 0.65;
    private const double MaxDrawdown = 12.0;

    public static Stats Compute(IReadOnlyList<DailyClose> bars, int windowDays)
    {
        if (bars.Count == 0)
        {
            return new Stats(0, null, null, null, null, null, null, false, decimal.MinValue, Array.Empty<decimal>());
        }

        var start = Math.Max(0, bars.Count - windowDays);
        var w = bars.Skip(start).ToList();
        var closes = w.Select(b => (double)b.Close).ToList();
        var n = closes.Count;

        var first = closes[0];
        var last = closes[^1];
        double? returnPct = first != 0 ? (last - first) / first * 100 : null;

        // Up-day share.
        int up = 0, tot = 0;
        for (var i = 1; i < n; i++)
        {
            if (closes[i - 1] == 0) continue;
            tot++;
            if (closes[i] > closes[i - 1]) up++;
        }
        double? upDayPct = tot > 0 ? (double)up / tot * 100 : null;

        // Max peak-to-trough drawdown.
        double peak = closes[0], maxDd = 0;
        foreach (var c in closes)
        {
            if (c > peak) peak = c;
            else if (peak > 0) maxDd = Math.Max(maxDd, (peak - c) / peak * 100);
        }

        // Steadiness = R² of ln(price) vs t; slope drives the weekly drift.
        var (r2, slope) = LogTrend(closes);
        double? weeklyDrift = slope is { } m ? (Math.Exp(m * 5) - 1) * 100 : null;

        var isCrawler = returnPct is > 0 && r2 is { } rr && rr >= MinSteadiness && maxDd <= MaxDrawdown;

        // Rank: steady climbers (high R², positive) on top; steady decliners sink.
        double score = r2 is { } r ? (returnPct is > 0 ? r : -r) : -2;

        return new Stats(
            BarCount: n,
            Start: w[0].TradingDate,
            ReturnPct: Round(returnPct, 2),
            MaxDrawdownPct: Round(maxDd, 2),
            UpDayPct: Round(upDayPct, 1),
            Steadiness: Round(r2, 3),
            WeeklyDriftPct: Round(weeklyDrift, 2),
            IsSteadyCrawler: isCrawler,
            CrawlScore: (decimal)Math.Round(score, 4),
            Spark: Downsample(w));
    }

    /// <summary>R² and slope of a least-squares line through ln(price) vs index.</summary>
    private static (double? R2, double? Slope) LogTrend(IReadOnlyList<double> closes)
    {
        var ys = new List<double>(closes.Count);
        foreach (var c in closes)
        {
            if (c <= 0) return (null, null);
            ys.Add(Math.Log(c));
        }
        var n = ys.Count;
        if (n < 3) return (null, null);

        double sx = 0, sy = 0;
        for (var i = 0; i < n; i++) { sx += i; sy += ys[i]; }
        var mx = sx / n;
        var my = sy / n;

        double sxx = 0, syy = 0, sxy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = i - mx;
            var dy = ys[i] - my;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }
        if (sxx <= 0 || syy <= 0) return (0, 0);

        var slope = sxy / sxx;
        var r = sxy / Math.Sqrt(sxx * syy);
        return (r * r, slope);
    }

    private static IReadOnlyList<decimal> Downsample(IReadOnlyList<DailyClose> w)
    {
        if (w.Count <= SparkPoints)
        {
            return w.Select(b => b.Close).ToList();
        }
        var step = (double)(w.Count - 1) / (SparkPoints - 1);
        var pts = new List<decimal>(SparkPoints);
        for (var i = 0; i < SparkPoints; i++)
        {
            pts.Add(w[(int)Math.Round(i * step)].Close);
        }
        return pts;
    }

    private static decimal? Round(double? v, int digits) =>
        v is { } x ? (decimal)Math.Round(x, digits) : null;
}
