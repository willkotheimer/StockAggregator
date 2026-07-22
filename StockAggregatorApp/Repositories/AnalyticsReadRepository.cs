using Microsoft.Data.SqlClient;
using StockAggregatorApp.Data;

namespace StockAggregatorApp.Repositories;

public sealed record DailyChange(string Symbol, decimal? ChangePct);

public interface IAnalyticsReadRepository
{
    Task<DateTime?> GetLatestTradingDateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyChange>> GetChangesForDateAsync(DateTime tradingDate, CancellationToken cancellationToken = default);
}

/// <summary>Read-only access to the precomputed dbo.DailySymbolStats rollup.</summary>
public sealed class AnalyticsReadRepository : IAnalyticsReadRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AnalyticsReadRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DateTime?> GetLatestTradingDateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT MAX(TradingDate) FROM dbo.DailySymbolStats;", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DateTime d ? d : null;
    }

    public async Task<IReadOnlyList<DailyChange>> GetChangesForDateAsync(DateTime tradingDate, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT Symbol, ChangePct FROM dbo.DailySymbolStats WHERE TradingDate = @d;";

        var rows = new List<DailyChange>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@d", System.Data.SqlDbType.Date).Value = tradingDate.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DailyChange(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDecimal(1)));
        }

        return rows;
    }
}
