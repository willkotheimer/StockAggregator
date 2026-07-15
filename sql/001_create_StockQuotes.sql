/* ============================================================================
   StockAggregator schema
   Run this once against your SQL Server database (the one named in the
   SqlConnectionString app setting).
   ============================================================================ */

IF DB_ID('StockAggregator') IS NULL
BEGIN
    CREATE DATABASE StockAggregator;
END
GO

USE StockAggregator;
GO

IF OBJECT_ID('dbo.StockQuotes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockQuotes
    (
        Id                BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_StockQuotes PRIMARY KEY,
        Symbol            NVARCHAR(20)   NOT NULL,
        Price             DECIMAL(18,4)  NULL,
        ChangesPercentage DECIMAL(9,4)   NULL,
        Volume            BIGINT         NULL,
        -- UTC instant the snapshot was captured (the app writes DateTime.UtcNow).
        CapturedAtUtc     DATETIME2(0)   NOT NULL,
        -- Which scheduled run produced the row, e.g. '08:30 CT'.
        RunLabel          NVARCHAR(20)   NOT NULL,
        -- Server-side insert time, handy for auditing.
        CreatedAt         DATETIME2(0)   NOT NULL
            CONSTRAINT DF_StockQuotes_CreatedAt DEFAULT (SYSUTCDATETIME())
    );

    -- Common query pattern: "give me a symbol's history, newest first."
    CREATE INDEX IX_StockQuotes_Symbol_CapturedAtUtc
        ON dbo.StockQuotes (Symbol, CapturedAtUtc DESC);

    -- Common query pattern: "everything captured in one run."
    CREATE INDEX IX_StockQuotes_CapturedAtUtc
        ON dbo.StockQuotes (CapturedAtUtc DESC);
END
GO
