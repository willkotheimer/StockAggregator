using Microsoft.Data.SqlClient;
using StockAggregator.Services;
using Xunit;

namespace StockAggregator.Tests;

public class SqlConnectionStringResolverTests
{
    private const string BaseConnection =
        "Server=tcp:example.database.windows.net,1433;Initial Catalog=StockAggregator;Encrypt=True;";

    [Fact]
    public void Resolve_UsesManagedIdentity_WhenRunningInAzure()
    {
        var result = SqlConnectionStringResolver.Resolve(BaseConnection, authOverride: null, runningInAzure: true);

        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryDefault, new SqlConnectionStringBuilder(result).Authentication);
    }

    [Fact]
    public void Resolve_UsesInteractive_WhenRunningLocally()
    {
        var result = SqlConnectionStringResolver.Resolve(BaseConnection, authOverride: null, runningInAzure: false);

        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryInteractive, new SqlConnectionStringBuilder(result).Authentication);
    }

    [Fact]
    public void Resolve_HonorsOverride_WithSpacesAndCasing()
    {
        var result = SqlConnectionStringResolver.Resolve(BaseConnection, "Active Directory Managed Identity", runningInAzure: false);

        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity, new SqlConnectionStringBuilder(result).Authentication);
    }

    [Fact]
    public void Resolve_LeavesExplicitAuthMode_Untouched()
    {
        var explicitAuth = BaseConnection + "Authentication=Active Directory Interactive;";

        // Even though runningInAzure would otherwise pick Default, the explicit mode wins.
        var result = SqlConnectionStringResolver.Resolve(explicitAuth, authOverride: null, runningInAzure: true);

        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryInteractive, new SqlConnectionStringBuilder(result).Authentication);
    }

    [Fact]
    public void Resolve_LeavesSqlAuth_Untouched()
    {
        const string sqlAuth = "Server=localhost;Database=StockAggregator;User ID=sa;Password=p@ss;";

        var result = SqlConnectionStringResolver.Resolve(sqlAuth, authOverride: null, runningInAzure: false);

        var builder = new SqlConnectionStringBuilder(result);
        Assert.Equal(SqlAuthenticationMethod.NotSpecified, builder.Authentication);
        Assert.Equal("sa", builder.UserID);
    }

    [Fact]
    public void Resolve_LeavesTrustedConnection_Untouched()
    {
        const string trusted = "Server=localhost;Database=StockAggregator;Trusted_Connection=True;";

        var result = SqlConnectionStringResolver.Resolve(trusted, authOverride: null, runningInAzure: false);

        Assert.Equal(SqlAuthenticationMethod.NotSpecified, new SqlConnectionStringBuilder(result).Authentication);
    }
}
