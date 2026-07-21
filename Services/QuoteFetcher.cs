using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Pulls quotes from Yahoo Finance's public chart endpoint (no API key). One
/// request per symbol against v8/finance/chart; the price, previous close and
/// volume come from the response's "meta" block. A failed symbol is logged and
/// skipped so one bad ticker doesn't sink the whole run.
/// </summary>
public class QuoteFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<QuoteFetcher> _logger;

    public QuoteFetcher(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<QuoteFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StockQuote>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var symbols = _config["StockSymbols"];
        if (string.IsNullOrWhiteSpace(symbols))
        {
            throw new InvalidOperationException(
                "App setting 'StockSymbols' is not set. Provide a comma-separated list of tickers.");
        }

        // Base URL is configurable so the endpoint can be swapped without a code change.
        var baseUrl = _config["YahooChartBaseUrl"]
            ?? "https://query1.finance.yahoo.com/v8/finance/chart";

        // Normalize: trim, drop blanks, uppercase, de-dupe while preserving order.
        var cleaned = symbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();

        var client = _httpClientFactory.CreateClient("yahoo");
        _logger.LogInformation("Requesting quotes for {Count} symbols from {Url}.", cleaned.Count, baseUrl);

        var quotes = new List<StockQuote>(cleaned.Count);
        foreach (var symbol in cleaned)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var url = $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d";
                using var response = await client.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Yahoo quote for {Symbol} failed with status {StatusCode}; skipping.",
                        symbol,
                        (int)response.StatusCode);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                var quote = ParseChartMeta(symbol, doc);
                if (quote is not null)
                {
                    quotes.Add(quote);
                }
                else
                {
                    _logger.LogWarning("Yahoo response for {Symbol} had no usable quote data; skipping.", symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Yahoo quote for {Symbol}; skipping.", symbol);
            }
        }

        _logger.LogInformation("Fetched {Count} of {Total} quotes from Yahoo.", quotes.Count, cleaned.Count);
        return quotes;
    }

    /// <summary>
    /// Extracts one quote from a Yahoo chart response. The chart endpoint doesn't
    /// return a change-percent field, so it's computed from price and previous close.
    /// </summary>
    private static StockQuote? ParseChartMeta(string symbol, JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0
            || !result[0].TryGetProperty("meta", out var meta))
        {
            return null;
        }

        var price = ReadDecimal(meta, "regularMarketPrice");
        // "chartPreviousClose" is the reliable field on the chart endpoint;
        // "previousClose" is often null there.
        var previousClose = ReadDecimal(meta, "chartPreviousClose") ?? ReadDecimal(meta, "previousClose");
        var volume = ReadLong(meta, "regularMarketVolume");

        decimal? changePercent = price.HasValue && previousClose is { } prev && prev != 0m
            ? Math.Round((price.Value - prev) / prev * 100m, 4)
            : null;

        if (price is null && volume is null)
        {
            return null;
        }

        return new StockQuote
        {
            Symbol = symbol,
            Price = price,
            ChangesPercentage = changePercent,
            Volume = volume,
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;

    private static long? ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
}
