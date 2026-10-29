# Research log

Every idea we test for an edge goes here: what it claims, how it is scored, how it is doing,
and what we decided about it. The point is to spot shared traits between near misses from
*different* ideas, and to prune ideas on a record instead of a feeling.

Numbers live in `dbo.ThesisLedger` (one row per variant per day, written after the midday email).
This file holds the reasoning the table can't.

## How a variant is judged

All theses report in one shape (`ThesisLedgerEntry`): tests, hit rate, the no-pattern hit rate on
the same tests, and the edge in the older and newer halves of the history. Status rules are fixed
in `ThesisLedger.Status` and were set **before** any ledger history existed:

| Status | Rule |
|---|---|
| Passed | +10 pts over no pattern in both halves, ≥100 tests, ≥30 per half |
| Close | not passed, but ≥ +5 pts in both halves on ≥30 tests each |
| TooFewTests | ≥ +5 pts overall, but a half has <30 tests |
| NoEdge | everything else |

Individual guesses are not stored. The tests are deterministic, so `CodeVersion` (the build's git
commit) plus the price history rebuilds every guess behind a row.

```sql
-- Near misses, newest first, across every thesis
SELECT AsOfDate, Thesis, Variant, Tests, Edge, OlderEdge, NewerEdge, Status
FROM dbo.ThesisLedger
WHERE Status IN ('Passed', 'Close', 'TooFewTests')
ORDER BY AsOfDate DESC, Edge DESC;

-- One variant's history: is it improving, fading, or never worked?
SELECT AsOfDate, Tests, OlderEdge, NewerEdge, Status, CodeVersion
FROM dbo.ThesisLedger
WHERE Thesis = 'follow-on' AND Variant = '5d'
ORDER BY AsOfDate;
```

## Theses

### price-pattern — the projection test (since 2026-10-19)

Does a pattern (alone or paired) place a stock's price 20 trading days ahead inside a fixed window
more often than knowing nothing? Code: `ProjectionTest`, `ProjectionPatterns`. 32 variants listed.

**As of 2026-10-28:** none close. The best variant is `far-below-high+strong-group` (a stock 10%+ below its
high while its group leads): +5.4 older, 0.0 newer. Windows that cross earnings land 42% vs 52%
for the rest.

### follow-on — big day in one fund, then another (since 2026-10-29)

Within 5/10/15 trading days of one fund's big day (|z| ≥ 2 against its own past year), does
another fund have its own big day more often than in any span that long? A link only makes guesses
once a binomial test says p ≤ 0.01. Code: `FollowOnTest`. Variants: `5d`, `10d`, `15d`.

**As of 2026-10-28:** 477 guesses since Sep 2024; 30% came true vs 16% base.

| Variant | Older | Newer | Status |
|---|---|---|---|
| 5d | +12.0 (90) | +9.6 (256) | **Close** — newer half 0.4 pts short of passing |
| 10d | −22 (6) | +24 (105) | TooFewTests |
| 15d | — (0) | +32 (20) | TooFewTests |

The edge in the last 20 days fell to +9, from +20 in the 20 days before.

## Observations across theses

- **Rebound flavor in both.** Most strong follow-on links go *big down day → another fund's big
  up day* (e.g. XLC down → XLK up within 15 days, 16 of 21). The best price pattern is also a
  rebound setup (far below the high, inside a strong group). If later near misses keep sharing
  "after a sharp drop," that is the common cause to chase.
- **Earnings hurt the price guesses.** Worth checking whether follow-on guesses that cross a big
  member's earnings date do worse too.

## Open questions / next tests

- **A tougher follow-on baseline.** The current baseline is "any span." Volatility clusters, so after
  *any* fund's big day every fund is more likely to have one. That alone could explain much of the
  edge without any specific link. A fairer baseline is "spans that start on a day some other fund
  had a big day." If the edge survives that, the links are real.
- Tag follow-on guesses with quarter end / earnings like the price guesses, so the two theses can
  be compared on the same events.
- When a second thesis reaches Close, compare the *guesses* behind both rows (rebuilt from each
  row's CodeVersion): same dates? same sectors? same market moves before them?

## Pruning (proposed — not yet agreed)

- **Prune candidate:** a variant with ≥300 tests that has been NoEdge at every month-end check for
  6 months straight.
- **Modify instead** when one half is strong and the other isn't. That points to a regime change, not
  a dead idea; note what changed.
- Pruning stops new rows; old rows are never deleted. Record each decision below.

## Decisions

| Date | Thesis / variant | Decision | Why |
|---|---|---|---|
| 2026-10-29 | follow-on | Added | Window-based follow-up check across funds |
