using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

/// <summary>
/// Pivots raw quote rows into the week grid: builds the ordered snapshot columns
/// (date + capture time), then rows grouped by ETF — each ETF row followed by its
/// member symbols, in the order configured. Symbols captured but not listed under
/// any ETF are collected into a trailing "Other" group.
/// </summary>
public sealed class QuoteQueryService : IQuoteQueryService
{
    private const string OtherGroup = "Other";

    // Canonical order of the four daily capture times, by RunLabel.
    private static readonly Dictionary<string, int> RunOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["08:30 CT"] = 0,
        ["11:00 CT"] = 1,
        ["13:00 CT"] = 2,
        ["14:30 CT"] = 3,
    };

    private readonly IQuoteReadRepository _repository;
    private readonly IReadOnlyList<EtfGroup> _groups;

    public QuoteQueryService(IQuoteReadRepository repository, IOptions<EtfGroupOptions> groupOptions)
    {
        _repository = repository;
        _groups = groupOptions.Value.Groups;
    }

    public async Task<WeekQuotesResponse> GetWeekAsync(int tradingDays = 5, CancellationToken cancellationToken = default)
    {
        if (tradingDays < 1)
        {
            tradingDays = 1;
        }

        // Look back far enough to cover the requested trading days plus weekends/holidays,
        // then keep only the most recent distinct capture dates.
        var sinceUtc = DateTime.UtcNow.AddDays(-(tradingDays * 2 + 4));
        var records = await _repository.GetSinceAsync(sinceUtc, cancellationToken);

        if (records.Count == 0)
        {
            return new WeekQuotesResponse(Array.Empty<SnapshotColumn>(), Array.Empty<SymbolRow>());
        }

        // All four capture times (08:30–14:30 CT) fall on the same UTC calendar day,
        // so the UTC date is a safe grouping key for a trading day.
        static string DateKey(DateTime utc) => utc.ToString("yyyy-MM-dd");
        static string SnapshotKey(string date, string runLabel) => $"{date}|{runLabel}";

        var keepDates = records
            .Select(r => DateKey(r.CapturedAtUtc))
            .Distinct()
            .OrderByDescending(d => d, StringComparer.Ordinal)
            .Take(tradingDays)
            .ToHashSet(StringComparer.Ordinal);

        var kept = records
            .Where(r => keepDates.Contains(DateKey(r.CapturedAtUtc)))
            .ToList();

        // Ordered snapshot columns: by date ascending, then by capture time.
        var snapshots = kept
            .Select(r => new { Date = DateKey(r.CapturedAtUtc), r.RunLabel, r.CapturedAtUtc })
            .GroupBy(x => SnapshotKey(x.Date, x.RunLabel))
            .Select(g => g.First())
            .OrderBy(x => x.Date, StringComparer.Ordinal)
            .ThenBy(x => RunOrder.TryGetValue(x.RunLabel, out var i) ? i : int.MaxValue)
            .Select(x => new SnapshotColumn(SnapshotKey(x.Date, x.RunLabel), x.Date, x.RunLabel, x.CapturedAtUtc))
            .ToList();

        // symbol -> its cells (keyed by snapshot; latest capture wins on a duplicate).
        var cellsBySymbol = kept
            .GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(r => SnapshotKey(DateKey(r.CapturedAtUtc), r.RunLabel))
                    .ToDictionary(
                        cg => cg.Key,
                        cg =>
                        {
                            var latest = cg.OrderByDescending(r => r.CapturedAtUtc).First();
                            return new QuoteCell(latest.Price, latest.ChangePercent);
                        }),
                StringComparer.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, QuoteCell> CellsFor(string symbol) =>
            cellsBySymbol.TryGetValue(symbol, out var cells)
                ? cells
                : new Dictionary<string, QuoteCell>();

        var rows = new List<SymbolRow>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in _groups)
        {
            // ETF header row.
            rows.Add(new SymbolRow(group.Etf, group.Etf, IsEtf: true, group.Etf, group.Description, CellsFor(group.Etf)));
            placed.Add(group.Etf);

            // Member rows, in configured order.
            foreach (var member in group.Members)
            {
                rows.Add(new SymbolRow($"{group.Etf}:{member}", member, IsEtf: false, group.Etf, Description: null, CellsFor(member)));
                placed.Add(member);
            }
        }

        // Any captured symbols not covered by a group go into a trailing "Other" bucket.
        var leftovers = cellsBySymbol.Keys
            .Where(s => !placed.Contains(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var symbol in leftovers)
        {
            rows.Add(new SymbolRow($"{OtherGroup}:{symbol}", symbol, IsEtf: false, OtherGroup, Description: null, CellsFor(symbol)));
        }

        return new WeekQuotesResponse(snapshots, rows);
    }
}
