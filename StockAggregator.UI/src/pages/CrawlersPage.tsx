import { useEffect, useState } from 'react';
import { fetchCrawlers } from '../api/client';
import type { CrawlerResponse, CrawlerRow } from '../types';
import { SYMBOL_NAMES } from '../symbolNames';

const WINDOWS = [
  { label: '1M', days: 21 },
  { label: '3M', days: 63 },
  { label: '6M', days: 126 },
];

const UP = '#157f3b';
const DOWN = '#c62828';

function pct(v: number | null, digits = 1, sign = false): string {
  if (v == null) return '—';
  return `${sign && v >= 0 ? '+' : ''}${v.toFixed(digits)}%`;
}

function Sparkline({ data }: { data: number[] }) {
  if (!data || data.length < 2) return null;
  const w = 88, h = 26, pad = 3;
  const min = Math.min(...data);
  const max = Math.max(...data);
  const range = max - min || 1;
  const pts = data
    .map((v, i) => {
      const x = pad + (i / (data.length - 1)) * (w - 2 * pad);
      const y = pad + (1 - (v - min) / range) * (h - 2 * pad);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(' ');
  const up = data[data.length - 1] >= data[0];
  return (
    <svg width={w} height={h} className="spark" aria-hidden>
      <polyline points={pts} fill="none" stroke={up ? UP : DOWN} strokeWidth={1.5} />
    </svg>
  );
}

function downloadCsv(filename: string, table: (string | number)[][]) {
  const esc = (v: string | number) => {
    const s = String(v ?? '');
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const csv = table.map((row) => row.map(esc).join(',')).join('\r\n');
  // Leading BOM so Excel reads UTF-8 correctly.
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export default function CrawlersPage() {
  const [window, setWindow] = useState(63);
  const [steadyOnly, setSteadyOnly] = useState(false);
  const [data, setData] = useState<CrawlerResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    setLoading(true);
    setError(null);
    fetchCrawlers(window)
      .then((d) => { if (live) setData(d); })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [window]);

  const rows: CrawlerRow[] = (data?.rows ?? []).filter((r) => !steadyOnly || r.isSteadyCrawler);

  const exportCsv = () => {
    const header = ['Rank', 'Symbol', 'Name', 'ETF', 'Return %', 'Steadiness (R2)', 'Max drawdown %', 'Up-day %', 'Weekly drift %', 'Steady crawler'];
    const body = rows.map((r, i) => [
      i + 1, r.symbol, SYMBOL_NAMES[r.symbol] ?? '', r.etf,
      r.returnPct ?? '', r.steadiness ?? '', r.maxDrawdownPct ?? '', r.upDayPct ?? '', r.weeklyDriftPct ?? '',
      r.isSteadyCrawler ? 'yes' : '',
    ]);
    downloadCsv(`steady-crawlers-${window}d.csv`, [header, ...body]);
  };

  return (
    <section className="page">
      <div className="analytics-head">
        <h2>Steady Crawlers</h2>
        {data?.asOfDate && <span className="asof">{data.windowDays}-day window · as of {data.asOfDate}</span>}
      </div>
      <p className="subtle">
        Names grinding steadily upward with little give-back — ranked by <strong>steadiness</strong> (R² of the
        log-price trend: 1 = a straight-line climb, 0 = noise), gated so steady <em>decliners</em> sink and
        volatile spikes don&apos;t masquerade as steady. Flagged rows clear R² ≥ 0.65, a positive return, and a
        max drawdown ≤ 12%. Historical, not a recommendation.
      </p>

      <div className="pill-row">
        <span className="pill-caption">window:</span>
        {WINDOWS.map((w) => (
          <button key={w.days} className={`pill pill-mode${w.days === window ? ' active' : ''}`} onClick={() => setWindow(w.days)}>
            {w.label}
          </button>
        ))}
        <button className={`pill pill-etf${steadyOnly ? ' active' : ''}`} onClick={() => setSteadyOnly((s) => !s)}>
          Steady only
        </button>
        <button className="pill" onClick={exportCsv} disabled={rows.length === 0} title="Download CSV (opens in Excel)">
          ⬇ Export CSV
        </button>
      </div>

      {loading && <p>Loading…</p>}
      {error && <p className="error">Error: {error}</p>}

      {!loading && !error && data && (
        rows.length === 0 ? (
          <p className="placeholder">No crawlers to show{steadyOnly ? ' — none flagged for this window.' : '.'}</p>
        ) : (
          <div className="table-wrap">
            <table className="quotes-table crawler-table">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Symbol</th>
                  <th>Trend</th>
                  <th>Return</th>
                  <th>Steadiness</th>
                  <th>Max DD</th>
                  <th>Up-days</th>
                  <th>Drift/wk</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={r.symbol} className={r.isSteadyCrawler ? 'crawler-flagged' : undefined}>
                    <td className="muted">{i + 1}</td>
                    <td className="crawler-sym">
                      <span className="crawler-ticker">{r.symbol}</span>
                      {r.isSteadyCrawler && <span className="badge">STEADY</span>}
                      <span className="crawler-name">{SYMBOL_NAMES[r.symbol] ?? ''}</span>
                      <span className="crawler-etf">{r.etf}</span>
                    </td>
                    <td><Sparkline data={r.spark} /></td>
                    <td style={{ color: (r.returnPct ?? 0) >= 0 ? UP : DOWN, fontWeight: 600 }}>{pct(r.returnPct, 1, true)}</td>
                    <td>{r.steadiness != null ? r.steadiness.toFixed(2) : '—'}</td>
                    <td className="down">{r.maxDrawdownPct != null ? `−${r.maxDrawdownPct.toFixed(1)}%` : '—'}</td>
                    <td>{r.upDayPct != null ? `${r.upDayPct.toFixed(0)}%` : '—'}</td>
                    <td style={{ color: (r.weeklyDriftPct ?? 0) >= 0 ? UP : DOWN }}>{pct(r.weeklyDriftPct, 1, true)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      )}
    </section>
  );
}
