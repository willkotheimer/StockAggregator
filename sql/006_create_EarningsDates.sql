/* ============================================================================
   dbo.EarningsDates — when each company reported earnings.

   Filled weekly by the EarningsRefresh function from Nasdaq's earnings-surprise
   data, which lists each company's last four reports. Rows accumulate: a report
   stays after it drops out of Nasdaq's four, so real history grows over time.
   The projection test fills in older years by estimate (EarningsCalendar), and
   those estimates are computed at read time, never stored here.

   Funds (SMH, XLK, …) don't report earnings and have no rows.

   Run this against the stockaggregator database BEFORE deploying the web app or
   Functions code that reads or writes it. Existing db_datareader/db_datawriter
   role grants (002) cover the new table.
   ============================================================================ */

IF OBJECT_ID('dbo.EarningsDates', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EarningsDates
    (
        Symbol            NVARCHAR(20)  NOT NULL,
        ReportDate        DATE          NOT NULL,
        -- The fiscal quarter the report covers, as the source labels it, e.g. 'Jun 2026'.
        FiscalQuarterEnd  NVARCHAR(20)  NULL,
        Source            NVARCHAR(20)  NOT NULL,
        FetchedAtUtc      DATETIME2(0)  NOT NULL
            CONSTRAINT DF_EarningsDates_FetchedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_EarningsDates PRIMARY KEY (Symbol, ReportDate)
    );
END
GO
