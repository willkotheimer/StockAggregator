# Azure deployment guide

## 1. Create Azure SQL Database

1. Create an Azure SQL logical server in Azure Portal.
2. Create a SQL database (for example, `StockAggregator`).
3. Configure firewall rules so your client or deployment environment can reach it.
4. Run the SQL script in [sql/001_create_StockQuotes.sql](sql/001_create_StockQuotes.sql) against the database.

If you prefer to use a contained serverless SQL database, the app still works as long as it can reach the server and the table exists.

## 2. Create the Function App

1. In Azure Portal, create a Function App using the .NET isolated worker runtime.
2. Choose a hosting plan and region.
3. Under Configuration, add the following app settings:
   - `SqlConnectionString`: the SQL connection string to the Azure SQL database
   - `FinancialDataApiKey`: your Financial Modeling Prep API key
   - `StockSymbols`: comma-separated ticker list, for example `NVDA,AAPL,MSFT`
   - `FmpQuoteBaseUrl`: optional, defaults to `https://financialmodelingprep.com/api/v3/quote`
   - `WEBSITE_TIME_ZONE`: `Central Standard Time`
   - `FUNCTIONS_WORKER_RUNTIME`: `dotnet-isolated`
4. Publish the app from Visual Studio, VS Code, or the pipeline in [azure-pipelines.yml](azure-pipelines.yml).

## 3. Verify the schedule

The four timer functions are already configured in [Functions/MarketSnapshotFunctions.cs](Functions/MarketSnapshotFunctions.cs):
- 08:30 CT
- 11:00 CT
- 13:00 CT
- 14:30 CT

These run on trading days only (Monday-Friday) because the cron expression uses `1-5` in the day-of-week field.

## 4. Azure DevOps pipeline

This repository contains [azure-pipelines.yml](azure-pipelines.yml), which:
- restores and builds the project
- publishes the function app
- deploys it to Azure Functions using an Azure service connection

Set these pipeline variables:
- `AzureServiceConnection`
- `FunctionAppName`
- `SqlConnectionString`
- `FinancialDataApiKey`
- `StockSymbols`
- `FmpQuoteBaseUrl`

## 5. Test it

After deployment:
1. Open the Function App in Azure Portal.
2. Go to Functions and confirm the four timer functions appear.
3. Trigger one manually from the portal if needed.
4. Query the database to confirm rows are landing in `dbo.StockQuotes`.
