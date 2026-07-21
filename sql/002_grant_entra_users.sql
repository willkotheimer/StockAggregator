/* ============================================================================
   Grant Microsoft Entra principals access to the StockAggregator database.

   Entra (Azure AD) authentication needs each principal to exist as a database
   user before it can connect. A correct connection string alone is NOT enough.

   Run this against the StockAggregator database while connected AS THE ENTRA
   ADMIN of the SQL server (the one set in infra/main.bicep). For example:
     sqlcmd -S <your-sql-server>.database.windows.net -d StockAggregator \
            -G -U <entra-admin-upn>          (interactive Entra login)

   Replace the two names below:
     - <function-app-name>  : the Function App's name. For a system-assigned
                              managed identity the DB user name equals the app
                              name. This is the identity your deployed app uses.
     - <your-user-upn>      : your own Entra sign-in (e.g. you@contoso.com), so
                              local `az login` development works too. Optional.
   ============================================================================ */

-- The Function App's managed identity (used by the deployed app). REQUIRED.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'stockaggregator-func')
BEGIN
    CREATE USER [stockaggregator-func] FROM EXTERNAL PROVIDER;
END
GO
ALTER ROLE db_datareader ADD MEMBER [stockaggregator-func];
ALTER ROLE db_datawriter ADD MEMBER [stockaggregator-func];
GO

-- The dashboard web app's managed identity. Read-only, so db_datareader only.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'stockaggregator-web')
BEGIN
    CREATE USER [stockaggregator-web] FROM EXTERNAL PROVIDER;
END
GO
ALTER ROLE db_datareader ADD MEMBER [stockaggregator-web];
GO

-- Your own account is the server's Entra admin, which already maps to dbo, so
-- you do NOT need a database user for local development. Only add one here if you
-- later remove yourself as admin.
