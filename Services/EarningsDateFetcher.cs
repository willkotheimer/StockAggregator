using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockAggregator.Services;

/// <summary>One reported earnings date.</summary>
public sealed record EarningsReport(string Symbol, DateTime ReportDate, string? FiscalQuarterEnd);

/// <summary>
/// Reads each company's recent earnings report dates from Nasdaq's public
/// earnings-surprise data (the last four quarters). Yahoo's keyless chart endpoint
/// carries dividends but not earnings, and its quote-summary endpoint now needs a
/// session cookie, so Nasdaq is the keyless source. Funds and most non-US listings
/// have no rows; a symbol that fails or comes back empty is skipped, never fatal.
/// </summary>
public sealed class EarningsDateFetcher
{
    private const string DefaultBaseUrl = "https://api.nasdaq.com/api/company";
    private static readonly TimeSpan PerSymbolDelay = TimeSpan.FromMilliseconds(400);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EarningsDateFetcher> _logger;

    public EarningsDateFetcher(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<EarningsDateFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EarningsReport>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var symbols = (_config["StockSymbols"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Where(s => !s.Contains('.')) // Nasdaq covers US listings; 7203.T has no page there
            .Distinct()
            .ToList();

        var baseUrl = (_config["EarningsBaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');
        var client = _httpClientFactory.CreateClient("nasdaq");
        var reports = new List<EarningsReport>();

        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync($"{baseUrl}/{Uri.EscapeDataString(symbol)}/earnings-surprise", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    reports.AddRange(Parse(symbol, json));
                }
                else
                {
                    _logger.LogInformation("Nasdaq earnings for {Symbol} returned {Status}; skipping.", symbol, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Fetching earnings dates for {Symbol} failed; skipping.", symbol);
            }

            await Task.Delay(PerSymbolDelay, cancellationToken);
        }

        _logger.LogInformation("Fetched {Count} earnings dates across {Symbols} symbols.", reports.Count, symbols.Count);
        return reports;
    }

    /// <summary>Pulls (dateReported, fiscalQtrEnd) out of Nasdaq's earningsSurpriseTable rows.</summary>
    public static IReadOnlyList<EarningsReport> Parse(string symbol, string json)
    {
        var reports = new List<EarningsReport>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("earningsSurpriseTable", out var table) || table.ValueKind != JsonValueKind.Object
            || !table.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return reports;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("dateReported", out var reported)
                && reported.ValueKind == JsonValueKind.String
                && DateTime.TryParseExact(reported.GetString(), "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var quarter = row.TryGetProperty("fiscalQtrEnd", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null;
                reports.Add(new EarningsReport(symbol, date.Date, quarter));
            }
        }

        return reports;
    }
}
