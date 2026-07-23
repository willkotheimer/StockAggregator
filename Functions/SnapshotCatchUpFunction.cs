using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

/// <summary>
/// Self-healing safety net for the scheduled snapshots. If a run failed to save
/// (app was mid-deploy at the scheduled minute, a total fetch outage, etc.), this
/// re-runs it and stamps the rows with the *original* slot time so the data lands
/// in its proper slot rather than at the catch-up moment.
///
/// For each slot it only acts when the slot is between 0 and 60 minutes old AND
/// the database has no rows for it yet — so it never backfills stale prices and
/// never duplicates a run that already landed (on time or by an earlier catch-up).
///
/// The connection-open retry in <see cref="SqlConnectionFactory"/> already handles
/// the common failure (serverless DB resuming); this covers the rarer case where a
/// run failed outright. Scheduled at :20 and :50 past the hour around the slots so
/// each miss gets one or two attempts inside its 60-minute window, while keeping
/// DB touches sparse enough that the serverless database still pauses when idle.
/// </summary>
public class SnapshotCatchUpFunction
{
    // Must match the scheduled slots in MarketSnapshotFunctions, in Central time.
    private static readonly (int Hour, int Minute, string Label)[] Slots =
    {
        (8, 30, "08:30 CT"),
        (11, 0, "11:00 CT"),
        (13, 0, "13:00 CT"),
        (14, 30, "14:30 CT"),
    };

    private static readonly TimeSpan CatchUpWindow = TimeSpan.FromMinutes(60);

    private readonly SnapshotRunner _runner;
    private readonly QuoteRepository _repository;
    private readonly ILogger<SnapshotCatchUpFunction> _logger;

    public SnapshotCatchUpFunction(SnapshotRunner runner, QuoteRepository repository, ILogger<SnapshotCatchUpFunction> logger)
    {
        _runner = runner;
        _repository = repository;
        _logger = logger;
    }

    [Function("Snapshot_CatchUp")]
    public async Task CatchUp(
        [TimerTrigger("0 20,50 8,11,13,14 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        var nowUtc = DateTime.UtcNow;
        var todayCentral = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, central).Date;

        foreach (var slot in Slots)
        {
            var slotCentral = todayCentral.AddHours(slot.Hour).AddMinutes(slot.Minute);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotCentral, DateTimeKind.Unspecified), central);

            var age = nowUtc - slotUtc;
            if (age < TimeSpan.Zero || age > CatchUpWindow)
            {
                continue; // not due yet, or older than the 60-minute window
            }

            try
            {
                if (await _repository.HasQuotesForRunAsync(slotUtc, slot.Label, cancellationToken))
                {
                    continue; // already saved — nothing to do
                }

                _logger.LogWarning(
                    "Catch-up: {Label} has no saved quotes ~{Minutes:n0} min after schedule; re-running stamped {SlotUtc:u}.",
                    slot.Label, age.TotalMinutes, slotUtc);

                await _runner.RunAsync(slot.Label, slotUtc, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log and move on; the next scheduled catch-up will try again while
                // the slot is still inside its window.
                _logger.LogError(ex, "Catch-up for {Label} failed.", slot.Label);
            }
        }
    }
}
