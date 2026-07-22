using Microsoft.Extensions.Options;
using StockAggregatorApp.Models;
using StockAggregatorApp.Repositories;

namespace StockAggregatorApp.Services;

public interface IAnalyticsQueryService
{
    Task<HiddenSignalResponse> GetHiddenSignalsAsync(DateTime? date, CancellationToken cancellationToken = default);
    Task<RotationResponse> GetRotationsAsync(DateTime? date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-time analytics computed from the daily rollup + the ETF group config.
/// Cheap enough (11 ETFs × ~7 members) to run per request; the expensive
/// per-symbol rollup is what's precomputed.
/// </summary>
public sealed class AnalyticsQueryService : IAnalyticsQueryService
{
    // An ETF counts as "rising" for a hidden signal at/above this daily change.
    private const decimal EtfUpThreshold = 0.5m;

    private readonly IAnalyticsReadRepository _repository;
    private readonly IReadOnlyList<EtfGroup> _groups;

    public AnalyticsQueryService(IAnalyticsReadRepository repository, IOptions<EtfGroupOptions> groupOptions)
    {
        _repository = repository;
        _groups = groupOptions.Value.Groups;
    }

    public async Task<HiddenSignalResponse> GetHiddenSignalsAsync(DateTime? date, CancellationToken cancellationToken = default)
    {
        var (asOf, changes) = await ResolveAsync(date, cancellationToken);
        if (asOf is null)
        {
            return new HiddenSignalResponse(null, Array.Empty<HiddenSignal>());
        }

        var signals = new List<HiddenSignal>();
        foreach (var group in _groups)
        {
            changes.TryGetValue(group.Etf, out var etfChange);

            var members = group.Members
                .Select(m => new MemberChange(m, changes.TryGetValue(m, out var c) ? c : null))
                .ToList();

            var tracked = members.Count(m => m.ChangePct.HasValue);
            var up = members.Count(m => m.ChangePct is > 0m);
            var downOrFlat = tracked - up;

            var top = members
                .Where(m => m.ChangePct.HasValue)
                .OrderByDescending(m => m.ChangePct)
                .FirstOrDefault();

            var isHidden = etfChange is { } e && e >= EtfUpThreshold && tracked > 0 && up * 2 <= tracked;

            signals.Add(new HiddenSignal(
                group.Etf,
                group.Description,
                etfChange,
                tracked,
                up,
                downOrFlat,
                isHidden,
                top?.Symbol,
                top?.ChangePct,
                members));
        }

        // Flagged first, then by the widest ETF-vs-members divergence.
        var ordered = signals
            .OrderByDescending(s => s.IsHiddenSignal)
            .ThenByDescending(s => (s.EtfChangePct ?? 0m) - (decimal)s.MembersUp / Math.Max(1, s.MembersTracked))
            .ToList();

        return new HiddenSignalResponse(asOf.Value.ToString("yyyy-MM-dd"), ordered);
    }

    public async Task<RotationResponse> GetRotationsAsync(DateTime? date, CancellationToken cancellationToken = default)
    {
        var (asOf, changes) = await ResolveAsync(date, cancellationToken);
        if (asOf is null)
        {
            return new RotationResponse(null, Array.Empty<RotationRow>());
        }

        var ranked = _groups
            .Select(g => new { g.Etf, g.Description, Change = changes.TryGetValue(g.Etf, out var c) ? c : (decimal?)null })
            .OrderByDescending(x => x.Change ?? decimal.MinValue)
            .Select((x, i) => new RotationRow(i + 1, x.Etf, x.Description, x.Change))
            .ToList();

        return new RotationResponse(asOf.Value.ToString("yyyy-MM-dd"), ranked);
    }

    private async Task<(DateTime? AsOf, Dictionary<string, decimal?> Changes)> ResolveAsync(
        DateTime? date,
        CancellationToken cancellationToken)
    {
        var asOf = date ?? await _repository.GetLatestTradingDateAsync(cancellationToken);
        if (asOf is null)
        {
            return (null, new Dictionary<string, decimal?>());
        }

        var rows = await _repository.GetChangesForDateAsync(asOf.Value, cancellationToken);
        var map = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            map[row.Symbol] = row.ChangePct;
        }

        return (asOf, map);
    }
}
