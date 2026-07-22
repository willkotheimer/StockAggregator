using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockAggregator.Services;

/// <summary>
/// Orchestrates the historical daily-OHLC backfill: pulls ~2 years of daily bars
/// per symbol from Yahoo (one call each) into dbo.DailyOhlc.
///
/// Deliberately gentle on Yahoo despite doing it all at once rather than the live
/// snapshots' burst: symbols are fetched sequentially with a delay between each,
/// and 429/5xx responses are retried with exponential backoff. It's also chunked
/// and resumable — GetRowCountsBySymbolAsync lets a run skip symbols already
/// backfilled, so calling the endpoint repeatedly (or with a small batchSize)
/// picks up where the last one stopped.
/// </summary>
public sealed class HistoricalBackfillService
{
    private readonly HistoricalQuoteFetcher _fetcher;
    private readonly DailyOhlcRepository _repository;
    private readonly AnalyticsRepository _analyticsRepository;
    private readonly IConfiguration _config;
    private readonly ILogger<HistoricalBackfillService> _logger;

    public HistoricalBackfillService(
        HistoricalQuoteFetcher fetcher,
        DailyOhlcRepository repository,
        AnalyticsRepository analyticsRepository,
        IConfiguration config,
        ILogger<HistoricalBackfillService> logger)
    {
        _fetcher = fetcher;
        _repository = repository;
        _analyticsRepository = analyticsRepository;
        _config = config;
        _logger = logger;
    }

    public sealed record BackfillResult(
        int TotalSymbols,
        int AlreadyDone,
        int Attempted,
        int Succeeded,
        int Failed,
        int RowsWritten,
        int RemainingAfter,
        IReadOnlyList<string> FailedSymbols);

    /// <summary>
    /// Backfills up to <paramref name="batchSize"/> not-yet-done symbols.
    /// </summary>
    /// <param name="range">Yahoo range, e.g. "2y".</param>
    /// <param name="batchSize">Max symbols to process this call. Resumable, so
    /// small values let you spread the work across several invocations.</param>
    /// <param name="force">Reprocess every symbol, even ones already backfilled.</param>
    public async Task<BackfillResult> RunAsync(
        string range,
        int batchSize,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var symbols = ParseSymbols();
        var existingCounts = await _repository.GetRowCountsBySymbolAsync(cancellationToken);

        var todo = force
            ? symbols
            : symbols.Where(s => !existingCounts.ContainsKey(s)).ToList();

        var batch = todo.Take(Math.Max(0, batchSize)).ToList();

        // Delay between symbols so ~90 calls trickle over tens of seconds rather
        // than hammering Yahoo in a burst. Configurable; default 400ms.
        var perSymbolDelay = TimeSpan.FromMilliseconds(
            int.TryParse(_config["BackfillPerSymbolDelayMs"], out var ms) ? ms : 400);

        _logger.LogInformation(
            "Backfill starting: {Batch} symbols this run (range={Range}, {Todo} outstanding of {Total}, force={Force}).",
            batch.Count, range, todo.Count, symbols.Count, force);

        var succeeded = 0;
        var rowsWritten = 0;
        var failedSymbols = new List<string>();

        for (var i = 0; i < batch.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = batch[i];

            try
            {
                var bars = await FetchWithRetryAsync(symbol, range, cancellationToken);
                var written = await _repository.ReplaceSymbolBarsAsync(symbol, bars, cancellationToken);
                rowsWritten += written;
                succeeded++;
                _logger.LogInformation("Backfilled {Symbol}: {Rows} daily bars.", symbol, written);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedSymbols.Add(symbol);
                _logger.LogWarning(ex, "Backfill failed for {Symbol}; skipping.", symbol);
            }

            if (perSymbolDelay > TimeSpan.Zero && i < batch.Count - 1)
            {
                await Task.Delay(perSymbolDelay, cancellationToken);
            }
        }

        var remainingAfter = force
            ? 0
            : symbols.Count(s => !existingCounts.ContainsKey(s) && !batch.Contains(s))
              + failedSymbols.Count;

        var result = new BackfillResult(
            TotalSymbols: symbols.Count,
            AlreadyDone: symbols.Count - todo.Count,
            Attempted: batch.Count,
            Succeeded: succeeded,
            Failed: failedSymbols.Count,
            RowsWritten: rowsWritten,
            RemainingAfter: remainingAfter,
            FailedSymbols: failedSymbols);

        await SafeLogRunAsync(startedAt, result, cancellationToken);

        _logger.LogInformation(
            "Backfill done: {Succeeded} ok, {Failed} failed, {Rows} rows, {Remaining} remaining.",
            result.Succeeded, result.Failed, result.RowsWritten, result.RemainingAfter);

        return result;
    }

    /// <summary>Fetch one symbol, retrying retryable failures with exponential backoff.</summary>
    private async Task<IReadOnlyList<Models.DailyOhlcBar>> FetchWithRetryAsync(
        string symbol,
        string range,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        var backoff = TimeSpan.FromSeconds(1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _fetcher.FetchSymbolAsync(symbol, range, cancellationToken);
            }
            catch (YahooRetryableException ex) when (attempt < maxAttempts)
            {
                var wait = ex.RetryAfter ?? backoff;
                _logger.LogWarning(
                    "Retryable error for {Symbol} (attempt {Attempt}/{Max}): {Message}. Waiting {Wait}s.",
                    symbol, attempt, maxAttempts, ex.Message, wait.TotalSeconds);
                await Task.Delay(wait, cancellationToken);
                backoff += backoff; // 1s, 2s, 4s
            }
        }
    }

    // Same normalization as QuoteFetcher: trim, drop blanks, uppercase, de-dupe.
    private List<string> ParseSymbols()
    {
        var symbols = _config["StockSymbols"];
        if (string.IsNullOrWhiteSpace(symbols))
        {
            throw new InvalidOperationException(
                "App setting 'StockSymbols' is not set. Provide a comma-separated list of tickers.");
        }

        return symbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();
    }

    private async Task SafeLogRunAsync(DateTime startedAt, BackfillResult result, CancellationToken cancellationToken)
    {
        try
        {
            var status = result.Failed == 0 ? "Success" : "Partial";
            var message = $"attempted={result.Attempted}, ok={result.Succeeded}, failed={result.Failed}, "
                + $"remaining={result.RemainingAfter}"
                + (result.FailedSymbols.Count > 0 ? $", failedSymbols={string.Join(",", result.FailedSymbols)}" : string.Empty);
            if (message.Length > 400)
            {
                message = message[..400];
            }

            await _analyticsRepository.LogRunAsync(
                "HistoricalBackfill", startedAt, DateTime.UtcNow, result.RowsWritten, status, message, cancellationToken);
        }
        catch
        {
            // best-effort logging
        }
    }
}
