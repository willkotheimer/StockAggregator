using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Persists fetched quotes to SQL Server. One row per symbol per capture.
/// </summary>
public class QuoteRepository
{
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(SqlConnectionFactory connectionFactory, ILogger<QuoteRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Verifies the database is reachable and the Entra token is accepted, without
    /// writing anything. Used by the health endpoint. Throws if the check fails.
    /// </summary>
    public async Task CheckDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1;", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>
    /// True if any quotes are already stored for the given run label on the UTC day
    /// of <paramref name="dayUtc"/>. The catch-up uses this to skip a slot that
    /// already landed (on time or via an earlier catch-up), so it never duplicates.
    /// </summary>
    public async Task<bool> HasQuotesForRunAsync(DateTime dayUtc, string runLabel, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT TOP 1 1 FROM dbo.StockQuotes
WHERE RunLabel = @RunLabel AND CapturedAtUtc >= @DayStart AND CapturedAtUtc < @DayEnd;";

        var dayStart = dayUtc.Date;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@RunLabel", System.Data.SqlDbType.NVarChar, 20).Value = runLabel;
        command.Parameters.Add("@DayStart", System.Data.SqlDbType.DateTime2).Value = dayStart;
        command.Parameters.Add("@DayEnd", System.Data.SqlDbType.DateTime2).Value = dayStart.AddDays(1);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    public async Task<int> SaveAsync(
        IReadOnlyList<StockQuote> quotes,
        DateTime capturedAtUtc,
        string runLabel,
        CancellationToken cancellationToken = default)
    {
        if (quotes.Count == 0)
        {
            _logger.LogWarning("No quotes to save for run {RunLabel}.", runLabel);
            return 0;
        }

        const string sql = @"
INSERT INTO dbo.StockQuotes (Symbol, Price, ChangesPercentage, Volume, DayHigh, DayLow, PreviousClose, CapturedAtUtc, RunLabel)
VALUES (@Symbol, @Price, @ChangesPercentage, @Volume, @DayHigh, @DayLow, @PreviousClose, @CapturedAtUtc, @RunLabel);";

        _logger.LogInformation("Attempting to save {Count} quotes for run {RunLabel}.", quotes.Count, runLabel);

        try
        {
            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                foreach (var quote in quotes)
                {
                    await using var command = new SqlCommand(sql, connection, transaction);
                    command.Parameters.Add("@Symbol", System.Data.SqlDbType.NVarChar, 20).Value =
                        quote.Symbol;
                    command.Parameters.Add("@Price", System.Data.SqlDbType.Decimal).Value =
                        (object?)quote.Price ?? DBNull.Value;
                    command.Parameters.Add("@ChangesPercentage", System.Data.SqlDbType.Decimal).Value =
                        (object?)quote.ChangesPercentage ?? DBNull.Value;
                    command.Parameters.Add("@Volume", System.Data.SqlDbType.BigInt).Value =
                        (object?)quote.Volume ?? DBNull.Value;
                    command.Parameters.Add("@DayHigh", System.Data.SqlDbType.Decimal).Value =
                        (object?)quote.DayHigh ?? DBNull.Value;
                    command.Parameters.Add("@DayLow", System.Data.SqlDbType.Decimal).Value =
                        (object?)quote.DayLow ?? DBNull.Value;
                    command.Parameters.Add("@PreviousClose", System.Data.SqlDbType.Decimal).Value =
                        (object?)quote.PreviousClose ?? DBNull.Value;
                    command.Parameters.Add("@CapturedAtUtc", System.Data.SqlDbType.DateTime2).Value =
                        capturedAtUtc;
                    command.Parameters.Add("@RunLabel", System.Data.SqlDbType.NVarChar, 20).Value =
                        runLabel;

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Saved {Count} quotes for run {RunLabel}.", quotes.Count, runLabel);
                return quotes.Count;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to save {Count} quotes for run {RunLabel}.", quotes.Count, runLabel);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SQL connection or begin transaction for run {RunLabel}.", runLabel);
            throw;
        }
    }
}
