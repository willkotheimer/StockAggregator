using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

/// <summary>
/// Timer entry points, one per scheduled capture time.
///
/// Schedules are expressed in the app's local time zone. Set the app setting
/// WEBSITE_TIME_ZONE = "Central Standard Time" so these fire on Central time and
/// follow daylight-saving automatically. The CRON fields are:
///   {second} {minute} {hour} {day} {month} {day-of-week}
/// and 1-5 restricts runs to Monday–Friday (trading days).
/// </summary>
public class MarketSnapshotFunctions
{
    private readonly SnapshotRunner _runner;
    private readonly ILogger<MarketSnapshotFunctions> _logger;

    public MarketSnapshotFunctions(SnapshotRunner runner, ILogger<MarketSnapshotFunctions> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [Function("Snapshot_0830CT")]
    public Task Snapshot0830(
        [TimerTrigger("0 30 8 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
        => _runner.RunAsync("08:30 CT", cancellationToken);

    [Function("Snapshot_1100CT")]
    public Task Snapshot1100(
        [TimerTrigger("0 0 11 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
        => _runner.RunAsync("11:00 CT", cancellationToken);

    [Function("Snapshot_1300CT")]
    public Task Snapshot1300(
        [TimerTrigger("0 0 13 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
        => _runner.RunAsync("13:00 CT", cancellationToken);

    [Function("Snapshot_1430CT")]
    public Task Snapshot1430(
        [TimerTrigger("0 30 14 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
        => _runner.RunAsync("14:30 CT", cancellationToken);
}
