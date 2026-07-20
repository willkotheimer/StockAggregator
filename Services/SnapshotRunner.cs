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

    public async Task RunAsync(string runLabel, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting snapshot run {RunLabel}.", runLabel);

        try
        {
            var quotes = await _fetcher.FetchAsync(cancellationToken);
            await _repository.SaveAsync(quotes, DateTime.UtcNow, runLabel, cancellationToken);

            _logger.LogInformation("Completed snapshot run {RunLabel}.", runLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot run {RunLabel} failed.", runLabel);
            throw;
        }
    }
}
