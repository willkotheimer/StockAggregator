using Microsoft.Data.SqlClient;
using StockAggregatorApp.Data;

namespace StockAggregatorApp.Repositories;

public interface IProjectionReadRepository
{
    /// <summary>Every stored earnings report date, by symbol.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<DateTime>>> GetEarningsDatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads for the projection test beyond the price history it shares with the dashboard.</summary>
public sealed class ProjectionReadRepository : IProjectionReadRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public ProjectionReadRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DateTime>>> GetEarningsDatesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT Symbol, ReportDate FROM dbo.EarningsDates ORDER BY Symbol, ReportDate;";

        var bySymbol = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var symbol = reader.GetString(0);
            if (!bySymbol.TryGetValue(symbol, out var dates))
            {
                bySymbol[symbol] = dates = new List<DateTime>();
            }
            dates.Add(reader.GetDateTime(1));
        }

        return bySymbol.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<DateTime>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }
}
