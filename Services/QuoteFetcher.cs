using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Pulls batch quotes from Financial Modeling Prep for the configured list of symbols.
/// </summary>
public class QuoteFetcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

        var apiKey = _config["FinancialDataApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("App setting 'FinancialDataApiKey' is not set.");
        }

        // Base URL is configurable so the endpoint can be swapped without a code change.
        // Default is the batch-quote endpoint, which returns price / changesPercentage / volume
        // for a comma-separated list of symbols.
        var baseUrl = _config["FmpQuoteBaseUrl"]
            ?? "https://financialmodelingprep.com/api/v3/quote";

        // Normalize: trim, drop blanks, uppercase, de-dupe while preserving order.
        var cleaned = string.Join(
            ',',
            symbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .Distinct());

        var url = $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(cleaned)}?apikey={apiKey}";

        var client = _httpClientFactory.CreateClient("fmp");
        _logger.LogInformation("Requesting quotes for {Count} symbols from {Url}.", cleaned.Split(',').Length, baseUrl);

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Financial Modeling Prep request failed with status {StatusCode}: {ResponseBody}",
                    (int)response.StatusCode,
                    responseText);
                response.EnsureSuccessStatusCode();
            }

            var quotes = await response.Content.ReadFromJsonAsync<List<StockQuote>>(JsonOptions, cancellationToken)
                ?? new List<StockQuote>();

            _logger.LogInformation("Received {Count} quotes from provider.", quotes.Count);
            return quotes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch quotes from Financial Modeling Prep for symbols {Symbols}.", cleaned);
            throw;
        }
    }
}
