using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

/// <summary>
/// Pure pairwise-correlation math over daily-return series. Pearson correlation of
/// each pair's overlapping last-N daily returns — near +1 = they move together,
/// near -1 = they move oppositely (natural hedges / diversifiers). Pairwise
/// overlap (rather than one global intersection of dates) keeps a symbol with a
/// shorter history from shrinking every other pair's sample.
/// </summary>
public static class CorrelationAnalysis
{
    public sealed record Cell(string A, string B, decimal Corr);

    public sealed record Result(
        IReadOnlyList<string> Symbols,
        decimal?[][] Matrix,
        IReadOnlyList<Cell> Pairs,
        DateTime? AsOf,
        int WindowDays);

    private const int MinOverlap = 15;

    public static Result Compute(IReadOnlyList<(string Symbol, IReadOnlyList<DailyClose> Bars)> series, int windowDays)
    {
        var symbols = series.Select(s => s.Symbol).ToList();

        // date -> daily return, per symbol
        var returns = new Dictionary<string, Dictionary<DateTime, double>>(StringComparer.OrdinalIgnoreCase);
        DateTime? asOf = null;
        foreach (var (symbol, bars) in series)
        {
            var map = new Dictionary<DateTime, double>();
            for (var i = 1; i < bars.Count; i++)
            {
                var prev = (double)bars[i - 1].Close;
                if (prev != 0)
                {
                    map[bars[i].TradingDate] = ((double)bars[i].Close - prev) / prev;
                }
            }
            returns[symbol] = map;
            if (bars.Count > 0 && (asOf is null || bars[^1].TradingDate > asOf))
            {
                asOf = bars[^1].TradingDate;
            }
        }

        var n = symbols.Count;
        var matrix = new decimal?[n][];
        var pairs = new List<Cell>();

        for (var i = 0; i < n; i++)
        {
            matrix[i] = new decimal?[n];
            for (var j = 0; j < n; j++)
            {
                if (i == j)
                {
                    matrix[i][j] = 1m;
                    continue;
                }

                var corr = Pearson(returns[symbols[i]], returns[symbols[j]], windowDays);
                matrix[i][j] = corr;

                // Record each unordered pair once.
                if (j > i && corr is { } c)
                {
                    pairs.Add(new Cell(symbols[i], symbols[j], c));
                }
            }
        }

        // Most opposing first (ascending correlation).
        pairs.Sort((a, b) => a.Corr.CompareTo(b.Corr));

        return new Result(symbols, matrix, pairs, asOf, windowDays);
    }

    private static decimal? Pearson(Dictionary<DateTime, double> x, Dictionary<DateTime, double> y, int windowDays)
    {
        // Overlapping dates, most recent `windowDays`.
        var dates = x.Keys.Where(y.ContainsKey).OrderBy(d => d).ToList();
        if (dates.Count > windowDays)
        {
            dates = dates.GetRange(dates.Count - windowDays, windowDays);
        }
        if (dates.Count < MinOverlap)
        {
            return null;
        }

        double sx = 0, sy = 0;
        foreach (var d in dates) { sx += x[d]; sy += y[d]; }
        var mx = sx / dates.Count;
        var my = sy / dates.Count;

        double cov = 0, vx = 0, vy = 0;
        foreach (var d in dates)
        {
            var dx = x[d] - mx;
            var dy = y[d] - my;
            cov += dx * dy;
            vx += dx * dx;
            vy += dy * dy;
        }

        if (vx <= 0 || vy <= 0)
        {
            return null;
        }

        var r = cov / Math.Sqrt(vx * vy);
        return (decimal)Math.Round(Math.Clamp(r, -1.0, 1.0), 3);
    }
}
