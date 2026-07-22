/* ============================================================================
   Add session range columns to dbo.StockQuotes.

   The Functions app now also captures the day's high, low, and prior close from
   Yahoo on each snapshot, enabling intraday-range and profit-taking analytics.
   Additive and nullable, so existing rows are unaffected (they stay NULL).

   Run this against the stockaggregator database BEFORE deploying the updated
   Functions app, otherwise the new INSERT will fail on the missing columns.
   ============================================================================ */

IF COL_LENGTH('dbo.StockQuotes', 'DayHigh') IS NULL
    ALTER TABLE dbo.StockQuotes ADD DayHigh DECIMAL(18,4) NULL;

IF COL_LENGTH('dbo.StockQuotes', 'DayLow') IS NULL
    ALTER TABLE dbo.StockQuotes ADD DayLow DECIMAL(18,4) NULL;

IF COL_LENGTH('dbo.StockQuotes', 'PreviousClose') IS NULL
    ALTER TABLE dbo.StockQuotes ADD PreviousClose DECIMAL(18,4) NULL;
GO
