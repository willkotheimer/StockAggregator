using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services.Portfolios;

/// <summary>One slice of each contribution: a symbol and its share of the money.</summary>
public sealed record PortfolioLeg(string Symbol, decimal Weight);

/// <summary>
/// Test 2's rule: the cash slice moves wholly into <see cref="Symbol"/> on any close below
/// <see cref="Below"/>, and wholly back to cash on any close above <see cref="Above"/>.
/// </summary>
public sealed record SwingRule(string Symbol, decimal Below, decimal Above);

public sealed record PortfolioDefinition(int Number, string Name, IReadOnlyList<PortfolioLeg> Legs, SwingRule? Swing = null);

public sealed record PortfolioHolding(string Symbol, decimal Shares, decimal Price, decimal Value);

public sealed record PortfolioResult(
    int Number,
    string Name,
    int Contributions,
    decimal Contributed,
    decimal Value,
    decimal Gain,
    decimal? GainPct,
    IReadOnlyList<PortfolioHolding> Holdings,
    // Test 2 only: whether the swing slice is in the stock right now, and when it last moved.
    bool? SwingInStock,
    DateTime? LastSwing);

public sealed record PortfolioLeaderboard(
    DateTime Start,
    // The last close the rules were applied through, and the prices the values use.
    DateTime ThroughClose,
    DateTime ValuedAt,
    decimal ContributionAmount,
    DateTime NextContribution,
    // Best value first. Every portfolio puts in the same money on the same days, so value ranks them fairly.
    IReadOnlyList<PortfolioResult> Ranked);

/// <summary>
/// The practice portfolios: eight fixed mixes, each putting in $250 every other Friday
/// from <see cref="Start"/>, compared against each other. This is a forward test, not a
/// backtest: nothing before the start date counts, so last year's runs (Sandisk, Micron)
/// can't flatter anyone.
///
/// No state is stored. The rules are fixed, so replaying them over real closes from the
/// start date gives the same result as tracking each day live. A contribution goes in at
/// that day's close (the next trading day's, when the Friday is a holiday); fractional
/// shares are allowed, as at most brokers. SPAXX (a money market fund) is cash at $1 a
/// share, earning <see cref="CashYield"/> a year.
/// </summary>
public static class PaperPortfolios
{
    public static readonly DateTime Start = new(2026, 10, 2);
    public const decimal Contribution = 250m;
    public const int ContributionEveryDays = 14;

    public const string Cash = "SPAXX";
    public const decimal CashYield = 0.04m;

    /// <summary>The S&amp;P 500 fund: also the trading calendar the rules walk.</summary>
    public const string SP500 = "VOO";

    public static readonly IReadOnlyList<PortfolioDefinition> All =
    [
        new(1, "S&P 500 only", [new(SP500, 1m)]),
        new(2, "S&P 500 + Sandisk swing", [new(SP500, 0.5m), new(Cash, 0.5m)], new SwingRule("SNDK", 1300m, 1500m)),
        new(3, "S&P 500 + Walmart + Exxon", [new(SP500, 0.5m), new("WMT", 0.25m), new("XOM", 0.25m)]),
        new(4, "S&P 500 + Micron", [new(SP500, 0.5m), new("MU", 0.5m)]),
        new(5, "S&P 500 + chip fund", [new(SP500, 0.5m), new("SMH", 0.5m)]),
        new(6, "S&P 500 + Eli Lilly + UnitedHealth", [new(SP500, 0.5m), new("LLY", 0.25m), new("UNH", 0.25m)]),
        new(7, "Sandisk only", [new("SNDK", 1m)]),
        new(8, "S&P 500 + growth fund", [new(SP500, 0.5m), new("VUG", 0.5m)]),
    ];

    /// <summary>Every symbol that needs prices (SPAXX doesn't; it's always $1).</summary>
    public static IReadOnlyList<string> PricedSymbols { get; } = All
        .SelectMany(p => p.Legs.Select(l => l.Symbol).Concat(p.Swing is { } s ? new[] { s.Symbol } : Array.Empty<string>()))
        .Where(s => s != Cash)
        .Distinct()
        .ToList();

    /// <summary>
    /// Applies every portfolio's rules over the closes up to <paramref name="asOf"/>, then
    /// values them at <paramref name="latestPrices"/> where given (e.g. today's midday
    /// prices), otherwise at each symbol's last close.
    /// </summary>
    public static PortfolioLeaderboard Run(
        IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> history,
        DateTime asOf,
        IReadOnlyDictionary<string, decimal>? latestPrices = null) =>
        Run(All, history, asOf, latestPrices);

    internal static PortfolioLeaderboard Run(
        IReadOnlyList<PortfolioDefinition> portfolios,
        IReadOnlyDictionary<string, IReadOnlyList<DailyClose>> history,
        DateTime asOf,
        IReadOnlyDictionary<string, decimal>? latestPrices = null)
    {
        var closes = history.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(b => b.TradingDate.Date >= Start && b.TradingDate.Date <= asOf.Date && b.Close > 0m)
                .ToDictionary(b => b.TradingDate.Date, b => b.Close),
            StringComparer.OrdinalIgnoreCase);

