using Microsoft.Data.SqlClient;
using StockAggregatorApp.Data;

namespace StockAggregatorApp.Repositories;

/// <summary>One symbol's daily close on a trading date, ascending by date.</summary>
public sealed record DailyClose(DateTime TradingDate, decimal Close);

/// <summary>One symbol's full daily bar. Open/High/Low may be null on sparse rows.</summary>
public sealed record DailyBar(DateTime TradingDate, decimal? Open, decimal? High, decimal? Low, decimal Close);

public interface IDailyOhlcReadRepository
{
    Task<IReadOnlyList<DailyClose>> GetClosesAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyBar>> GetBarsAsync(string symbol, CancellationToken cancellationToken = default);
}

/// <summary>Read-only access to dbo.DailyOhlc — the backfilled daily history the
/// rebound analysis scans. Only date + close are needed (close-anchored episodes).</summary>
public sealed class DailyOhlcReadRepository : IDailyOhlcReadRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public DailyOhlcReadRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DailyClose>> GetClosesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT TradingDate, [Close]
FROM dbo.DailyOhlc
WHERE Symbol = @symbol AND [Close] IS NOT NULL
ORDER BY TradingDate;";

        var rows = new List<DailyClose>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@symbol", System.Data.SqlDbType.NVarChar, 20).Value = symbol;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DailyClose(reader.GetDateTime(0), reader.GetDecimal(1)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<DailyBar>> GetBarsAsync(string symbol, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT TradingDate, [Open], High, Low, [Close]
FROM dbo.DailyOhlc
WHERE Symbol = @symbol AND [Close] IS NOT NULL
ORDER BY TradingDate;";

        var rows = new List<DailyBar>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@symbol", System.Data.SqlDbType.NVarChar, 20).Value = symbol;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DailyBar(
                reader.GetDateTime(0),
                reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.GetDecimal(4)));
        }

        return rows;
    }
}
