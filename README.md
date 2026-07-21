# StockAggregator

Azure Functions app (.NET 10, isolated worker) that pulls stock quotes from
[Yahoo Finance](https://finance.yahoo.com) on a schedule and writes them to SQL Server.

## What it does

Four timer-triggered functions fire on trading days (Mon–Fri), each capturing a
snapshot of every configured symbol:

| Function          | Central time | CRON (`sec min hour day month dow`) |
| ----------------- | ------------ | ----------------------------------- |
| `Snapshot_0830CT` | 08:30        | `0 30 8 * * 1-5`                    |
| `Snapshot_1100CT` | 11:00        | `0 0 11 * * 1-5`                    |
| `Snapshot_1300CT` | 13:00        | `0 0 13 * * 1-5`                    |
| `Snapshot_1430CT` | 14:30        | `0 30 14 * * 1-5`                   |

Each run: fetch quotes for the comma-separated `StockSymbols` list → deserialize →
insert one row per symbol into `dbo.StockQuotes`, tagged with the UTC capture time and
a run label (e.g. `08:30 CT`).

## Project layout

```
Program.cs                         DI wiring (HttpClient + services)
Functions/MarketSnapshotFunctions  The four timer triggers
Services/QuoteFetcher              Calls Yahoo Finance, parses into StockQuote
Services/QuoteRepository           Parametrized inserts into SQL Server
Services/SnapshotRunner            Orchestrates fetch + save
Models/StockQuote                  The quote entity
sql/001_create_StockQuotes.sql     Database + table DDL (run once)
```

## Configuration (app settings)

| Setting               | Purpose                                                        |
| --------------------- | ------------------------------------------------------------- |
| `StockSymbols`        | Comma-separated tickers, e.g. `NVDA,AAPL,MSFT` (non-US need a Yahoo suffix, e.g. `7203.T`) |
| `SqlConnectionString` | SQL Server connection string. Uses Microsoft Entra — no password (see [README-Azure.md](README-Azure.md)) |
| `YahooChartBaseUrl`   | Optional. Defaults to `https://query1.finance.yahoo.com/v8/finance/chart` |
| `WEBSITE_TIME_ZONE`   | Set to `Central Standard Time` so timers run on CT + follow DST |

**Locally** these live in `.env` (git-ignored — never committed). Copy the template
and fill it in:

```
cp .env.example .env
```

`Program.cs` loads `.env` into the environment at startup, so `IConfiguration` picks
the values up like any other setting. The file is absent in Azure, where the loader is
a no-op and the values come from Function App application settings instead (sourced
from your DevOps pipeline variables).

`local.settings.json` (also git-ignored) is kept to just the two Functions runtime keys
— `AzureWebJobsStorage` and `FUNCTIONS_WORKER_RUNTIME` — so that secrets have exactly
one home locally: `.env`.

When you add a new setting, add it to both `.env` and `.env.example` (real value in the
former, placeholder in the latter).

## A note on the data source

Quotes come from Yahoo Finance's public **chart** endpoint
(`/v8/finance/chart/{symbol}`) — no API key or quota. It's one request per symbol;
`QuoteFetcher` reads `regularMarketPrice`, `regularMarketVolume` and
`chartPreviousClose` from the response's `meta` block and computes the change percent.
A symbol that fails is logged and skipped so one bad ticker doesn't sink the run.
Non-US symbols need a Yahoo suffix (e.g. `7203.T`, `ASML.AS`); a browser-like
`User-Agent` is required and set on the HttpClient in `Program.cs`.

## Run locally

1. Create the database: run `sql/001_create_StockQuotes.sql`.
2. `cp .env.example .env` and fill in real values.
3. Install Azure Functions Core Tools (`func`) if you don't have it:
   `npm i -g azure-functions-core-tools@4 --unsafe-perm true`
4. `func start`

To test a single run without waiting for the schedule, you can temporarily add
`RunOnStartup = true` to one `TimerTrigger`, or invoke it via the local admin endpoint.

## Deploy

Publish as a normal .NET isolated Function App. Set the app settings above in the
Function App configuration (values sourced from your DevOps pipeline variables).
