using System;
using System.Net;
using Xunit;

namespace StockAggregator.Tests;

public class DeploymentSmokeTests
{
    [Fact]
    public async Task ManualSnapshotEndpoint_ReturnsOk_WhenDeployed()
    {
        var baseUrl = Environment.GetEnvironmentVariable("FUNCTION_APP_URL");
        Assert.False(string.IsNullOrWhiteSpace(baseUrl), "FUNCTION_APP_URL must be set for the deployment smoke test.");

        baseUrl = baseUrl.Trim();
        Assert.DoesNotContain(" ", baseUrl, StringComparison.Ordinal);
        Assert.StartsWith("https://", baseUrl, StringComparison.OrdinalIgnoreCase);
        Assert.True(Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri),
            $"FUNCTION_APP_URL must be an absolute URI. Current value: '{baseUrl}'");

        var endpoint = new Uri(baseUri, "/api/manual-snapshot");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var response = await client.GetAsync(endpoint);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode,
            $"Expected success status from deployment smoke test, but got {(int)response.StatusCode}. FUNCTION_APP_URL: '{baseUrl}'. Endpoint: {endpoint}. Body: {body}");
        Assert.Contains("Snapshot run completed", body);
    }
}
