using Microsoft.Data.SqlClient;
using StockAggregatorApp.Data;

namespace StockAggregatorApp.Repositories;

public sealed record DailyChange(string Symbol, decimal? ChangePct);

public interface IAnalyticsReadRepository
{
    Task<DateTime?> GetLatestTradingDateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyChange>> GetChangesForDateAsync(DateTime tradingDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Each symbol's last snapshot price per trading day across a range. This is the
    /// self-maintaining source for recent dates: the nightly rollup keeps it current,
    /// where dbo.DailyOhlc only moves when the manual backfill is run.
    /// </summary>
    Task<IReadOnlyList<SymbolClose>> GetLastPricesForSymbolsAsync(
        IReadOnlyList<string> symbols, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>Distinct trading dates in the rollup — the recent half of the range picker.</summary>
    Task<IReadOnlyList<DateTime>> GetRollupDatesAsync(CancellationToken cancellationToken = default);
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

    public async Task<IReadOnlyList<SymbolClose>> GetLastPricesForSymbolsAsync(
        IReadOnlyList<string> symbols, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0)
        {
            return Array.Empty<SymbolClose>();
        }

        // Symbols come from server-side config, never the request, but are still parameterised.
        var names = symbols.Select((_, i) => $"@s{i}").ToList();
        var sql = $@"
SELECT Symbol, TradingDate, LastPrice
FROM dbo.DailySymbolStats
WHERE LastPrice IS NOT NULL
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

    public async Task<IReadOnlyList<DateTime>> GetRollupDatesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT DISTINCT TradingDate
FROM dbo.DailySymbolStats
WHERE LastPrice IS NOT NULL
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
