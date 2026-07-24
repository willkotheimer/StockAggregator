using System.Collections.Concurrent;
using System.Diagnostics;
using StockAggregatorApp.Models;

namespace StockAggregatorApp.Services;

/// <summary>
/// Process-wide cache for the steady-crawler screener. Computing one window scans the
/// full daily history of every tracked symbol (~150s), far too slow to run on each
/// request. This memoizes the result per window: a background service refreshes the
/// standard windows nightly, and any first/uncached request computes once — guarded
/// so concurrent callers share a single compute rather than stampeding the database.
/// After the first compute the endpoint is an in-memory read, so the serverless SQL
/// database is touched on a schedule instead of on every dashboard visit.
/// </summary>
public sealed class CrawlerCache
{
    // The windows the dashboard exposes (1M / 3M / 6M in trading days). Precomputed nightly.
    public static readonly IReadOnlyList<int> StandardWindows = new[] { 21, 63, 126 };

    // Concurrent GETs for the same not-yet-cached window collapse to one compute if a
    // result appeared within this grace period while they queued behind the lock.
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CrawlerCache> _logger;
    private readonly ConcurrentDictionary<int, Entry> _cache = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public CrawlerCache(IServiceScopeFactory scopeFactory, ILogger<CrawlerCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public sealed record Entry(CrawlerResponse Response, DateTimeOffset ComputedUtc);

    /// <summary>Returns the cached result if present (any age); otherwise computes once and caches it.</summary>
    public async Task<CrawlerResponse> GetAsync(int windowDays, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(windowDays, out var hit))
        {
            return hit.Response;
        }

        return (await ComputeAndStoreAsync(windowDays, force: false, cancellationToken)).Response;
    }

    /// <summary>Forces a recompute and replaces the cached entry. Used by the nightly refresher.</summary>
    public Task RefreshAsync(int windowDays, CancellationToken cancellationToken = default) =>
        ComputeAndStoreAsync(windowDays, force: true, cancellationToken);

    private async Task<Entry> ComputeAndStoreAsync(int windowDays, bool force, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(windowDays, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // A caller ahead of us may have just populated it while we queued on the lock.
            if (!force
                && _cache.TryGetValue(windowDays, out var existing)
                && DateTimeOffset.UtcNow - existing.ComputedUtc < DedupeWindow)
            {
                return existing;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICrawlerQueryService>();

            var sw = Stopwatch.StartNew();
            var response = await service.GetCrawlersAsync(windowDays, cancellationToken);
            sw.Stop();

            var entry = new Entry(response, DateTimeOffset.UtcNow);
            _cache[windowDays] = entry;
            _logger.LogInformation(
                "Crawler cache computed window={Window} in {ElapsedMs}ms ({Rows} rows).",
                windowDays, sw.ElapsedMilliseconds, response.Rows.Count);
            return entry;
        }
        finally
        {
            gate.Release();
        }
    }
}
