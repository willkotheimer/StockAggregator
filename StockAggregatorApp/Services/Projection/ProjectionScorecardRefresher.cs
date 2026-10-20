namespace StockAggregatorApp.Services.Projection;

/// <summary>
/// Computes the projection scorecard ahead of the midday email, so the 11:30 CT send
/// reads a cached result instead of waiting on a five-year replay (seconds locally,
/// slower on the F1 plan). Runs on startup and each weekday at 10:45 CT: after the
/// nightly rollup (22:30 CT) has added yesterday, before the digest asks.
/// </summary>
public sealed class ProjectionScorecardRefresher : BackgroundService
{
    private static readonly TimeSpan RefreshAtCentral = new(10, 45, 0);
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    private readonly ProjectionScorecardCache _cache;
    private readonly ILogger<ProjectionScorecardRefresher> _logger;

    public ProjectionScorecardRefresher(ProjectionScorecardCache cache, ILogger<ProjectionScorecardRefresher> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeUntilNextRefresh(DateTimeOffset.UtcNow), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cache.GetAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Projection scorecard refresh failed.");
        }
    }

    /// <summary>Until the next weekday 10:45 Central, daylight saving included.</summary>
    public static TimeSpan TimeUntilNextRefresh(DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, Central);
        var next = local.Date + RefreshAtCentral;
        if (next <= local.DateTime) next = next.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) next = next.AddDays(1);

        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), Central);
        return nextUtc - nowUtc.UtcDateTime;
    }
}
