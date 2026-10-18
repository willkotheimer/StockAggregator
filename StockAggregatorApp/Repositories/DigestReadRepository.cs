using Microsoft.Data.SqlClient;
using StockAggregatorApp.Data;

namespace StockAggregatorApp.Repositories;

/// <summary>One raw snapshot row with the intraday fields the digest needs.</summary>
public sealed record SnapshotQuote(
    string Symbol,
    decimal? Price,
    decimal? ChangePct,
    decimal? DayHigh,
    decimal? DayLow,
    decimal? PreviousClose,
    DateTime CapturedAtUtc,
    string RunLabel);

public sealed record RollupRun(DateTime StartedAtUtc, string Status);

public interface IDigestReadRepository
{
    /// <summary>The most recent day that has any snapshot rows.</summary>
    Task<DateTime?> GetLatestSnapshotDateAsync(CancellationToken cancellationToken = default);

    /// <summary>Every snapshot row captured on the given (UTC) date.</summary>
    Task<IReadOnlyList<SnapshotQuote>> GetSnapshotsForDateAsync(DateTime date, CancellationToken cancellationToken = default);

    Task<RollupRun?> GetLastRollupRunAsync(CancellationToken cancellationToken = default);

    /// <summary>The last date in dbo.DailyOhlc — how far the manual backfill has reached.</summary>
    Task<DateTime?> GetHistoryThroughAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The reads behind the midday digest that the dashboard doesn't already need:
/// today's snapshots with their high/low/previous close, and the pipeline-health
/// facts for the operator's data check.
/// </summary>
public sealed class DigestReadRepository : IDigestReadRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public DigestReadRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DateTime?> GetLatestSnapshotDateAsync(CancellationToken cancellationToken = default)
    {
        var result = await ScalarAsync("SELECT MAX(CAST(CapturedAtUtc AS date)) FROM dbo.StockQuotes;", cancellationToken);
        return result is DateTime d ? d : null;
    }

    public async Task<IReadOnlyList<SnapshotQuote>> GetSnapshotsForDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT Symbol, Price, ChangesPercentage, DayHigh, DayLow, PreviousClose, CapturedAtUtc, RunLabel
FROM dbo.StockQuotes
WHERE CapturedAtUtc >= @start AND CapturedAtUtc < @end
ORDER BY CapturedAtUtc;";

        var rows = new List<SnapshotQuote>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@start", System.Data.SqlDbType.DateTime2).Value = date.Date;
        command.Parameters.Add("@end", System.Data.SqlDbType.DateTime2).Value = date.Date.AddDays(1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SnapshotQuote(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.GetDateTime(6),
                reader.GetString(7)));
        }

        return rows;
    }

    public async Task<RollupRun?> GetLastRollupRunAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT TOP 1 StartedAtUtc, Status
FROM dbo.AnalyticsRun
WHERE RunType = 'DailyRollup'
ORDER BY StartedAtUtc DESC;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new RollupRun(reader.GetDateTime(0), reader.GetString(1))
            : null;
    }

    public async Task<DateTime?> GetHistoryThroughAsync(CancellationToken cancellationToken = default)
    {
        var result = await ScalarAsync("SELECT MAX(TradingDate) FROM dbo.DailyOhlc;", cancellationToken);
        return result is DateTime d ? d : null;
    }

    private async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
