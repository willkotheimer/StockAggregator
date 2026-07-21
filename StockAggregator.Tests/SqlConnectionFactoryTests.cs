using StockAggregator.Services;
using Xunit;

namespace StockAggregator.Tests;

public class SqlConnectionFactoryTests
{
    [Fact]
    public void NeedsEntraToken_True_ForPasswordlessEntraString()
    {
        const string entra =
            "Server=tcp:example.database.windows.net,1433;Initial Catalog=StockAggregator;Encrypt=True;";

        Assert.True(SqlConnectionFactory.NeedsEntraToken(entra));
    }

    [Fact]
    public void NeedsEntraToken_False_WhenSqlCredentialsPresent()
    {
        const string sqlAuth = "Server=localhost;Database=StockAggregator;User ID=sa;Password=p@ss;";

        Assert.False(SqlConnectionFactory.NeedsEntraToken(sqlAuth));
    }

    [Fact]
    public void NeedsEntraToken_False_ForTrustedConnection()
    {
        const string trusted = "Server=localhost;Database=StockAggregator;Trusted_Connection=True;";

        Assert.False(SqlConnectionFactory.NeedsEntraToken(trusted));
    }

    [Fact]
    public void NeedsEntraToken_False_WhenAuthenticationKeywordSet()
    {
        // An explicit Authentication mode means SqlClient handles auth; don't attach a token.
        const string explicitAuth =
            "Server=tcp:example.database.windows.net,1433;Initial Catalog=StockAggregator;Encrypt=True;Authentication=Active Directory Default;";

        Assert.False(SqlConnectionFactory.NeedsEntraToken(explicitAuth));
    }
}
