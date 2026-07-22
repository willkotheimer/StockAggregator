using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Thrown when Yahoo returns a retryable failure (HTTP 429 / 5xx). Carries the
/// server-suggested retry delay when present so the caller can honour Retry-After.
/// </summary>
public sealed class YahooRetryableException : Exception
{
    public YahooRetryableException(string message, TimeSpan? retryAfter)
        : base(message) => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Pulls historical daily OHLC bars from Yahoo's chart endpoint. Unlike
/// <see cref="QuoteFetcher"/> (which reads the live "meta" block for a single
/// current quote), this parses the parallel <c>timestamp[]</c> and
/// <c>indicators.quote[0]</c> arrays — one entry per trading day — so a single
/// request with range=2y returns ~500 daily bars for a symbol.
///
/// Throws on failure rather than swallowing it, so the backfill orchestration can
/// apply retry/backoff and mark a symbol failed only after exhausting retries.
/// </summary>
public sealed class HistoricalQuoteFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<HistoricalQuoteFetcher> _logger;

    public HistoricalQuoteFetcher(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<HistoricalQuoteFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Fetches daily bars for one symbol over the given Yahoo range (e.g. "2y").
    /// Returns bars in ascending date order; days with no usable close are dropped.
    /// </summary>
    public async Task<IReadOnlyList<DailyOhlcBar>> FetchSymbolAsync(
        string symbol,
        string range,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _config["YahooChartBaseUrl"]
            ?? "https://query1.finance.yahoo.com/v8/finance/chart";

        var url = $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(symbol)}?interval=1d&range={Uri.EscapeDataString(range)}";

        var client = _httpClientFactory.CreateClient("yahoo");
        using var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 429 (throttled) and 5xx are worth retrying; other statuses are not.
            if (response.StatusCode == HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500)
            {
                throw new YahooRetryableException(
                    $"Yahoo returned {(int)response.StatusCode} for {symbol}.",
                    response.Headers.RetryAfter?.Delta);
            }

            throw new InvalidOperationException(
                $"Yahoo returned non-retryable {(int)response.StatusCode} for {symbol}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return ParseBars(symbol, doc);
    }

    private List<DailyOhlcBar> ParseBars(string symbol, JsonDocument doc)
    {
        var bars = new List<DailyOhlcBar>();

        if (!doc.RootElement.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0)
        {
            _logger.LogWarning("Yahoo historical response for {Symbol} had no result block.", symbol);
            return bars;
        }

        var node = result[0];
        if (!node.TryGetProperty("timestamp", out var timestamps)
            || timestamps.ValueKind != JsonValueKind.Array
            || !node.TryGetProperty("indicators", out var indicators)
            || !indicators.TryGetProperty("quote", out var quoteArray)
            || quoteArray.ValueKind != JsonValueKind.Array
            || quoteArray.GetArrayLength() == 0)
        {
            _logger.LogWarning("Yahoo historical response for {Symbol} had no timestamp/quote arrays.", symbol);
            return bars;
        }

        var quote = quoteArray[0];
        var opens = GetArray(quote, "open");
        var highs = GetArray(quote, "high");
        var lows = GetArray(quote, "low");
        var closes = GetArray(quote, "close");
        var volumes = GetArray(quote, "volume");

        // adjclose is a sibling array under indicators.adjclose[0].adjclose.
        JsonElement? adjCloses = null;
        if (indicators.TryGetProperty("adjclose", out var adjArray)
            && adjArray.ValueKind == JsonValueKind.Array
            && adjArray.GetArrayLength() > 0
            && adjArray[0].TryGetProperty("adjclose", out var adj)
            && adj.ValueKind == JsonValueKind.Array)
        {
            adjCloses = adj;
        }

        // Yahoo daily timestamps are the market-open instant in exchange time.
        // Shift by the exchange gmtoffset so we land on the correct trading date.
        var gmtOffsetSeconds = ReadGmtOffsetSeconds(node);

        var count = timestamps.GetArrayLength();
        for (var i = 0; i < count; i++)
        {
            var close = ElementAt(closes, i);
            // No close means Yahoo has no real bar for that slot; skip it.
            if (close is null)
            {
                continue;
            }

            var ts = timestamps[i].GetInt64();
            var tradingDate = DateTimeOffset
                .FromUnixTimeSeconds(ts)
                .ToOffset(TimeSpan.FromSeconds(gmtOffsetSeconds))
                .Date;

            bars.Add(new DailyOhlcBar(
                Symbol: symbol,
                TradingDate: tradingDate,
                Open: ElementAt(opens, i),
                High: ElementAt(highs, i),
                Low: ElementAt(lows, i),
                Close: close,
                AdjClose: adjCloses is { } ac ? ElementAt(ac, i) : null,
                Volume: ElementLongAt(volumes, i)));
        }

        return bars;
    }

    private static int ReadGmtOffsetSeconds(JsonElement node) =>
        node.TryGetProperty("meta", out var meta)
        && meta.TryGetProperty("gmtoffset", out var off)
        && off.ValueKind == JsonValueKind.Number
            ? off.GetInt32()
            : 0;

    private static JsonElement? GetArray(JsonElement quote, string name) =>
        quote.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr
            : null;

    private static decimal? ElementAt(JsonElement? array, int index)
    {
        if (array is not { } arr || index >= arr.GetArrayLength())
        {
            return null;
        }

        var el = arr[index];
        return el.ValueKind == JsonValueKind.Number ? el.GetDecimal() : null;
    }

    private static long? ElementLongAt(JsonElement? array, int index)
    {
        if (array is not { } arr || index >= arr.GetArrayLength())
        {
            return null;
        }

        var el = arr[index];
        return el.ValueKind == JsonValueKind.Number ? el.GetInt64() : null;
    }
}
