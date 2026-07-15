using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockAggregator.Models;

namespace StockAggregator.Services;

/// <summary>
/// Persists fetched quotes to SQL Server. One row per symbol per capture.
/// </summary>
public class QuoteRepository
{
    private readonly IConfiguration _config;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(IConfiguration config, ILogger<QuoteRepository> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SaveAsync(
        IReadOnlyList<StockQuote> quotes,
        DateTime capturedAtUtc,
        string runLabel,
        CancellationToken cancellationToken = default)
    {
        if (quotes.Count == 0)
        {
            _logger.LogWarning("No quotes to save for run {RunLabel}.", runLabel);
            return;
        }

        var connectionString = _config["SqlConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("App setting 'SqlConnectionString' is not set.");
        }

        const string sql = @"
INSERT INTO dbo.StockQuotes (Symbol, Price, ChangesPercentage, Volume, CapturedAtUtc, RunLabel)
VALUES (@Symbol, @Price, @ChangesPercentage, @Volume, @CapturedAtUtc, @RunLabel);";

        await using var connection = new SqlConnection(connectionString);
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
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
