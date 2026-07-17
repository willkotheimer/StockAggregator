# StockAggregator

Azure Functions app (.NET 10, isolated worker) that pulls batch stock quotes from
[Financial Modeling Prep](https://financialmodelingprep.com) on a schedule and writes
them to SQL Server.

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
Services/QuoteFetcher              Calls FMP, deserializes into StockQuote
Services/QuoteRepository           Parametrized inserts into SQL Server
Services/SnapshotRunner            Orchestrates fetch + save
Models/StockQuote                  The quote entity
sql/001_create_StockQuotes.sql     Database + table DDL (run once)
```

## Configuration (app settings)

| Setting               | Purpose                                                        |
| --------------------- | ------------------------------------------------------------- |
| `StockSymbols`        | Comma-separated tickers, e.g. `NVDA,AAPL,MSFT`                 |
| `FinancialDataApiKey` | FMP API key **(secret)**                                      |
| `SqlConnectionString` | SQL Server connection string **(secret)**                    |
| `FmpQuoteBaseUrl`     | Optional. Defaults to `https://financialmodelingprep.com/api/v3/quote` |
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

## A note on the API endpoint

The default endpoint is the **batch quote** endpoint (`/api/v3/quote/{symbols}`),
because it accepts a comma-separated symbol list and returns exactly the fields we
store (`price`, `changesPercentage`, `volume`). The `/stable/profile` endpoint you
linked returns a company profile for a single symbol, which is a different shape. If
you'd rather use a different FMP endpoint, override `FmpQuoteBaseUrl` — no code change
needed, as long as the JSON is an array of objects with `symbol`/`price`/
`changesPercentage`/`volume` fields.

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
