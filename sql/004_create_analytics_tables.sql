/* ============================================================================
   Analytics tables — derived, precomputed rollups over dbo.StockQuotes.

   DailySymbolStats : one row per symbol per trading day (the keystone rollup
                      the other analytics build on).
   AnalyticsRun     : a log of each rollup run, for observability / "computed
                      as of" stamps on the dashboard.

   Both are rebuildable from raw StockQuotes at any time. Run before deploying
   the analytics rollup function.
   ============================================================================ */

IF OBJECT_ID('dbo.DailySymbolStats', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailySymbolStats
    (
        Symbol                 NVARCHAR(20)  NOT NULL,
        TradingDate            DATE          NOT NULL,
        FirstPrice             DECIMAL(18,4) NULL,
        LastPrice              DECIMAL(18,4) NULL,
        DayHigh                DECIMAL(18,4) NULL,
        DayLow                 DECIMAL(18,4) NULL,
        PreviousClose          DECIMAL(18,4) NULL,
        ChangePct              DECIMAL(9,4)  NULL,
        IntradayRangePct       DECIMAL(9,4)  NULL,
        MaxSnapshotDrawdownPct DECIMAL(9,4)  NULL,
        SnapshotCount          INT           NOT NULL,
        ComputedAtUtc          DATETIME2(0)  NOT NULL
            CONSTRAINT DF_DailySymbolStats_ComputedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_DailySymbolStats PRIMARY KEY (Symbol, TradingDate)
    );

    CREATE INDEX IX_DailySymbolStats_TradingDate ON dbo.DailySymbolStats (TradingDate DESC);
END
GO

IF OBJECT_ID('dbo.AnalyticsRun', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AnalyticsRun
    (
        RunId         BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AnalyticsRun PRIMARY KEY,
        RunType       NVARCHAR(40)  NOT NULL,
        AsOfDate      DATE          NULL,
        StartedAtUtc  DATETIME2(0)  NOT NULL,
        CompletedAtUtc DATETIME2(0) NULL,
        RowsWritten   INT           NULL,
        Status        NVARCHAR(20)  NOT NULL,
        Message       NVARCHAR(400) NULL
    );
END
GO
