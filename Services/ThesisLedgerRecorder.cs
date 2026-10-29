using System.Data;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockAggregator.Services;

/// <summary>
/// Stores the day's thesis scores in dbo.ThesisLedger: GETs the web app's
/// /api/projections/ledger (the web app is read-only on SQL) and upserts one row per
/// variant. Upsert, not insert, so a manual resend on the same day just refreshes it.
/// </summary>
public sealed class ThesisLedgerRecorder
{
    private const string DefaultWebAppUrl = "https://stockaggregator-web.azurewebsites.net/";

    private readonly SqlConnectionFactory _connectionFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ThesisLedgerRecorder> _logger;

    public ThesisLedgerRecorder(
        SqlConnectionFactory connectionFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<ThesisLedgerRecorder> logger)
    {
        _connectionFactory = connectionFactory;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>Returns how many rows were written.</summary>
    public async Task<int> RecordAsync(CancellationToken cancellationToken)
    {
        var entries = await FetchAsync(cancellationToken);

        const string sql = @"
MERGE dbo.ThesisLedger AS t
USING (SELECT @AsOfDate AS AsOfDate, @Thesis AS Thesis, @Variant AS Variant) AS s
    ON t.AsOfDate = s.AsOfDate AND t.Thesis = s.Thesis AND t.Variant = s.Variant
WHEN MATCHED THEN UPDATE SET
    Label = @Label, Tests = @Tests, HitRate = @HitRate, BaselineHitRate = @BaselineHitRate, Edge = @Edge,
    OlderTests = @OlderTests, OlderEdge = @OlderEdge, NewerTests = @NewerTests, NewerEdge = @NewerEdge,
    Status = @Status, CodeVersion = @CodeVersion, RecordedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (AsOfDate, Thesis, Variant, Label, Tests, HitRate, BaselineHitRate, Edge,
     OlderTests, OlderEdge, NewerTests, NewerEdge, Status, CodeVersion)
    VALUES (@AsOfDate, @Thesis, @Variant, @Label, @Tests, @HitRate, @BaselineHitRate, @Edge,
     @OlderTests, @OlderEdge, @NewerTests, @NewerEdge, @Status, @CodeVersion);";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        foreach (var e in entries)
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@AsOfDate", SqlDbType.Date).Value = e.AsOf.Date;
            command.Parameters.Add("@Thesis", SqlDbType.NVarChar, 40).Value = e.Thesis;
            command.Parameters.Add("@Variant", SqlDbType.NVarChar, 100).Value = e.Variant;
            command.Parameters.Add("@Label", SqlDbType.NVarChar, 200).Value = e.Label;
            command.Parameters.Add("@Tests", SqlDbType.Int).Value = e.Tests;
            command.Parameters.Add("@HitRate", SqlDbType.Float).Value = (object?)e.HitRate ?? DBNull.Value;
            command.Parameters.Add("@BaselineHitRate", SqlDbType.Float).Value = (object?)e.BaselineHitRate ?? DBNull.Value;
            command.Parameters.Add("@Edge", SqlDbType.Float).Value = (object?)e.Edge ?? DBNull.Value;
            command.Parameters.Add("@OlderTests", SqlDbType.Int).Value = e.OlderTests;
            command.Parameters.Add("@OlderEdge", SqlDbType.Float).Value = (object?)e.OlderEdge ?? DBNull.Value;
            command.Parameters.Add("@NewerTests", SqlDbType.Int).Value = e.NewerTests;
            command.Parameters.Add("@NewerEdge", SqlDbType.Float).Value = (object?)e.NewerEdge ?? DBNull.Value;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = e.Status;
            command.Parameters.Add("@CodeVersion", SqlDbType.NVarChar, 100).Value =
                e.CodeVersion.Length > 100 ? e.CodeVersion[..100] : e.CodeVersion;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Stored {Count} thesis ledger rows as of {AsOf:yyyy-MM-dd}.",
            entries.Count, entries.Count > 0 ? entries[0].AsOf : null);
        return entries.Count;
    }

    private async Task<IReadOnlyList<LedgerEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        var baseUrl = (_config["DigestWebAppUrl"] ?? DefaultWebAppUrl).TrimEnd('/');
        var client = _httpClientFactory.CreateClient();
        // The scorecard is warmed at 10:45 CT, but a restarted web app recomputes it first.
        client.Timeout = TimeSpan.FromSeconds(180);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/projections/ledger");
        if (_config["WebAppApiKey"] is { Length: > 0 } key)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<LedgerEntry>>(cancellationToken) ?? [];
    }

    /// <summary>The web app's ThesisLedgerEntry, as JSON.</summary>
    private sealed record LedgerEntry(
        [property: JsonPropertyName("asOf")] DateTime AsOf,
        [property: JsonPropertyName("thesis")] string Thesis,
        [property: JsonPropertyName("variant")] string Variant,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("tests")] int Tests,
        [property: JsonPropertyName("hitRate")] double? HitRate,
        [property: JsonPropertyName("baselineHitRate")] double? BaselineHitRate,
        [property: JsonPropertyName("edge")] double? Edge,
        [property: JsonPropertyName("olderTests")] int OlderTests,
        [property: JsonPropertyName("olderEdge")] double? OlderEdge,
        [property: JsonPropertyName("newerTests")] int NewerTests,
        [property: JsonPropertyName("newerEdge")] double? NewerEdge,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("codeVersion")] string CodeVersion);
}
