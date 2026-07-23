import { useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { fetchHistory } from '../api/client';
import type { SymbolSeries } from '../types';

const WINDOWS = [
  { label: '1M', days: 21 },
  { label: '3M', days: 63 },
  { label: '6M', days: 126 },
  { label: '1Y', days: 252 },
];

const UP = '#157f3b';
const DOWN = '#c62828';

function windowLabel(days: number): string {
  return WINDOWS.find((w) => w.days === days)?.label ?? `${days}d`;
}

function fmtRet(v: number): string {
  return `${v >= 0 ? '+' : ''}${v.toFixed(1)}%`;
}

function fmtDate(iso: string): string {
  return new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

interface Props {
  symbols: string[];
  colorOf: (symbol: string) => string;
  etfGroups: { etf: string; members: string[] }[];
  standalone: string[];
  onRemoveEtfAll: (etf: string) => void;
  onRemoveEtfOnly: (etf: string) => void;
  onRemoveStock: (symbol: string) => void;
  onPlotClick: () => void;
}

export default function StockChart({
  symbols, colorOf, etfGroups, standalone, onRemoveEtfAll, onRemoveEtfOnly, onRemoveStock, onPlotClick,
}: Props) {
  const [window, setWindow] = useState(126);
  const [normalize, setNormalize] = useState(true);
  const [series, setSeries] = useState<SymbolSeries[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (symbols.length === 0) { setSeries([]); return; }
    let live = true;
    setLoading(true);
    setError(null);
    fetchHistory(symbols, window)
      .then((d) => { if (live) setSeries(d.series); })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [symbols, window]);

  const data = useMemo(() => {
    const byDate = new Map<string, Record<string, number | string>>();
    for (const s of series) {
      const base = s.points.find((p) => p.close > 0)?.close ?? 0;
      for (const p of s.points) {
        const row = byDate.get(p.date) ?? { date: p.date };
        row[s.symbol] = normalize && base > 0 ? (p.close / base - 1) * 100 : p.close;
        byDate.set(p.date, row);
      }
    }
    return [...byDate.values()].sort((a, b) => String(a.date).localeCompare(String(b.date)));
  }, [series, normalize]);

  const rets = series.map((s) => {
    const f = s.points.find((p) => p.close > 0)?.close ?? 0;
    const l = s.points.length ? s.points[s.points.length - 1].close : 0;
    return { symbol: s.symbol, ret: f > 0 ? (l / f - 1) * 100 : 0 };
  });
  const best = rets.length ? rets.reduce((a, b) => (b.ret > a.ret ? b : a)) : null;
  const worst = rets.length ? rets.reduce((a, b) => (b.ret < a.ret ? b : a)) : null;

  let summary: ReactNode;
  if (symbols.length === 0) summary = 'Pick an ETF or stock from the panel to chart it.';
  else if (rets.length === 0) summary = 'Loading…';
  else if (rets.length === 1) summary = <>{best!.symbol} <strong style={{ color: best!.ret >= 0 ? UP : DOWN }}>{fmtRet(best!.ret)}</strong> over {windowLabel(window)}</>;
  else summary = (
    <>
      {symbols.length} symbols · {windowLabel(window)} — best <strong>{best!.symbol}</strong> <span style={{ color: best!.ret >= 0 ? UP : DOWN }}>{fmtRet(best!.ret)}</span>, worst <strong>{worst!.symbol}</strong> <span style={{ color: worst!.ret >= 0 ? UP : DOWN }}>{fmtRet(worst!.ret)}</span>
    </>
  );

  return (
    <div className="stock-chart">
      <div className="chart-hero">
        <h2 className="chart-hero-title">Price comparison</h2>
        <p className="chart-hero-summary">{summary}</p>
      </div>

      <div className="chart-toolbar">
        <div className="pill-row" style={{ margin: 0 }}>
          {WINDOWS.map((w) => (
            <button key={w.days} className={`pill pill-mode${w.days === window ? ' active' : ''}`} onClick={() => setWindow(w.days)}>
              {w.label}
            </button>
          ))}
          <button className={`pill${normalize ? ' pill-mode active' : ''}`} onClick={() => setNormalize((n) => !n)}>
            {normalize ? '% change' : 'price'}
          </button>
        </div>
      </div>

      <div className="chart-row">
        <aside className="chart-legend">
          {symbols.length === 0 ? (
            <p className="subtle" style={{ margin: 0 }}>No symbols selected.</p>
          ) : (
            <>
              {etfGroups.map((g) => (
                <div key={g.etf} className="cl-group">
                  <div className="cl-etf">
                    <span className="cl-dot" style={{ background: colorOf(g.etf) }} />
                    <span className="cl-sym">{g.etf}</span>
                    <span className="cl-actions">
                      <button type="button" className="cl-btn" title="Remove ETF, keep its stocks" onClick={() => onRemoveEtfOnly(g.etf)}>−</button>
                      <button type="button" className="cl-btn" title="Remove ETF and its stocks" onClick={() => onRemoveEtfAll(g.etf)}>−all</button>
                    </span>
                  </div>
                  {g.members.map((m) => (
                    <div key={m} className="cl-stock">
                      <span className="cl-dot" style={{ background: colorOf(m) }} />
                      <span className="cl-sym">{m}</span>
                      <button type="button" className="cl-btn cl-x" title="Remove" onClick={() => onRemoveStock(m)}>−</button>
                    </div>
                  ))}
                </div>
              ))}
              {standalone.map((s) => (
                <div key={s} className="cl-stock cl-standalone">
                  <span className="cl-dot" style={{ background: colorOf(s) }} />
                  <span className="cl-sym">{s}</span>
                  <button type="button" className="cl-btn cl-x" title="Remove" onClick={() => onRemoveStock(s)}>−</button>
                </div>
              ))}
            </>
          )}
        </aside>

        <div className="chart-plot" onClick={onPlotClick} title="Click to toggle the browse panel">
          {symbols.length === 0 ? (
            <p className="subtle chart-hint">Open the panel and click an ETF or stock to plot it. Click the chart to toggle the panel.</p>
          ) : error ? (
            <p className="error">Error: {error}</p>
          ) : (
            <div style={{ height: 440, opacity: loading ? 0.6 : 1 }}>
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={data} margin={{ left: 4, right: 16, top: 8, bottom: 4 }}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="date" tickFormatter={fmtDate} minTickGap={48} tick={{ fontSize: 11 }} />
                  <YAxis
                    tickFormatter={(v: number) => (normalize ? `${v > 0 ? '+' : ''}${v.toFixed(0)}%` : `$${v}`)}
                    width={54}
                    tick={{ fontSize: 11 }}
                    domain={['auto', 'auto']}
                  />
                  {normalize && <ReferenceLine y={0} />}
                  <Tooltip
                    labelFormatter={(d) => new Date(`${d}T00:00:00`).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' })}
                    formatter={(v: number, name: string) => [normalize ? `${v >= 0 ? '+' : ''}${v.toFixed(2)}%` : `$${v.toFixed(2)}`, name]}
                  />
                  {symbols.map((s) => (
                    <Line key={s} type="monotone" dataKey={s} stroke={colorOf(s)} strokeWidth={1.8} dot={false} connectNulls isAnimationActive={false} />
                  ))}
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
