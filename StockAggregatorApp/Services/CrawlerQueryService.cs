using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

public interface ICrawlerQueryService
{
    Task<CrawlerResponse> GetCrawlersAsync(int windowDays, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-time "steady crawler" screener over the member stocks, from the backfilled
/// daily history (dbo.DailyOhlc). Ranks every tracked member by how steadily it has
/// climbed over the window (steady positive trends on top). Read-time, no table.
/// </summary>
public sealed class CrawlerQueryService : ICrawlerQueryService
{
    private readonly IDailyOhlcReadRepository _repository;
    private readonly IReadOnlyList<EtfGroup> _groups;

    public CrawlerQueryService(IDailyOhlcReadRepository repository, IOptions<EtfGroupOptions> groupOptions)
    {
        _repository = repository;
        _groups = groupOptions.Value.Groups;
    }

    public async Task<CrawlerResponse> GetCrawlersAsync(int windowDays, CancellationToken cancellationToken = default)
    {
        // Distinct member symbols, each mapped to the first ETF it appears under.
        var etfOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _groups)
        {
            foreach (var member in group.Members)
            {
                if (!etfOf.ContainsKey(member))
                {
                    etfOf[member] = group.Etf;
                }
            }
        }

        var rows = new List<(CrawlerRow Row, decimal Score)>();
        DateTime? asOf = null;

        foreach (var (symbol, etf) in etfOf)
        {
            var bars = await _repository.GetClosesAsync(symbol, cancellationToken);
            var s = CrawlerAnalysis.Compute(bars, windowDays);
            if (bars.Count > 0 && (asOf is null || bars[^1].TradingDate > asOf))
            {
                asOf = bars[^1].TradingDate;
            }

            rows.Add((new CrawlerRow(
                Symbol: symbol,
                Etf: etf,
                BarCount: s.BarCount,
                ReturnPct: s.ReturnPct,
                MaxDrawdownPct: s.MaxDrawdownPct,
                UpDayPct: s.UpDayPct,
                Steadiness: s.Steadiness,
                WeeklyDriftPct: s.WeeklyDriftPct,
                IsSteadyCrawler: s.IsSteadyCrawler,
                Spark: s.Spark), s.CrawlScore));
        }

        var ranked = rows
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Row.ReturnPct ?? decimal.MinValue)
            .Select(r => r.Row)
            .ToList();

        return new CrawlerResponse(windowDays, asOf?.ToString("yyyy-MM-dd"), ranked);
    }
}
