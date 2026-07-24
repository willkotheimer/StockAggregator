namespace StockAggregatorApp.Services;

/// <summary>
/// Warms the crawler cache on startup and refreshes the standard windows once a day.
/// Runs inside the web app (kept warm around the clock), so the heavy per-symbol
/// history scan happens on a schedule rather than on a visitor's request. The daily
/// slot sits just after the Functions app's nightly analytics rollup (22:30 UTC).
/// </summary>
public sealed class CrawlerCacheRefresher : BackgroundService
{
    private static readonly TimeSpan RefreshAtUtc = new(23, 15, 0);

    private readonly CrawlerCache _cache;
    private readonly ILogger<CrawlerCacheRefresher> _logger;

    public CrawlerCacheRefresher(CrawlerCache cache, ILogger<CrawlerCacheRefresher> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm on startup so the first visitor after a restart gets a cached response.
        await RefreshAllAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRefresh();
            _logger.LogInformation("Next crawler cache refresh in {Hours:n1}h.", delay.TotalHours);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshAllAsync(stoppingToken);
        }
    }

    private async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        foreach (var window in CrawlerCache.StandardWindows)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await _cache.RefreshAsync(window, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Crawler cache refresh failed for window={Window}.", window);
            }
        }
    }

    private static TimeSpan TimeUntilNextRefresh()
    {
        var now = DateTimeOffset.UtcNow;
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, RefreshAtUtc.Hours, RefreshAtUtc.Minutes, 0, TimeSpan.Zero);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
