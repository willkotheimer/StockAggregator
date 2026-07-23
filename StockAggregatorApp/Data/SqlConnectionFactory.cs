using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;

namespace StockAggregatorApp.Data;

/// <summary>
/// Creates <see cref="SqlConnection"/>s that authenticate with Microsoft Entra via
/// an access token (SqlClient's <c>AccessTokenCallback</c>). In Azure the token
/// comes from the App Service's managed identity; locally from the Azure CLI login
/// (<c>az login</c>). Mirrors the Functions app's connection strategy — the Entra
/// providers are no longer built into Microsoft.Data.SqlClient 7.0. A connection
/// string that already carries SQL/Windows credentials is used unchanged.
/// </summary>
public sealed class SqlConnectionFactory
{
    private const string DatabaseScope = "https://database.windows.net/.default";

    private readonly string _connectionString;
    private readonly bool _useEntraToken;
    private readonly TokenCredential? _credential;
    private readonly SqlRetryLogicBaseProvider _openRetryProvider;

    public SqlConnectionFactory(IConfiguration config)
    {
        _connectionString = config["SqlConnectionString"]
            ?? throw new InvalidOperationException("Configuration value 'SqlConnectionString' is not set.");

        _useEntraToken = NeedsEntraToken(_connectionString);
        if (_useEntraToken)
        {
            _credential = CreateCredential(IsRunningInAzure());
        }

        _openRetryProvider = BuildOpenRetryProvider();
    }

    public SqlConnection Create()
    {
        var connection = new SqlConnection(_connectionString)
        {
            // Retry connection opens on transient faults. The database is Azure SQL
            // serverless and auto-pauses when idle; the first connection after a pause
            // triggers a resume that can outlast the connection timeout, so a lone
            // attempt fails (a paused DB was returning 500s on dashboard reads).
            RetryLogicProvider = _openRetryProvider,
        };

        if (_useEntraToken && _credential is { } credential)
        {
            connection.AccessTokenCallback = async (_, cancellationToken) =>
            {
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(new[] { DatabaseScope }),
                    cancellationToken);
                return new SqlAuthenticationToken(token.Token, token.ExpiresOn);
            };
        }

        return connection;
    }

    /// <summary>Exponential-backoff retry for connection opens, tuned for serverless
    /// resume (40613 "database not currently available", the 4919x busy codes, and
    /// -2 connection timeout).</summary>
    private static SqlRetryLogicBaseProvider BuildOpenRetryProvider()
    {
        var options = new SqlRetryLogicOption
        {
            NumberOfTries = 4,
            DeltaTime = TimeSpan.FromSeconds(4),
            MaxTimeInterval = TimeSpan.FromSeconds(20),
            TransientErrors = new List<int>
            {
                -2, 40613, 40197, 40501, 40540, 42108, 42109,
                49918, 49919, 49920, 4060, 4221, 1205, 233, 10928, 10929, 10053, 10054, 10060, 64, 20, 0,
            },
        };

        return SqlConfigurableRetryFactory.CreateExponentialRetryProvider(options);
    }

    private static bool NeedsEntraToken(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return builder.Authentication == SqlAuthenticationMethod.NotSpecified
            && !builder.IntegratedSecurity
            && string.IsNullOrEmpty(builder.UserID)
            && string.IsNullOrEmpty(builder.Password);
    }

    private static TokenCredential CreateCredential(bool runningInAzure) =>
        runningInAzure
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
            : new AzureCliCredential();

    private static bool IsRunningInAzure() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
}
