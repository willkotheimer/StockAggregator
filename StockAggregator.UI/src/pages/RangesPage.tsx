import { useEffect, useState } from 'react';
import { fetchEtfGroups, fetchRanges } from '../api/client';
import { useApi } from '../hooks/useApi';
import type { RangeResponse } from '../types';

const PULLBACKS = [3, 5, 10];

function pct(v: number | null, digits = 2): string {
  if (v == null) return '—';
  return `${v.toFixed(digits)}%`;
}

export default function RangesPage() {
  const { data: groups, loading: groupsLoading, error: groupsError } = useApi(fetchEtfGroups);

  const [etf, setEtf] = useState<string | null>(null);
  const [pullback, setPullback] = useState(5);

  useEffect(() => {
    if (groups && groups.length > 0 && etf === null) {
      setEtf(groups[0].etf);
    }
  }, [groups, etf]);

  const [data, setData] = useState<RangeResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!etf) return;
    let live = true;
    setLoading(true);
    setError(null);
    fetchRanges(etf, pullback)
      .then((d) => { if (live) setData(d); })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [etf, pullback]);

  if (groupsLoading) return <p>Loading…</p>;
  if (groupsError) return <p className="error">Error: {groupsError}</p>;

  return (
    <section className="page">
      <div className="analytics-head">
        <h2>Ranges &amp; profit-taking</h2>
      </div>
      <p className="subtle">
        How much each name typically moves, and how much it usually gains before a pullback — so profit
        targets are calibrated to the stock&apos;s own behavior. Medians over its daily history. Not advice.
      </p>

      {/* ETF pills (select one) */}
      <div className="pill-row">
        {groups?.map((g) => (
          <button
            key={g.etf}
            className={`pill pill-etf${g.etf === etf ? ' active' : ''}`}
            title={g.description}
            onClick={() => setEtf(g.etf)}
          >
            {g.etf}
          </button>
        ))}
      </div>

      {/* Pullback threshold for the profit-taking column */}
      <div className="pill-row">
        <span className="pill-caption">pullback:</span>
        {PULLBACKS.map((p) => (
          <button
            key={p}
            className={`pill pill-mode${p === pullback ? ' active' : ''}`}
            onClick={() => setPullback(p)}
          >
            {p}%
          </button>
        ))}
      </div>

      {loading && <p>Loading {etf}…</p>}
      {error && <p className="error">Error: {error}</p>}

      {!loading && !error && data && (
        <>
          <p className="subtle">
            {data.etf} — {data.description}. Ranked by typical daily range; the ETF itself is pinned on top as
            a benchmark. &ldquo;Gain before pullback&rdquo; = typical run-up before a {data.pullbackPct}% drop
            ({'∑'} across n episodes).
          </p>
          <div className="table-wrap">
            <table className="quotes-table rebound-table">
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Daily range</th>
                  <th>Weekly range</th>
                  <th>Up-day %</th>
                  <th>Up day</th>
                  <th>Down day</th>
                  <th>Gain before {data.pullbackPct}% pullback</th>
                  <th>History</th>
                </tr>
              </thead>
              <tbody>
                {data.rows.map((r) => (
                  <tr key={r.symbol} className={r.isEtf ? 'range-etf-row' : undefined}>
                    <td>
                      <span className={r.isEtf ? 'symbol symbol-etf' : 'symbol'}>{r.symbol}</span>
                      {r.isEtf && <span className="etf-desc">ETF</span>}
                    </td>
                    <td>{pct(r.medianDailyRangePct)}</td>
                    <td>{pct(r.medianWeeklyRangePct)}</td>
                    <td>{r.upDayPct != null ? `${r.upDayPct.toFixed(0)}%` : '—'}</td>
                    <td className="up">{r.medianUpDayPct != null ? `+${r.medianUpDayPct.toFixed(2)}%` : '—'}</td>
                    <td className="down">{r.medianDownDayPct != null ? `−${r.medianDownDayPct.toFixed(2)}%` : '—'}</td>
                    <td>
                      {r.typicalGainBeforePullbackPct != null
                        ? <>{pct(r.typicalGainBeforePullbackPct)} <span className="muted">(n={r.pullbackEpisodeCount})</span></>
                        : '—'}
                    </td>
                    <td className="muted">{r.barCount > 0 ? `${r.barCount}d` : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
