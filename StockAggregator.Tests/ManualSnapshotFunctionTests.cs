using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StockAggregator.Functions;
using StockAggregator.Services;
using Xunit;

namespace StockAggregator.Tests;

public class ManualSnapshotFunctionTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsOk_WhenRunnerPersistsRows()
    {
        var function = new ManualSnapshotFunction(new FakeSnapshotRunner(1), NullLogger<ManualSnapshotFunction>.Instance);

        var result = await function.InvokeAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Snapshot run completed", result.Body);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsInternalServerError_WhenRunnerPersistsNoRows()
    {
        var function = new ManualSnapshotFunction(new FakeSnapshotRunner(0), NullLogger<ManualSnapshotFunction>.Instance);

        var result = await function.InvokeAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("No rows were persisted", result.Body);
    }

    private sealed class FakeSnapshotRunner : ISnapshotRunner
    {
        private readonly int _rowsPersisted;

        public FakeSnapshotRunner(int rowsPersisted)
        {
            _rowsPersisted = rowsPersisted;
        }

        public Task<int> RunAsync(string runLabel, CancellationToken cancellationToken = default)
            => Task.FromResult(_rowsPersisted);
    }
}
