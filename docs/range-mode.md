# Range mode — day-over-day price matrix with a trend read

*Shipped 2026-08-04 · commits `6d9cdce`, `b7badf8` · live at <https://stockaggregator-web.azurewebsites.net/quotes>*

## The problem

The Quotes calendar could only multi-select individual days, and each selected day rendered
its own side-by-side table of four intraday capture times. That answers "what happened on
Tuesday" but says nothing about behaviour *across* days — which is where mean reversion and
momentum actually live. A single day's four snapshots can't tell you whether a symbol is
trending or oscillating.

## What changed

The calendar now has a **Days / Range** switch.

**Range mode** picks a start and end date and renders a close-only matrix: one row per symbol,
one column per trading day, ETF rows only until you expand a group. No OHLC, no intraday
columns — just the day's closing price, which is what makes historical dates usable at all.

A **weekday filter** narrows the span in place rather than replacing it. Picking `Fr` keeps
every Friday in the range, and because each cell measures against the previous *populated*
column, the percentages become week-over-week rather than day-over-day. Excluded days stay
visible on the calendar but dimmed, so the filter's effect is legible.

### The trend columns

Three sortable columns summarise each row over the selected dates:

| Column | Meaning |
| --- | --- |
| **Trend** | `Up +6.4%` / `Down −3.1%` / `Channel ±1.8%` |
| **Band** | low-to-high spread across the range |
| **In band** | where the last close sits between that low and high, 0–100% |

The classification takes two gates, not one. A net move must clear **2%** *and* cover at
least **half the low-to-high band** before it counts as a trend — otherwise it's Channeling.

That second gate is the one doing real work. Live example from the deployed API: SMH over
May–July shows a net **+6.9%**, which a naive first-vs-last measure would call an uptrend.
But its band over the same window is **21.6%**, so that gain covers under a third of the
symbol's own oscillation. It classifies as **Channeling at 31.9% of band** — a symbol
swinging inside a range and currently sitting toward the bottom of it. Opposite reading,
same numbers.

That's the pairing worth internalising:

- **Channeling + low band position** → stretched to the downside, mean-reversion candidate
- **Strong net + high band position** → momentum continuation

### Deliberately descriptive

The trend column reports what *happened*. It does not forecast. This is the same footing the
Rebound page stands on — historical base rates rather than predictions. Answering "what's the
best time to trade" is a different problem that needs a model and far more data than a few
weeks of intraday capture provides.

## The interesting engineering bit

The matrix reads **two sources stitched together**, because neither alone is sufficient:

| Source | Role | Coverage |
| --- | --- | --- |
| `dbo.DailyOhlc` | **depth** — official closes, years of history | populated by a manual backfill, so its recent end goes stale |
| `dbo.DailySymbolStats` | **freshness** — the nightly rollup's `LastPrice` | self-maintaining, but only as old as intraday capture |

Official closes win wherever `DailyOhlc` has a bar; the rollup fills only dates it hasn't
reached. The seam heals on its own whenever a backfill runs, and no date is ever taken from
both.

The insight that made this simple: the backfill is a *depth* mechanism, not a freshness one.
The first version read only `DailyOhlc` and its right edge sat 13 days behind the dashboard.
Chasing that with repeated backfills would have treated a freshness problem as a depth
problem — and the backfill can't even detect recent staleness, since its resume check tests
each symbol's *earliest* bar.

Honesty about the seam: the rollup's `LastPrice` is the **14:30 CT snapshot — 15:30 ET, half
an hour before the bell**. It's a near-close, not the closing print. Rather than blur that,
the API returns `provisionalFrom` and the UI asterisks those columns with a caption saying
where official closes stop.

Reads are set-based (one query per source) rather than the per-symbol loop the comparison
chart uses, which would have been 87 round trips per matrix load.

## Also in this change

The ETF **add-to-chart button** was `color: var(--muted)` on `var(--bg)` — grey on grey, and
only filled once already active. It was invisible precisely when a first-time user needed to
find it. Now filled green at rest, flipping to a `✓` once charted.

## API

```
GET /api/quotes/matrix?from=2026-05-01&to=2026-08-04&dow=5
GET /api/quotes/trading-dates
```

`dow` is 0=Sun..6=Sat, omitted for every trading day. Columns cap at 180, trimmed from the
start so the most recent dates survive. Response carries `dates[]`, `provisionalFrom`, and
per-row `closes[]` / `changes[]` aligned positionally to `dates`.

Trend classification is pure and unit-tested — 8 cases in
`StockAggregator.Tests/TrendSummaryTests.cs`, including the oscillation-ending-high case that
a naive implementation gets wrong.

---

## Capturing the GIF

Screenshots live in `docs/screenshots/` and are generated by the Playwright suite at
`StockAggregator.UI/e2e/screenshots.spec.ts` (flip `CAPTURE = true`, re-run, flip back).
That suite captures PNGs; a GIF needs a screen recorder over a live session, or Playwright
video (`use: { video: 'on' }` in `playwright.config.ts`) converted with ffmpeg.

**Suggested shot list — roughly 12–15 seconds, `/quotes` with the browse panel open:**

1. **Days mode, as it is today** (~1s) — establishes the starting point.
2. **Click `Range`** (~1s) — the mode switch, quick-picks swap to `1M / 3M / 6M / 1Y`,
   the weekday filter row appears.
3. **Click `3M`** (~2s) — matrix fills in. This is the money frame: ~11 ETF rows across a
   wide date axis, closing prices with green/red changes, trend column on the right.
4. **Click the `Fr` weekday filter** (~2s) — columns collapse from every trading day to
   Fridays only, and the excluded days visibly dim on the calendar above. This is the single
   most legible moment in the demo — the grid narrows and the percentages become weekly.
5. **Click the `Trend` column header to sort** (~2s) — rows reorder, strongest movers to the
   top. Shows it working as a screener rather than a static readout.
6. **Click `In band` to sort** (~2s) — reorders again, surfacing the stretched-to-the-bottom
   names. Pair with step 5 to make the momentum-vs-reversion distinction visible.
7. **Expand one ETF group** (~2s) — member rows appear beneath, inheriting the same columns.

Use a **3M range with the Friday filter** as the resting state for any still image — it fits
without horizontal scrolling and shows a good mix of Up / Down / Channel verdicts. A full
unfiltered 6M range scrolls sideways and reads as noise in a small frame.

Two things to be aware of when recording against live data:

- Recent columns carry an asterisk and a caption about 14:30 CT prices. That's correct
  behaviour, not a glitch — either include it and let it demonstrate the honesty, or run a
  backfill first to shrink it to zero.
- US market holidays currently appear as near-empty columns, because non-US members like
  `7203.T` (Tokyo) traded when US markets were closed. On a 3M range that's 2026-06-19 and
  2026-07-03. Avoid framing that puts them center-shot until the column-quorum fix lands.