        var calendar = closes.TryGetValue(SP500, out var sp) ? sp.Keys.Order().ToList() : [];
        var books = portfolios.Select(p => new Book(p)).ToList();

        var next = Start;
        DateTime? lastDay = null;
        foreach (var day in calendar)
        {
            var calendarDays = lastDay is { } prev ? (day - prev).Days : 0;
            var contributions = 0;
            while (day >= next)
            {
                contributions++;
                next = next.AddDays(ContributionEveryDays);
            }

            foreach (var book in books)
            {
                book.Step(day, calendarDays, contributions, closes);
            }
            lastDay = day;
        }

        decimal? PriceOf(string symbol)
        {
            if (symbol == Cash) return 1m;
            if (latestPrices is not null && latestPrices.TryGetValue(symbol, out var live) && live > 0m) return live;
            return closes.TryGetValue(symbol, out var c) && c.Count > 0 ? c[c.Keys.Max()] : null;
        }

        var ranked = books
            .Select(b => b.Result(PriceOf))
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Number)
            .ToList();

        return new PortfolioLeaderboard(
            Start,
            ThroughClose: lastDay ?? Start,
            ValuedAt: asOf.Date,
            ContributionAmount: Contribution,
            NextContribution: next,
            Ranked: ranked);
    }

    /// <summary>One portfolio's running book: shares held, cash, and money waiting for a price.</summary>
    private sealed class Book
    {
        private readonly PortfolioDefinition _def;
        private readonly Dictionary<string, decimal> _shares = new(StringComparer.OrdinalIgnoreCase);
        // A slice whose symbol had no close that day waits here, uninvested, for the next close.
        private readonly Dictionary<string, decimal> _waiting = new(StringComparer.OrdinalIgnoreCase);
        private decimal _cash;
        private int _contributions;
        private DateTime? _lastSwing;

        public Book(PortfolioDefinition def) => _def = def;

        public void Step(DateTime day, int calendarDays, int contributions, Dictionary<string, Dictionary<DateTime, decimal>> closes)
        {
            decimal? Close(string symbol) =>
                closes.TryGetValue(symbol, out var c) && c.TryGetValue(day, out var price) ? price : null;

            // Cash earns its yield for every calendar day since the last trading day.
            _cash += _cash * CashYield * calendarDays / 365m;

            for (var i = 0; i < contributions; i++)
            {
                _contributions++;
                foreach (var leg in _def.Legs)
                {
                    var amount = Contribution * leg.Weight;
                    if (leg.Symbol == Cash) _cash += amount;
                    else _waiting[leg.Symbol] = _waiting.GetValueOrDefault(leg.Symbol) + amount;
                }
            }

            foreach (var symbol in _waiting.Keys.ToList())
            {
                if (_waiting[symbol] > 0m && Close(symbol) is { } price)
                {
                    _shares[symbol] = _shares.GetValueOrDefault(symbol) + _waiting[symbol] / price;
                    _waiting[symbol] = 0m;
                }
            }

            if (_def.Swing is { } swing && Close(swing.Symbol) is { } swingPrice)
            {
                if (swingPrice < swing.Below && _cash > 0m)
                {
                    _shares[swing.Symbol] = _shares.GetValueOrDefault(swing.Symbol) + _cash / swingPrice;
                    _cash = 0m;
                    _lastSwing = day;
                }
                else if (swingPrice > swing.Above && _shares.GetValueOrDefault(swing.Symbol) > 0m)
                {
                    _cash += _shares[swing.Symbol] * swingPrice;
                    _shares[swing.Symbol] = 0m;
                    _lastSwing = day;
                }
            }
        }

        public PortfolioResult Result(Func<string, decimal?> priceOf)
        {
            var holdings = new List<PortfolioHolding>();
            foreach (var (symbol, shares) in _shares.Where(kv => kv.Value > 0m).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var price = priceOf(symbol) ?? 0m;
                holdings.Add(new PortfolioHolding(symbol, Math.Round(shares, 6), price, Math.Round(shares * price, 2)));
            }
            if (_cash > 0m)
            {
                holdings.Add(new PortfolioHolding(Cash, Math.Round(_cash, 2), 1m, Math.Round(_cash, 2)));
            }

            var waiting = _waiting.Values.Sum();
            var contributed = _contributions * Contribution;
            var value = Math.Round(holdings.Sum(h => h.Value) + waiting, 2);

            return new PortfolioResult(
                _def.Number,
                _def.Name,
                _contributions,
                contributed,
                value,
                value - contributed,
                contributed > 0m ? Math.Round((value - contributed) / contributed * 100m, 2) : null,
                holdings,
                SwingInStock: _def.Swing is { } s ? _shares.GetValueOrDefault(s.Symbol) > 0m : null,
                LastSwing: _lastSwing);
        }
    }
}
