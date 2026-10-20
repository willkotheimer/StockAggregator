# Azure deployment guide

## Authentication: Microsoft Entra (no SQL passwords)

The app connects to Azure SQL using **Microsoft Entra ID**, not a SQL
username/password. The connection string uses `Authentication=Active Directory
Default`, which resolves to:

- the Function App's **system-assigned managed identity** when running in Azure, and
- your **`az login`** credentials when running locally.

No secret lives in the connection string. `Microsoft.Data.SqlClient` handles the
token exchange, so no extra NuGet package is required.

## 1. Create Azure SQL Database

1. Create an Azure SQL logical server in Azure Portal (or via [infra/main.bicep](infra/main.bicep)).
2. Create a SQL database (for example, `StockAggregator`).
3. **Set a Microsoft Entra admin on the server** (SQL server → Microsoft Entra ID →
   Set admin). This is required before any Entra login works. The Bicep template
   sets this from the `sqlAadAdminLogin` / `sqlAadAdminObjectId` parameters.
4. Configure firewall rules so your client or deployment environment can reach it.
5. Run [sql/001_create_StockQuotes.sql](sql/001_create_StockQuotes.sql) against the database.

## 2. Create the Function App

1. In Azure Portal, create a Function App using the .NET isolated worker runtime.
2. Choose a hosting plan and region.
3. **Enable the system-assigned managed identity** (Function App → Identity →
   System assigned → On). The Bicep template already does this.
4. Under Configuration, add the following app settings:
   - `SqlConnectionString`: Entra connection string, e.g.
     `Server=tcp:<server>.database.windows.net,1433;Initial Catalog=StockAggregator;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;`
   - `StockSymbols`: comma-separated ticker list, for example `NVDA,AAPL,MSFT`
     (non-US symbols need a Yahoo suffix, e.g. `7203.T`, `ASML.AS`)
   - `YahooChartBaseUrl`: optional, defaults to `https://query1.finance.yahoo.com/v8/finance/chart`
   - `WEBSITE_TIME_ZONE`: `Central Standard Time`
   - `FUNCTIONS_WORKER_RUNTIME`: `dotnet-isolated`
5. Publish the app from Visual Studio, VS Code, or the pipeline in [azure-pipelines.yml](azure-pipelines.yml).

## 2a. Grant the managed identity access in the database

This is the step most people miss — the connection fails until the identity is a
database user. Connected to the `StockAggregator` database **as the Entra admin**,
run [sql/002_grant_entra_users.sql](sql/002_grant_entra_users.sql) after replacing
`<function-app-name>` with your Function App's name (the DB user name equals the
app name for a system-assigned identity). Add your own UPN there too for local dev.

## 3. Verify the schedule

The four timer functions are already configured in [Functions/MarketSnapshotFunctions.cs](Functions/MarketSnapshotFunctions.cs):
- 08:30 CT
- 11:00 CT
- 13:00 CT
- 14:30 CT

These run on trading days only (Monday-Friday) because the cron expression uses `1-5` in the day-of-week field.

## 3a. Midday digest email

`DailyDigest` ([Functions/DailyDigestFunction.cs](Functions/DailyDigestFunction.cs)) runs at
11:30 CT on weekdays. It fetches fresh prices and saves them as an `11:30 CT` snapshot,
asks the web app for the email (`GET /api/digest`), and sends it through Azure
Communication Services Email. If the quotes show the market is closed (a holiday), it
sends nothing.

1. Deploy the email resources with [infra/email.bicep](infra/email.bicep). It is separate
   from `main.bicep` so it never rewrites the Function App's other settings:
   ```bash
   az deployment group create -g <rg> -f infra/email.bicep \
     -p functionAppPrincipalId=$(az functionapp identity show -n <function-app> -g <rg> --query principalId -o tsv)
   ```
   The template also gives the Function App's managed identity permission to send, so
   there is no email key or password.
2. Add these Function App settings, using the deployment's two outputs:
   - `CommunicationServicesEndpoint`: e.g. `https://<acs>.unitedstates.communication.azure.com`
   - `DigestSenderAddress`: e.g. `DoNotReply@<id>.azurecomm.net`
   - `DigestRecipients`: comma-separated addresses
   - `DigestWebAppUrl`: optional, defaults to `https://stockaggregator-web.azurewebsites.net/`
   - `WebAppApiKey`: only if the web app has an `ApiKey` set
3. Check the email without sending: open `https://<web-app>/digest/preview`
   (optionally `?date=yyyy-MM-dd`).
4. Send a test: `POST /api/digest/send` on the Function App, with the function key. By
   default this resends from stored prices, so it works on a weekend. Add `fresh=true`
   to run the full scheduled path, or `date=yyyy-MM-dd` to build the email for a past day.

The bottom of the email reports the projection test (`GET /api/projections` on the web
app): each pattern, alone or paired, guesses the price 20 trading days ahead, and the
email shows how often the guesses land against a no-pattern baseline. It marks guesses
whose window crossed a quarter end or an earnings report. Earnings dates come from
`EarningsRefresh` (Saturdays 07:00 CT, or `POST /api/earnings/refresh`) into
`dbo.EarningsDates`. Run [sql/006_create_EarningsDates.sql](sql/006_create_EarningsDates.sql)
before deploying code that uses it.

## 4. Azure DevOps pipeline

This repository contains [azure-pipelines.yml](azure-pipelines.yml), which:
- restores and builds the project
- publishes the function app
- deploys it to Azure Functions using an Azure service connection

Set these pipeline variables:
- `AzureServiceConnection`
- `FunctionAppName`
- `SqlConnectionString`
- `StockSymbols`

## 5. Test it

After deployment:
1. Open the Function App in Azure Portal.
2. Go to Functions and confirm the four timer functions appear.
3. Trigger one manually from the portal if needed.
4. Query the database to confirm rows are landing in `dbo.StockQuotes`.
