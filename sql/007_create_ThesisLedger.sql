/* ============================================================================
   dbo.ThesisLedger — each thesis variant's score, one row per variant per day.

   Written each weekday by the DailyDigest function (after the email goes out)
   from the web app's GET /api/projections/ledger. One shape for every thesis
   (price patterns, follow-on spans, and whatever comes next), so near misses
   from different ideas can be compared side by side.

   Why store it when the tests can be rerun: a variant that is later changed or
   pruned can't be rerun as it was. CodeVersion names the build that produced
   the row, so the individual guesses behind it can be rebuilt from that commit.

   Rows are never deleted. Pruning a variant just stops new rows; see
   docs/research-log.md.

   Run this against the stockaggregator database BEFORE deploying the Functions
   code that writes it. Existing db_datareader/db_datawriter grants (002) cover it.
   ============================================================================ */

IF OBJECT_ID('dbo.ThesisLedger', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ThesisLedger
    (
        AsOfDate        DATE           NOT NULL,
        Thesis          NVARCHAR(40)   NOT NULL,
        Variant         NVARCHAR(100)  NOT NULL,
        Label           NVARCHAR(200)  NOT NULL,
        Tests           INT            NOT NULL,
        HitRate         FLOAT          NULL,
        BaselineHitRate FLOAT          NULL,
        Edge            FLOAT          NULL,
        OlderTests      INT            NOT NULL,
        OlderEdge       FLOAT          NULL,
        NewerTests      INT            NOT NULL,
        NewerEdge       FLOAT          NULL,
        -- Passed / Close / TooFewTests / NoEdge (ThesisStatus in the web app).
        Status          NVARCHAR(20)   NOT NULL,
        CodeVersion     NVARCHAR(100)  NOT NULL,
        RecordedAtUtc   DATETIME2(0)   NOT NULL
            CONSTRAINT DF_ThesisLedger_RecordedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ThesisLedger PRIMARY KEY (AsOfDate, Thesis, Variant)
    );
END
GO
