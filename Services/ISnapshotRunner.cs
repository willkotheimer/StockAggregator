using System.Threading;
using System.Threading.Tasks;

namespace StockAggregator.Services;

public interface ISnapshotRunner
{
    Task<int> RunAsync(string runLabel, CancellationToken cancellationToken = default);
}
