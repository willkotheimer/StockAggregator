using System.Threading;
using System.Threading.Tasks;

namespace StockAggregator.Services;

public interface ISnapshotRunner
{
    Task RunAsync(string runLabel, CancellationToken cancellationToken = default);
}
