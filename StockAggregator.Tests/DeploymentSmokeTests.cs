using System;
using System.Net;
using Xunit;

namespace StockAggregator.Tests;

public class DeploymentSmokeTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsOk_WhenDeployed()
    {
        var baseUrl = Environment.GetEnvironmentVariable("FUNCTION_APP_URL");
        Assert.False(string.IsNullOrWhiteSpace(baseUrl), "FUNCTION_APP_URL must be set for the deployment smoke test.");

        baseUrl = baseUrl.Trim();
        Assert.DoesNotContain(" ", baseUrl, StringComparison.Ordinal);
        Assert.StartsWith("https://", baseUrl, StringComparison.OrdinalIgnoreCase);
        Assert.True(Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri),
            $"FUNCTION_APP_URL must be an absolute URI. Current value: '{baseUrl}'");

        // Hits the health probe (verifies app + DB) rather than the snapshot
        // endpoint, so a deploy doesn't write stock rows to the database.
        var endpoint = new Uri(baseUri, "/api/health");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var response = await client.GetAsync(endpoint);
        var body = await response.Content.ReadAsStringAsync();
        var headers = string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(";", h.Value)}"));

        Assert.True(response.IsSuccessStatusCode,
            $"Expected success status from deployment smoke test, but got {(int)response.StatusCode}. FUNCTION_APP_URL: '{baseUrl}'. Endpoint: {endpoint}. Headers: {headers}. Body: {body}");
        Assert.Contains("Healthy", body);
    }
}
