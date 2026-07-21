using Microsoft.Data.SqlClient;

namespace StockAggregator.Services;

/// <summary>
/// Chooses the Microsoft Entra authentication mode for the SQL connection so the
/// same base connection string works both locally and in Azure. In Azure the app
/// authenticates as its managed identity (Active Directory Default); locally it
/// signs in interactively (Active Directory Interactive), which avoids the flaky
/// DefaultAzureCredential/az-CLI chain on developer machines.
/// </summary>
public static class SqlConnectionStringResolver
{
    /// <summary>
    /// Returns the connection string with an Entra <c>Authentication</c> mode filled in.
    /// If the base string already specifies an authentication mode, or uses SQL/Windows
    /// auth (User ID/Password or Trusted_Connection), it is returned unchanged.
    /// </summary>
    /// <param name="baseConnectionString">The configured SqlConnectionString.</param>
    /// <param name="authOverride">Optional explicit mode, e.g. "Active Directory Managed Identity".</param>
    /// <param name="runningInAzure">True when hosted in Azure (App Service/Functions).</param>
    public static string Resolve(string baseConnectionString, string? authOverride, bool runningInAzure)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString);

        var usesExplicitAuth =
            builder.Authentication != SqlAuthenticationMethod.NotSpecified
            || builder.IntegratedSecurity
            || !string.IsNullOrEmpty(builder.UserID)
            || !string.IsNullOrEmpty(builder.Password);

        if (!usesExplicitAuth)
        {
            builder.Authentication = ResolveAuthMethod(authOverride, runningInAzure);
        }

        return builder.ConnectionString;
    }

    private static SqlAuthenticationMethod ResolveAuthMethod(string? authOverride, bool runningInAzure)
    {
        if (!string.IsNullOrWhiteSpace(authOverride)
            && Enum.TryParse<SqlAuthenticationMethod>(authOverride.Replace(" ", string.Empty), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return runningInAzure
            ? SqlAuthenticationMethod.ActiveDirectoryDefault
            : SqlAuthenticationMethod.ActiveDirectoryInteractive;
    }

    /// <summary>
    /// True when running in Azure App Service / Functions, detected via a host-injected
    /// environment variable that is absent on developer machines.
    /// </summary>
    public static bool IsRunningInAzure() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
}
