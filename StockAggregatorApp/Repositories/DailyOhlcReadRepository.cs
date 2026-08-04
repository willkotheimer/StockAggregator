using Microsoft.Data.SqlClient;
using StockAggregatorApp.Data;

namespace StockAggregatorApp.Repositories;

/// <summary>One symbol's daily close on a trading date, ascending by date.</summary>
public sealed record DailyClose(DateTime TradingDate, decimal Close);

/// <summary>One symbol's full daily bar. Open/High/Low may be null on sparse rows.</summary>
public sealed record DailyBar(DateTime TradingDate, decimal? Open, decimal? High, decimal? Low, decimal Close);

/// <summary>One symbol's close on one trading date, for the multi-symbol range read.</summary>
public sealed record SymbolClose(string Symbol, DateTime TradingDate, decimal Close);

public interface IDailyOhlcReadRepository
{
    Task<IReadOnlyList<DailyClose>> GetClosesAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyBar>> GetBarsAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Closes for many symbols across an inclusive date range, in one round trip.</summary>
    Task<IReadOnlyList<SymbolClose>> GetClosesForSymbolsAsync(
        IReadOnlyList<string> symbols, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>Distinct trading dates present in the history — the range picker's calendar.</summary>
    Task<IReadOnlyList<DateTime>> GetTradingDatesAsync(CancellationToken cancellationToken = default);
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

    public async Task<IReadOnlyList<SymbolClose>> GetClosesForSymbolsAsync(
        IReadOnlyList<string> symbols, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0)
        {
            return Array.Empty<SymbolClose>();
        }

        // The symbol list comes from server-side config, never from the request, but
        // it is still parameterised rather than interpolated.
        var names = symbols.Select((_, i) => $"@s{i}").ToList();
        var sql = $@"
SELECT Symbol, TradingDate, [Close]
FROM dbo.DailyOhlc
WHERE [Close] IS NOT NULL
  AND TradingDate >= @from AND TradingDate <= @to
  AND Symbol IN ({string.Join(", ", names)})
ORDER BY Symbol, TradingDate;";

        var rows = new List<SymbolClose>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@from", System.Data.SqlDbType.Date).Value = from.Date;
        command.Parameters.Add("@to", System.Data.SqlDbType.Date).Value = to.Date;
        for (var i = 0; i < symbols.Count; i++)
        {
            command.Parameters.Add($"@s{i}", System.Data.SqlDbType.NVarChar, 20).Value = symbols[i];
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SymbolClose(reader.GetString(0), reader.GetDateTime(1), reader.GetDecimal(2)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<DateTime>> GetTradingDatesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT DISTINCT TradingDate
FROM dbo.DailyOhlc
WHERE [Close] IS NOT NULL
ORDER BY TradingDate;";

        var dates = new List<DateTime>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dates.Add(reader.GetDateTime(0));
        }

        return dates;
    }
}
