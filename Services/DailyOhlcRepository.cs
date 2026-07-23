using Microsoft.Data.SqlClient;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Reads and writes dbo.DailyOhlc — the historical daily-bar store the backfill
/// populates. Writes run as the Functions app identity (db_datawriter).
/// </summary>
public sealed class DailyOhlcRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public DailyOhlcRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// How many bars each symbol already has. Lets the backfill skip symbols that
    /// are already done, so it's resumable across repeated (chunked) invocations.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetRowCountsBySymbolAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT Symbol, COUNT(*) FROM dbo.DailyOhlc GROUP BY Symbol;";

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    /// <summary>
    /// The earliest bar date each symbol currently has. Lets the backfill skip a
    /// symbol only when its history already reaches the requested range (depth-aware
    /// resume), so a deeper re-pull (e.g. 2y → 5y) actually advances instead of
    /// treating any existing rows as "done".
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DateTime>> GetEarliestDatesBySymbolAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT Symbol, MIN(TradingDate) FROM dbo.DailyOhlc GROUP BY Symbol;";

        var earliest = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            earliest[reader.GetString(0)] = reader.GetDateTime(1);
        }

        return earliest;
    }

    /// <summary>
    /// Idempotently replaces one symbol's bars: deletes the symbol's existing rows,
    /// then inserts the fresh set, in a single transaction. Scoped to one symbol so
    /// a re-run overwrites only that symbol and never touches others.
    /// </summary>
    public async Task<int> ReplaceSymbolBarsAsync(
        string symbol,
        IReadOnlyList<DailyOhlcBar> bars,
        CancellationToken cancellationToken)
    {
        if (bars.Count == 0)
        {
            return 0;
        }

        const string insert = @"
INSERT INTO dbo.DailyOhlc (Symbol, TradingDate, [Open], High, Low, [Close], AdjClose, Volume)
VALUES (@Symbol, @TradingDate, @Open, @High, @Low, @Close, @AdjClose, @Volume);";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var del = new SqlCommand("DELETE FROM dbo.DailyOhlc WHERE Symbol = @Symbol;", connection, tx))
            {
                del.Parameters.Add("@Symbol", System.Data.SqlDbType.NVarChar, 20).Value = symbol;
                await del.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var bar in bars)
            {
                await using var cmd = new SqlCommand(insert, connection, tx);
                cmd.Parameters.Add("@Symbol", System.Data.SqlDbType.NVarChar, 20).Value = bar.Symbol;
                cmd.Parameters.Add("@TradingDate", System.Data.SqlDbType.Date).Value = bar.TradingDate.Date;
                cmd.Parameters.Add("@Open", System.Data.SqlDbType.Decimal).Value = (object?)bar.Open ?? DBNull.Value;
                cmd.Parameters.Add("@High", System.Data.SqlDbType.Decimal).Value = (object?)bar.High ?? DBNull.Value;
                cmd.Parameters.Add("@Low", System.Data.SqlDbType.Decimal).Value = (object?)bar.Low ?? DBNull.Value;
                cmd.Parameters.Add("@Close", System.Data.SqlDbType.Decimal).Value = (object?)bar.Close ?? DBNull.Value;
                cmd.Parameters.Add("@AdjClose", System.Data.SqlDbType.Decimal).Value = (object?)bar.AdjClose ?? DBNull.Value;
                cmd.Parameters.Add("@Volume", System.Data.SqlDbType.BigInt).Value = (object?)bar.Volume ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return bars.Count;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
