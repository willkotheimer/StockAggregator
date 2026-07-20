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

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/api/manual-snapshot");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected success status from deployment smoke test, but got {(int)response.StatusCode}. Body: {body}");
        Assert.Contains("Snapshot run completed", body);
    }
}
