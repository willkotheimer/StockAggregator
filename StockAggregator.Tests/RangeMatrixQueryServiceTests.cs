using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services;
using Xunit;

namespace StockAggregator.Tests;

/// <summary>
/// Covers how the matrix stitches its two price sources and which dates earn a column.
/// Both are places a wrong answer renders as a plausible-looking grid rather than an error.
/// </summary>
public class RangeMatrixQueryServiceTests
{
    private static readonly DateTime Mon = new(2026, 5, 4);
    private static readonly DateTime Tue = new(2026, 5, 5);
    private static readonly DateTime Wed = new(2026, 5, 6);

    /// <summary>One ETF with enough members that the 20% column quorum is meaningful.</summary>
    private static readonly string[] Members =
        ["AAA", "BBB", "CCC", "DDD", "EEE", "FFF", "GGG", "HHH", "III", "TOKYO"];

    private static RangeMatrixQueryService Build(
        IEnumerable<SymbolClose> official, IEnumerable<SymbolClose> rollup)
    {
        var groups = new EtfGroupOptions
        {
            Groups = [new EtfGroup { Etf = "ETF", Description = "Test", Members = [.. Members] }],
        };

        return new RangeMatrixQueryService(
            new FakeOhlc(official.ToList()),
            new FakeRollup(rollup.ToList()),
            Options.Create(groups));
    }

    private static SymbolClose[] AllSymbolsOn(DateTime date, decimal price) =>
        [.. Members.Select(s => new SymbolClose(s, date, price)), new SymbolClose("ETF", date, price)];

    [Fact]
    public async Task DateCarriedByASingleSymbol_DoesNotBecomeAColumn()
    {
        // Wednesday is a US holiday: only the Tokyo listing traded.
        var official = AllSymbolsOn(Mon, 100m)
            .Concat(AllSymbolsOn(Tue, 102m))
            .Concat([new SymbolClose("TOKYO", Wed, 50m)]);

        var result = await Build(official, []).GetMatrixAsync(Mon, Wed, null);

        Assert.Equal(["2026-05-04", "2026-05-05"], result.Dates);
    }

    [Fact]
    public async Task DateCoveredByEnoughSymbols_KeepsItsColumn()
    {
        var official = AllSymbolsOn(Mon, 100m).Concat(AllSymbolsOn(Tue, 102m));

        var result = await Build(official, []).GetMatrixAsync(Mon, Tue, null);

        Assert.Equal(["2026-05-04", "2026-05-05"], result.Dates);
    }

    [Fact]
    public async Task OfficialCloseWins_WhereBothSourcesHaveTheSameDate()
    {
        // The overlap window: DailyOhlc and the rollup both carry Monday.
        var official = AllSymbolsOn(Mon, 100m);
        var rollup = AllSymbolsOn(Mon, 999m);

        var result = await Build(official, rollup).GetMatrixAsync(Mon, Mon, null);

        var etf = result.Rows.Single(r => r.IsEtf);
        Assert.Equal(100m, etf.Closes[0]);
        Assert.Null(result.ProvisionalFrom);
    }

    [Fact]
    public async Task RollupFillsDatesTheBackfillHasNotReached_AndIsFlaggedProvisional()
    {
        var official = AllSymbolsOn(Mon, 100m);
        var rollup = AllSymbolsOn(Tue, 110m);

        var result = await Build(official, rollup).GetMatrixAsync(Mon, Tue, null);

        var etf = result.Rows.Single(r => r.IsEtf);
        Assert.Equal([100m, 110m], etf.Closes);
        Assert.Equal("2026-05-05", result.ProvisionalFrom);
    }

    [Fact]
    public async Task WeekdayFilter_MeasuresChangeAgainstThePreviousKeptColumn()
    {
        // Three consecutive Fridays, filtered to Fridays: the change on the third is
        // week-over-week against the second, not against an intervening weekday.
        var f1 = new DateTime(2026, 5, 1);
        var thu = new DateTime(2026, 5, 7);
        var f2 = new DateTime(2026, 5, 8);

        var official = AllSymbolsOn(f1, 100m)
            .Concat(AllSymbolsOn(thu, 500m))
            .Concat(AllSymbolsOn(f2, 110m));

        var result = await Build(official, []).GetMatrixAsync(f1, f2, dayOfWeek: 5);

        Assert.Equal(["2026-05-01", "2026-05-08"], result.Dates);
        var etf = result.Rows.Single(r => r.IsEtf);
        Assert.Null(etf.Changes[0]);
        Assert.Equal(10m, Math.Round(etf.Changes[1]!.Value, 2));
    }

    private sealed class FakeOhlc(List<SymbolClose> rows) : IDailyOhlcReadRepository
    {
        public Task<IReadOnlyList<SymbolClose>> GetClosesForSymbolsAsync(
            IReadOnlyList<string> symbols, DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SymbolClose>>(
                [.. rows.Where(r => symbols.Contains(r.Symbol, StringComparer.OrdinalIgnoreCase)
                    && r.TradingDate >= from && r.TradingDate <= to)]);

        public Task<IReadOnlyList<DateTime>> GetTradingDatesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DateTime>>([.. rows.Select(r => r.TradingDate).Distinct().Order()]);

        public Task<IReadOnlyList<DailyClose>> GetClosesAsync(string symbol, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DailyBar>> GetBarsAsync(string symbol, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRollup(List<SymbolClose> rows) : IAnalyticsReadRepository
    {
        public Task<IReadOnlyList<SymbolClose>> GetLastPricesForSymbolsAsync(
            IReadOnlyList<string> symbols, DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SymbolClose>>(
                [.. rows.Where(r => symbols.Contains(r.Symbol, StringComparer.OrdinalIgnoreCase)
                    && r.TradingDate >= from && r.TradingDate <= to)]);

        public Task<IReadOnlyList<DateTime>> GetRollupDatesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DateTime>>([.. rows.Select(r => r.TradingDate).Distinct().Order()]);

        public Task<DateTime?> GetLatestTradingDateAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DailyChange>> GetChangesForDateAsync(DateTime tradingDate, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
