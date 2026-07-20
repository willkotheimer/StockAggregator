using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StockAggregator.Functions;
using StockAggregator.Services;
using Xunit;

namespace StockAggregator.Tests;

public class ManualSnapshotFunctionTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsOk_WhenRunnerSucceeds()
    {
        var function = new ManualSnapshotFunction(new FakeSnapshotRunner(), NullLogger<ManualSnapshotFunction>.Instance);

        var result = await function.InvokeAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Snapshot run completed.", result.Body);
    }

    private sealed class FakeSnapshotRunner : ISnapshotRunner
    {
        public Task RunAsync(string runLabel, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
