using Microsoft.Extensions.Logging;

namespace StockAggregator.Services;

/// <summary>
/// Ties the fetch + persist steps together so every timer entry point runs identical logic.
/// </summary>
public class SnapshotRunner : ISnapshotRunner
{
    private readonly QuoteFetcher _fetcher;
    private readonly QuoteRepository _repository;
    private readonly ILogger<SnapshotRunner> _logger;

    public SnapshotRunner(
        QuoteFetcher fetcher,
        QuoteRepository repository,
        ILogger<SnapshotRunner> logger)
    {
        _fetcher = fetcher;
        _repository = repository;
        _logger = logger;
    }

    public Task<int> RunAsync(string runLabel, CancellationToken cancellationToken = default)
        => RunAsync(runLabel, DateTime.UtcNow, cancellationToken);

    /// <summary>
    /// Runs a snapshot and stamps the rows with an explicit capture time. The
    /// scheduled runs pass <see cref="DateTime.UtcNow"/>; the catch-up passes the
    /// original slot time so a late re-run still lands in its proper slot.
    /// </summary>
    public async Task<int> RunAsync(string runLabel, DateTime capturedAtUtc, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting snapshot run {RunLabel} (stamped {CapturedAtUtc:u}).", runLabel, capturedAtUtc);

        try
        {
            var quotes = await _fetcher.FetchAsync(cancellationToken);
            var rowsPersisted = await _repository.SaveAsync(quotes, capturedAtUtc, runLabel, cancellationToken);

            _logger.LogInformation("Completed snapshot run {RunLabel} with {RowCount} rows saved.", runLabel, rowsPersisted);
            return rowsPersisted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot run {RunLabel} failed.", runLabel);
            throw;
        }
    }
}
