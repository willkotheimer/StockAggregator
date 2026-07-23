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

        // Depth-aware resume: a symbol is "done" only if its earliest bar already
        // reaches the requested range. This lets a deeper re-pull (e.g. 2y → 5y)
        // advance — the old count-based check treated any existing rows as done, so
        // a plain re-run skipped everything and a force re-ran all 87 and timed out.
        var cutoff = RangeCutoffUtc(range);
        var existingEarliest = await _repository.GetEarliestDatesBySymbolAsync(cancellationToken);
        bool IsDeepEnough(string symbol, IReadOnlyDictionary<string, DateTime> earliest) =>
            earliest.TryGetValue(symbol, out var e) && e <= cutoff;

        var todo = force
            ? symbols
            : symbols.Where(s => !IsDeepEnough(s, existingEarliest)).ToList();

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

        // Re-read earliest dates so RemainingAfter reflects reality after this run —
        // and stays truthful: it converges to the count of symbols that simply don't
        // have the requested depth available (recent listings), rather than oscillating.
        var freshEarliest = await _repository.GetEarliestDatesBySymbolAsync(cancellationToken);
        var remainingAfter = symbols.Count(s => !IsDeepEnough(s, freshEarliest));

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

    /// <summary>
    /// The cutoff date at/under which a symbol's earliest bar counts as "already
    /// deep enough" for the requested Yahoo range. Includes 30 days of slack so a
    /// first trading bar that lands a couple weeks after the exact range start
    /// (holidays, listing date) still qualifies. Unknown ranges fall back to 2y.
    /// </summary>
    private static DateTime RangeCutoffUtc(string range)
    {
        var now = DateTime.UtcNow.Date;
        range = (range ?? string.Empty).Trim().ToLowerInvariant();

        DateTime start;
        if (range == "max") start = new DateTime(1900, 1, 1);
        else if (range == "ytd") start = new DateTime(now.Year, 1, 1);
        else if (range.EndsWith("mo") && int.TryParse(range[..^2], out var mo)) start = now.AddMonths(-mo);
        else if (range.EndsWith("y") && int.TryParse(range[..^1], out var y)) start = now.AddYears(-y);
        else if (range.EndsWith("d") && int.TryParse(range[..^1], out var d)) start = now.AddDays(-d);
        else start = now.AddYears(-2);

        return start.AddDays(30);
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
