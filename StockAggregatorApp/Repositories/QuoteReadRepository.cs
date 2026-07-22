using Microsoft.Data.SqlClient;
using StockAggregatorApp.Data;
using StockAggregatorApp.Models;

namespace StockAggregatorApp.Repositories;

/// <summary>Read-only access to dbo.StockQuotes.</summary>
public sealed class QuoteReadRepository : IQuoteReadRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public QuoteReadRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<QuoteRecord>> GetSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT Symbol, Price, ChangesPercentage, Volume, CapturedAtUtc, RunLabel
FROM dbo.StockQuotes
WHERE CapturedAtUtc >= @since
ORDER BY CapturedAtUtc;";

        var results = new List<QuoteRecord>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@since", System.Data.SqlDbType.DateTime2).Value = sinceUtc;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<DateTime>> GetAvailableDatesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT DISTINCT CAST(CapturedAtUtc AS date) AS D FROM dbo.StockQuotes ORDER BY D;";

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

    public async Task<IReadOnlyList<QuoteRecord>> GetForDatesAsync(
        IReadOnlyList<DateTime> dates,
        CancellationToken cancellationToken = default)
    {
        if (dates.Count == 0)
        {
            return Array.Empty<QuoteRecord>();
        }

        var paramNames = dates.Select((_, i) => $"@d{i}").ToList();
        var sql = $@"
SELECT Symbol, Price, ChangesPercentage, Volume, CapturedAtUtc, RunLabel
FROM dbo.StockQuotes
WHERE CAST(CapturedAtUtc AS date) IN ({string.Join(", ", paramNames)})
ORDER BY CapturedAtUtc;";

        var results = new List<QuoteRecord>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        for (var i = 0; i < dates.Count; i++)
        {
            command.Parameters.Add(paramNames[i], System.Data.SqlDbType.Date).Value = dates[i].Date;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    private static QuoteRecord Read(SqlDataReader reader) => new(
        Symbol: reader.GetString(0),
        Price: reader.IsDBNull(1) ? null : reader.GetDecimal(1),
        ChangePercent: reader.IsDBNull(2) ? null : reader.GetDecimal(2),
        Volume: reader.IsDBNull(3) ? null : reader.GetInt64(3),
        CapturedAtUtc: reader.GetDateTime(4),
        RunLabel: reader.GetString(5));
}
