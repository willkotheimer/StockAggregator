/* ============================================================================
   dbo.DailyOhlc — one daily OHLC bar per symbol per trading day.

   Populated by the historical backfill (HistoricalBackfillFunction, POST
   /api/backfill), which pulls ~2 years of daily bars per symbol from Yahoo in a
   single call each. This is the source of truth for *historical* daily prices,
   kept deliberately separate from dbo.StockQuotes (your live 4x/day intraday
   snapshots) and dbo.DailySymbolStats (the snapshot-derived rollup), so the two
   resolutions never collide.

   Daily bars carry no intraday path, so path-dependent metrics
   (MaxSnapshotDrawdownPct, precise gain-before-pullback) are not reconstructable
   from history — those remain forward-only from the live snapshots. Everything
   the rebound base-rate analysis needs is daily resolution, which this provides.

   Idempotent + rebuildable: re-running the backfill overwrites a symbol's rows.

   Run this against the stockaggregator database BEFORE deploying the updated
   Functions app, otherwise the backfill INSERT will fail on the missing table.
   ============================================================================ */

USE StockAggregator;
GO

IF OBJECT_ID('dbo.DailyOhlc', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailyOhlc
    (
        Symbol       NVARCHAR(20)  NOT NULL,
        TradingDate  DATE          NOT NULL,
        -- Open/Close are reserved words in T-SQL, hence the bracketing.
        [Open]       DECIMAL(18,4) NULL,
        High         DECIMAL(18,4) NULL,
        Low          DECIMAL(18,4) NULL,
        [Close]      DECIMAL(18,4) NULL,
        -- Split/dividend-adjusted close from Yahoo (indicators.adjclose).
        AdjClose     DECIMAL(18,4) NULL,
        Volume       BIGINT        NULL,
        -- Where the bar came from, e.g. 'Yahoo'. Leaves room for other sources.
        Source       NVARCHAR(20)  NOT NULL
            CONSTRAINT DF_DailyOhlc_Source DEFAULT ('Yahoo'),
        CreatedAt    DATETIME2(0)  NOT NULL
            CONSTRAINT DF_DailyOhlc_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_DailyOhlc PRIMARY KEY (Symbol, TradingDate)
    );

    -- Common query pattern: "all symbols' bars for a date range", date-ordered.
    CREATE INDEX IX_DailyOhlc_TradingDate ON dbo.DailyOhlc (TradingDate DESC);
END
GO
