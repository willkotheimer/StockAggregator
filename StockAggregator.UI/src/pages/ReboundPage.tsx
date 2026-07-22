import { useEffect, useMemo, useState } from 'react';
import { fetchEtfGroups, fetchRebound } from '../api/client';
import { useApi } from '../hooks/useApi';
import type { ReboundResponse } from '../types';

type Mode = 'trough' | 'surge';

// Direction-aware wording so one component serves both modes symmetrically.
const LABELS = {
  trough: {
    anchor: 'peak', extreme: 'low', moveWord: 'below', reversalTo: 'its prior peak',
    goesBack: 'back up', activeTitle: 'in a drawdown', episodeCol: 'depth',
  },
  surge: {
    anchor: 'low', extreme: 'high', moveWord: 'above', reversalTo: 'its starting low',
    goesBack: 'back down', activeTitle: 'on a surge', episodeCol: 'height',
  },
} as const;

function price(v: number | null | undefined): string {
  if (v == null) return '—';
  return `$${v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function addDays(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

export default function ReboundPage() {
  const { data: groups, loading: groupsLoading, error: groupsError } = useApi(fetchEtfGroups);

  const [mode, setMode] = useState<Mode>('trough');
  const [etf, setEtf] = useState<string | null>(null);
  const [symbol, setSymbol] = useState<string | null>(null);

  // Default to the first ETF + its first member once groups arrive.
  useEffect(() => {
    if (groups && groups.length > 0 && etf === null) {
      setEtf(groups[0].etf);
      setSymbol(groups[0].members[0] ?? null);
    }
  }, [groups, etf]);

  const members = useMemo(
    () => groups?.find((g) => g.etf === etf)?.members ?? [],
    [groups, etf],
  );

  const [rebound, setRebound] = useState<ReboundResponse | null>(null);
  const [rbLoading, setRbLoading] = useState(false);
  const [rbError, setRbError] = useState<string | null>(null);

  useEffect(() => {
    if (!symbol) {
      setRebound(null);
      return;
    }
    let live = true;
    setRbLoading(true);
    setRbError(null);
    fetchRebound(symbol, mode)
      .then((d) => { if (live) setRebound(d); })
      .catch((e: unknown) => { if (live) setRbError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setRbLoading(false); });
    return () => { live = false; };
  }, [symbol, mode]);

  function pickEtf(next: string) {
    setEtf(next);
    const first = groups?.find((g) => g.etf === next)?.members[0] ?? null;
    setSymbol(first);
  }

  if (groupsLoading) return <p>Loading…</p>;
  if (groupsError) return <p className="error">Error: {groupsError}</p>;

  const L = LABELS[mode];

  return (
    <section className="page">
      <div className="analytics-head">
        <h2>Rebound base rates</h2>
        {rebound?.asOfDate && <span className="asof">as of {rebound.asOfDate}</span>}
      </div>
      <p className="subtle">
        How {symbol ?? 'a stock'} has historically behaved after a{' '}
        {mode === 'trough' ? 'drawdown' : 'run-up'} — measured from its own daily history, shown as
        base rates (median, range, and how many times it happened). Not a forecast.
      </p>

      {/* Mode: Trough vs Surge (select one) */}
      <div className="pill-row">
        <button className={`pill pill-mode${mode === 'trough' ? ' active' : ''}`} onClick={() => setMode('trough')}>
          Trough <span className="pill-sub">dip → recovery</span>
        </button>
        <button className={`pill pill-mode${mode === 'surge' ? ' active' : ''}`} onClick={() => setMode('surge')}>
          Surge <span className="pill-sub">run-up → pullback</span>
        </button>
      </div>

      {/* ETF pills (select one) */}
      <div className="pill-row">
        {groups?.map((g) => (
          <button
            key={g.etf}
            className={`pill pill-etf${g.etf === etf ? ' active' : ''}`}
            title={g.description}
            onClick={() => pickEtf(g.etf)}
          >
            {g.etf}
          </button>
        ))}
      </div>

      {/* Member stock pills (select one) */}
      <div className="pill-row">
        {members.map((m) => (
          <button
            key={m}
            className={`pill pill-stock${m === symbol ? ' active' : ''}`}
            onClick={() => setSymbol(m)}
          >
            {m}
          </button>
        ))}
      </div>

      {rbLoading && <p>Loading {symbol}…</p>}
      {rbError && <p className="error">Error: {rbError}</p>}

      {!rbLoading && !rbError && rebound && (
        <>
          {rebound.barCount === 0 ? (
            <p className="placeholder">
              No daily history for {rebound.symbol} yet — run the backfill, then check back.
            </p>
          ) : (
            <>
              <HeroCard rebound={rebound} mode={mode} />

              <h3 className="rebound-table-title">
                All {L.episodeCol}s ≥ {rebound.thresholdPct}% — {rebound.symbol} since {rebound.historyStart}
              </h3>
              <div className="table-wrap">
                <table className="quotes-table rebound-table">
                  <thead>
                    <tr>
                      <th>{mode === 'trough' ? 'Peak' : 'Low'}</th>
                      <th>{L.anchor} $</th>
                      <th>{mode === 'trough' ? 'Trough' : 'High'}</th>
                      <th>{L.extreme} $</th>
                      <th>{L.episodeCol}</th>
                      <th>{L.anchor}→{L.extreme}</th>
                      <th>{L.extreme}→{mode === 'trough' ? 'recovery' : 'pullback'}</th>
                      <th>round trip</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rebound.episodes.map((e) => (
                      <tr key={`${e.anchorDate}-${e.extremeDate}`}>
                        <td>{e.anchorDate}</td>
                        <td>{price(e.anchorPrice)}</td>
                        <td>{e.extremeDate}</td>
                        <td>{price(e.extremePrice)}</td>
                        <td className={mode === 'trough' ? 'down' : 'up'}>
                          {mode === 'trough' ? '−' : '+'}{e.movePct.toFixed(1)}%
                        </td>
                        <td>{e.anchorToExtremeDays}d</td>
                        <td>{e.extremeToReversalDays != null ? `${e.extremeToReversalDays}d` : '—'}</td>
                        <td>{e.anchorToReversalDays != null ? `${e.anchorToReversalDays}d` : '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </>
      )}
    </section>
  );
}

function HeroCard({ rebound, mode }: { rebound: ReboundResponse; mode: Mode }) {
  const L = LABELS[mode];
  const { current, baseRate } = rebound;
  const active = current != null;

  return (
    <div className={`rebound-hero${active ? ' active' : ''}`}>
      <div className="rebound-hero-head">
        <span className="rebound-sym">{rebound.symbol}</span>
        <span className={`rebound-status ${mode}`}>
          {active ? `${L.activeTitle}` : `not currently ${L.activeTitle}`}
        </span>
        <span className="rebound-now">now {price(rebound.lastClose)}</span>
      </div>

      {active && current ? (
        <>
          <div className="rebound-stats">
            <div className="rebound-stat">
              <span className="rebound-stat-label">{mode === 'trough' ? 'Peak' : 'Low'}</span>
              <span className="rebound-stat-val">{price(current.anchorPrice)}</span>
              <span className="rebound-stat-sub">{current.anchorDate}</span>
            </div>
            <div className="rebound-stat">
              <span className="rebound-stat-label">{L.extreme} so far</span>
              <span className={`rebound-stat-val ${mode === 'trough' ? 'down' : 'up'}`}>
                {mode === 'trough' ? '−' : '+'}{current.maxMovePct.toFixed(1)}%
              </span>
              <span className="rebound-stat-sub">{price(current.extremePrice)} · {current.extremeDate}</span>
            </div>
            <div className="rebound-stat">
              <span className="rebound-stat-label">now vs {L.anchor}</span>
              <span className={`rebound-stat-val ${mode === 'trough' ? 'down' : 'up'}`}>
                {mode === 'trough' ? '−' : '+'}{current.currentMovePct.toFixed(1)}%
              </span>
              <span className="rebound-stat-sub">{current.daysSinceExtreme}d since the {L.extreme}</span>
            </div>
          </div>

          {baseRate ? (
            <div className="rebound-reco">
              <p className="rebound-reco-lead">
                Historically, {rebound.symbol} {mode === 'trough' ? 'drawdowns' : 'run-ups'} of at least{' '}
                <strong>{baseRate.comparableMovePct.toFixed(0)}%</strong> returned to {L.reversalTo} in a{' '}
                <strong>median of {baseRate.medianReversalDays} days</strong> from the {L.extreme} — i.e. it
                tends to go <strong>{L.goesBack}</strong> around{' '}
                <strong>{addDays(current.extremeDate, baseRate.medianReversalDays)}</strong>.
              </p>
              <p className="rebound-reco-range">
                Range {baseRate.minReversalDays}–{baseRate.maxReversalDays} days across{' '}
                <strong>n = {baseRate.episodeCount}</strong> comparable episodes
                {' '}({baseRate.reversedWithinShort}/{baseRate.episodeCount} within {baseRate.shortWindowDays}d,{' '}
                {baseRate.reversedWithinLong}/{baseRate.episodeCount} within {baseRate.longWindowDays}d).
                {baseRate.episodeCount < 4 && ' Thin sample — treat the median loosely.'}
              </p>
            </div>
          ) : (
            <p className="rebound-reco-range">
              No comparable {mode === 'trough' ? 'drawdowns' : 'run-ups'} of this size in {rebound.symbol}&apos;s
              history yet — not enough to state a base rate.
            </p>
          )}
        </>
      ) : (
        <p className="rebound-reco-range">
          {rebound.symbol} is at or near {mode === 'trough' ? 'its highs' : 'its lows'} — no active{' '}
          {mode === 'trough' ? 'drawdown' : 'run-up'} of {rebound.thresholdPct}%+ to project from. Switch modes
          or pick another stock.
        </p>
      )}

      <p className="rebound-disclaimer">
        Not a guarantee — always based on what the price is now, rather than what it can be.
      </p>
    </div>
  );
}
