using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace StockAggregator.Services;

/// <summary>
/// Stores earnings report dates in dbo.EarningsDates. Insert-only: a date already
/// stored is left alone, so rows accumulate past Nasdaq's four-quarter view and the
/// real history grows week by week.
/// </summary>
public sealed class EarningsRepository
{
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly ILogger<EarningsRepository> _logger;

    public EarningsRepository(SqlConnectionFactory connectionFactory, ILogger<EarningsRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>Adds any dates not already stored; returns how many were new.</summary>
    public async Task<int> AddNewAsync(IReadOnlyList<EarningsReport> reports, CancellationToken cancellationToken = default)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.EarningsDates WHERE Symbol = @Symbol AND ReportDate = @ReportDate)
    INSERT INTO dbo.EarningsDates (Symbol, ReportDate, FiscalQuarterEnd, Source)
    VALUES (@Symbol, @ReportDate, @FiscalQuarterEnd, 'Nasdaq');";

        var added = 0;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        foreach (var report in reports)
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Symbol", System.Data.SqlDbType.NVarChar, 20).Value = report.Symbol;
            command.Parameters.Add("@ReportDate", System.Data.SqlDbType.Date).Value = report.ReportDate;
            command.Parameters.Add("@FiscalQuarterEnd", System.Data.SqlDbType.NVarChar, 20).Value =
                (object?)report.FiscalQuarterEnd ?? DBNull.Value;
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Stored {Added} new earnings dates of {Total} fetched.", added, reports.Count);
        return added;
    }
}
