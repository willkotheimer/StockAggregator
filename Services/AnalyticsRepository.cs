using Microsoft.Data.SqlClient;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Reads raw quotes and writes the derived analytics rollups (Functions side —
/// runs as the Functions app identity, which has db_datawriter).
/// </summary>
public sealed class AnalyticsRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AnalyticsRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public sealed record RawQuote(
        string Symbol,
        decimal? Price,
        decimal? ChangePct,
        decimal? DayHigh,
        decimal? DayLow,
        decimal? PreviousClose,
        DateTime CapturedAtUtc);

    public async Task<IReadOnlyList<RawQuote>> GetQuotesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT Symbol, Price, ChangesPercentage, DayHigh, DayLow, PreviousClose, CapturedAtUtc
FROM dbo.StockQuotes
WHERE CapturedAtUtc >= @since
ORDER BY CapturedAtUtc;";

        var rows = new List<RawQuote>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@since", System.Data.SqlDbType.DateTime2).Value = sinceUtc;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RawQuote(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.GetDateTime(6)));
        }

        return rows;
    }

    /// <summary>Idempotent: deletes existing rows for the affected dates, then inserts fresh.</summary>
    public async Task ReplaceDailyStatsAsync(IReadOnlyList<DailySymbolStat> stats, CancellationToken cancellationToken)
    {
        if (stats.Count == 0)
        {
            return;
        }

        var dates = stats.Select(s => s.TradingDate.Date).Distinct().ToList();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var date in dates)
        {
            await using var del = new SqlCommand("DELETE FROM dbo.DailySymbolStats WHERE TradingDate = @d;", connection, tx);
            del.Parameters.Add("@d", System.Data.SqlDbType.Date).Value = date;
            await del.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insert = @"
INSERT INTO dbo.DailySymbolStats
    (Symbol, TradingDate, FirstPrice, LastPrice, DayHigh, DayLow, PreviousClose, ChangePct, IntradayRangePct, MaxSnapshotDrawdownPct, SnapshotCount)
VALUES (@Symbol, @TradingDate, @FirstPrice, @LastPrice, @DayHigh, @DayLow, @PreviousClose, @ChangePct, @IntradayRangePct, @MaxSnapshotDrawdownPct, @SnapshotCount);";

        foreach (var s in stats)
        {
            await using var cmd = new SqlCommand(insert, connection, tx);
            cmd.Parameters.Add("@Symbol", System.Data.SqlDbType.NVarChar, 20).Value = s.Symbol;
            cmd.Parameters.Add("@TradingDate", System.Data.SqlDbType.Date).Value = s.TradingDate.Date;
            cmd.Parameters.Add("@FirstPrice", System.Data.SqlDbType.Decimal).Value = (object?)s.FirstPrice ?? DBNull.Value;
            cmd.Parameters.Add("@LastPrice", System.Data.SqlDbType.Decimal).Value = (object?)s.LastPrice ?? DBNull.Value;
            cmd.Parameters.Add("@DayHigh", System.Data.SqlDbType.Decimal).Value = (object?)s.DayHigh ?? DBNull.Value;
            cmd.Parameters.Add("@DayLow", System.Data.SqlDbType.Decimal).Value = (object?)s.DayLow ?? DBNull.Value;
            cmd.Parameters.Add("@PreviousClose", System.Data.SqlDbType.Decimal).Value = (object?)s.PreviousClose ?? DBNull.Value;
            cmd.Parameters.Add("@ChangePct", System.Data.SqlDbType.Decimal).Value = (object?)s.ChangePct ?? DBNull.Value;
            cmd.Parameters.Add("@IntradayRangePct", System.Data.SqlDbType.Decimal).Value = (object?)s.IntradayRangePct ?? DBNull.Value;
            cmd.Parameters.Add("@MaxSnapshotDrawdownPct", System.Data.SqlDbType.Decimal).Value = (object?)s.MaxSnapshotDrawdownPct ?? DBNull.Value;
            cmd.Parameters.Add("@SnapshotCount", System.Data.SqlDbType.Int).Value = s.SnapshotCount;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task LogRunAsync(
        string runType,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        int rowsWritten,
        string status,
        string? message,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT INTO dbo.AnalyticsRun (RunType, StartedAtUtc, CompletedAtUtc, RowsWritten, Status, Message)
VALUES (@RunType, @StartedAtUtc, @CompletedAtUtc, @RowsWritten, @Status, @Message);";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@RunType", System.Data.SqlDbType.NVarChar, 40).Value = runType;
        cmd.Parameters.Add("@StartedAtUtc", System.Data.SqlDbType.DateTime2).Value = startedAtUtc;
        cmd.Parameters.Add("@CompletedAtUtc", System.Data.SqlDbType.DateTime2).Value = completedAtUtc;
        cmd.Parameters.Add("@RowsWritten", System.Data.SqlDbType.Int).Value = rowsWritten;
        cmd.Parameters.Add("@Status", System.Data.SqlDbType.NVarChar, 20).Value = status;
        cmd.Parameters.Add("@Message", System.Data.SqlDbType.NVarChar, 400).Value = (object?)message ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
