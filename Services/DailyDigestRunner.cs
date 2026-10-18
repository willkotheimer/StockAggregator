using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockAggregator.Services;

/// <summary>What one digest run did, for the log and the manual endpoint's reply.</summary>
public sealed record DigestRunResult(bool Sent, string Message);

/// <summary>
/// The midday digest, end to end:
///
///   1. fetch fresh prices and save them as the "11:30 CT" snapshot (skipped when the
///      market is closed — a holiday sends nothing)
///   2. ask the web app for the email (GET /api/digest), where all analysis and wording live
///   3. send it through Azure Communication Services Email
///
/// Sending authenticates with the Function App's managed identity (the Azure CLI login
/// locally), the same way the SQL connection does, so there is no email secret anywhere.
/// </summary>
public sealed class DailyDigestRunner
{
    public const string RunLabel = "11:30 CT";

    private const string DefaultWebAppUrl = "https://stockaggregator-web.azurewebsites.net/";

    private readonly QuoteFetcher _fetcher;
    private readonly QuoteRepository _repository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DailyDigestRunner> _logger;

    public DailyDigestRunner(
        QuoteFetcher fetcher,
        QuoteRepository repository,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<DailyDigestRunner> logger)
    {
        _fetcher = fetcher;
        _repository = repository;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <param name="fetchFresh">Fetch and save prices first. The schedule always does;
    /// a manual test send can skip it to resend from what's already stored.</param>
    /// <param name="date">The day to build the email for; null means the latest day with prices.</param>
    public async Task<DigestRunResult> RunAsync(bool fetchFresh, DateTime? date, CancellationToken cancellationToken)
    {
        if (fetchFresh)
        {
            var quotes = await _fetcher.FetchAsync(cancellationToken);
            if (!MarketSession.IsOpenToday(quotes, DateTimeOffset.UtcNow))
            {
                _logger.LogInformation("Market is closed today; no digest sent.");
                return new DigestRunResult(false, "Market is closed today, so no digest was sent.");
            }

            await _repository.SaveAsync(quotes, DateTime.UtcNow, RunLabel, cancellationToken);
        }

        var digest = await GetDigestAsync(date, cancellationToken);
        if (!digest.HasData)
        {
            _logger.LogWarning("Digest for {Date} has no prices; not sending.", digest.TradingDate);
            return new DigestRunResult(false, $"No prices for {digest.TradingDate}, so no digest was sent.");
        }

        var recipients = Recipients();
        await SendAsync(digest, recipients, cancellationToken);

        _logger.LogInformation("Sent digest {Subject} to {Count} recipient(s).", digest.Subject, recipients.Count);
        return new DigestRunResult(true, $"Sent \"{digest.Subject}\" to {recipients.Count} recipient(s).");
    }

    private async Task<DigestPayload> GetDigestAsync(DateTime? date, CancellationToken cancellationToken)
    {
        var baseUrl = (_config["DigestWebAppUrl"] ?? DefaultWebAppUrl).TrimEnd('/');
        var url = date is { } d ? $"{baseUrl}/api/digest?date={d:yyyy-MM-dd}" : $"{baseUrl}/api/digest";

        var client = _httpClientFactory.CreateClient();
        // The web app is kept warm, but its first SQL query can wake the serverless DB.
        client.Timeout = TimeSpan.FromSeconds(120);

        // One retry covers a web app that was restarting or a DB that was resuming.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (_config["WebAppApiKey"] is { Length: > 0 } key)
                {
                    request.Headers.Add("X-Api-Key", key);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DigestPayload>(cancellationToken)
                    ?? throw new InvalidOperationException("The web app returned an empty digest.");
            }
            catch (Exception ex) when (attempt < 2 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Fetching the digest from {Url} failed; retrying once.", url);
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            }
        }
    }

    private IReadOnlyList<string> Recipients()
    {
        var recipients = (_config["DigestRecipients"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return recipients.Length > 0
            ? recipients
            : throw new InvalidOperationException("App setting 'DigestRecipients' is not set.");
    }

    private async Task SendAsync(DigestPayload digest, IReadOnlyList<string> recipients, CancellationToken cancellationToken)
    {
        var endpoint = _config["CommunicationServicesEndpoint"]
            ?? throw new InvalidOperationException("App setting 'CommunicationServicesEndpoint' is not set.");
        var sender = _config["DigestSenderAddress"]
            ?? throw new InvalidOperationException("App setting 'DigestSenderAddress' is not set.");

        var client = new EmailClient(new Uri(endpoint), CreateCredential());
        var message = new EmailMessage(
            senderAddress: sender,
            recipients: new EmailRecipients(recipients.Select(r => new EmailAddress(r)).ToList()),
            content: new EmailContent(digest.Subject) { Html = digest.Html, PlainText = digest.Text });

        // Waits for the service to accept or reject the message, so a failure fails the run.
        var operation = await client.SendAsync(WaitUntil.Completed, message, cancellationToken);
        if (operation.Value.Status != EmailSendStatus.Succeeded)
        {
            throw new InvalidOperationException($"Email send finished with status {operation.Value.Status}.");
        }
    }

    private static TokenCredential CreateCredential() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"))
            ? new AzureCliCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);

    /// <summary>The parts of the web app's DigestResponse the sender needs.</summary>
    private sealed record DigestPayload(
        [property: JsonPropertyName("tradingDate")] string? TradingDate,
        [property: JsonPropertyName("hasData")] bool HasData,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);
}
