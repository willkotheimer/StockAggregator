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

### follow-on — big day in one fund, then another (2026-10-29, pruned 2026-10-30)

Within 5/10/15 trading days of one fund's big day (|z| ≥ 2 against its own past year), does
another fund have its own big day more often than in any span that long? A link only makes guesses
once a binomial test says p ≤ 0.01. Code: `FollowOnTest`. Variants: `5d`, `10d`, `15d`.

**As of 2026-10-28:** 477 guesses since Sep 2024; 30% came true vs 16% base.

| Variant | Older | Newer | Status |
|---|---|---|---|
| 5d | +12.0 (90) | +9.6 (256) | **Close**: newer half 0.4 pts short of passing |
| 10d | −22 (6) | +24 (105) | TooFewTests |
| 15d | — (0) | +32 (20) | TooFewTests |

The edge in the last 20 days fell to +9, from +20 in the 20 days before.

**Bounce check (self-links, 10 days, average over funds):** after a fund's own big down day, it had
a big *up* day 40% of the time and another big *down* day 42%, against a base of about 22% for each.
Both directions rise about equally, which is volatility clustering ("big days follow big days"), not
direction. So part of the raw cross-fund edge is likely the same effect.

### follow-on-relative — the rotation thesis (2026-10-29, pruned 2026-10-30)

Same test, but each fund's daily move is its move **minus the stock funds' average** that day. A
day when everything moves together cancels out; what's left is one fund gaining or losing ground
on the rest: money moving. This is the user's rotation thesis in testable form. Rotation
destinations (XLP, XLU, TLT, GLD) join as leaders and followers but never the average.

**As of 2026-10-28 (stock funds only; destinations not backfilled yet):** 130 guesses since Feb 2025;
33% came true vs 16% base.

| Variant | Older | Newer | Status |
|---|---|---|---|
| 5d | +5.2 (21) | +20.5 (71) | TooFewTests |
| 10d | — (0) | +17.2 (37) | TooFewTests |
| 15d | — (0) | −28.7 (1) | NoEdge |

Strongest relative links differ from the raw ones. XLY falling behind → XLC pulling ahead within 5 days
(11 of 25, 44% vs 14%). XLE pulling ahead → XLY falling behind within 10 days (11 of 22, 50% vs 20%).
That one has an obvious story: energy up means fuel costs up, which hurts consumer spending.

Relative self-links are also about symmetric (34% vs 35% after a big relative down day, base about 23%),
so relative big days cluster too. That means relative links can still pick up some clustering.

### Practice portfolios — forward only (since 2026-10-30)

Not a thesis scored on hit rates, so it has no ledger rows. Eight fixed mixes each put in $250 every
other Friday from Fri Oct 2, 2026, at real closes, in fractional shares. Nothing before Oct 2 counts,
so last year's Sandisk and Micron runs can't flatter anyone. The leaderboard is in the email and at
`GET /api/portfolios`. Code: `PaperPortfolios` (rules, pure) and `PaperPortfolioService` (prices).

| # | Mix |
|---|---|
| 1 | 100% VOO |
| 2 | 50% VOO, 50% SPAXX (4%/yr), with the SPAXX half all into Sandisk on any close below $1,300 and all back above $1,500 |
| 3 | 50% VOO, 25% WMT, 25% XOM |
| 4 | 50% VOO, 50% MU |
| 5 | 50% VOO, 50% SMH |
| 6 | 50% VOO, 25% LLY, 25% UNH |
| 7 | 100% SNDK |
| 8 | 50% VOO, 50% VUG |

No state is stored: the rules are fixed, so replaying them over the real closes from Oct 2 gives the
same result as tracking it live. Changing a rule later changes the history too, so record any change
below.

## Observations across theses

- **Rebound flavor in both.** Most strong raw follow-on links go *big down day → another fund's big
  up day* (e.g. XLC down → XLK up within 15 days, 16 of 21). The best price pattern is also a
  rebound setup (far below the high, inside a strong group). But the bounce check shows the raw
  links are mostly volatility clustering, so the "rebound" read is weaker than it looked.
- **Raw and relative find different links.** Links that survive the relative measure are the
  candidates for real rotation; the ones that only appear raw are probably the market moving together.
- **Earnings hurt the price guesses.** Worth checking whether follow-on guesses that cross a big
  member's earnings date do worse too.

## Open questions / next tests

- **Direction check for links.** Because big days cluster in both directions, a link like "A down → B
  up" is only rotation if B *up* clearly beats B *down* after the same A days. Compare the two
  directions per link; a link where both are high is clustering.
- **Destinations need history.** XLP, XLU, TLT and GLD are configured but have no data until they're
  added to the Functions app's StockSymbols and backfilled (`range=5y`).
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
| 2026-10-29 | follow-on, follow-on-relative | Added self-links (tallied, never guessed from) | Bounce check: shows whether a "follow-on" is just the leader's own clustering |
| 2026-10-29 | follow-on-relative | Added | The rotation thesis: moves measured against the stock funds' average |
| 2026-10-29 | follow-on, follow-on-relative | Added destinations XLP, XLU, TLT, GLD | Places money can go on days every stock fund falls; kept out of the email and dashboard. Raw follow-on numbers will shift when their data arrives |
| 2026-10-30 | follow-on, follow-on-relative | **Pruned**, with the destinations | The user's call: the 5/10/15-day fund follow-on is a stretch to act on. The raw version was mostly volatility clustering; relative was TooFewTests. Ledger rows through 2026-10-30 stay |
| 2026-10-30 | practice portfolios | Added | Eight forward-only mixes compared against each other, from Oct 2 |
