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
INSERT INTO dbo.StockQuotes (Symbol, Price, ChangesPercentage, Volume, CapturedAtUtc, RunLabel)
VALUES (@Symbol, @Price, @ChangesPercentage, @Volume, @CapturedAtUtc, @RunLabel);";

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
