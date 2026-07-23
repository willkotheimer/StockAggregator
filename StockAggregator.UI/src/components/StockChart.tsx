import { useEffect, useMemo, useState } from 'react';
import {
  CartesianGrid, Legend, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { fetchHistory } from '../api/client';
import type { SymbolSeries } from '../types';

const WINDOWS = [
  { label: '1M', days: 21 },
  { label: '3M', days: 63 },
  { label: '6M', days: 126 },
  { label: '1Y', days: 252 },
];

function fmtDate(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

interface Props {
  symbols: string[];
  colorOf: (symbol: string) => string;
  onRemove: (symbol: string) => void;
}

export default function StockChart({ symbols, colorOf, onRemove }: Props) {
  const [window, setWindow] = useState(126);
  const [normalize, setNormalize] = useState(true);
  const [series, setSeries] = useState<SymbolSeries[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (symbols.length === 0) {
      setSeries([]);
      return;
    }
    let live = true;
    setLoading(true);
    setError(null);
    fetchHistory(symbols, window)
      .then((d) => { if (live) setSeries(d.series); })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [symbols, window]);

  // Merge per-symbol series into one date-keyed row set for Recharts.
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

  return (
    <div className="stock-chart">
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
        <div className="chart-chips">
          {symbols.map((s) => (
            <button key={s} className="chart-chip" style={{ borderColor: colorOf(s), color: colorOf(s) }} onClick={() => onRemove(s)} title="Remove from chart">
              <span className="chart-chip-dot" style={{ background: colorOf(s) }} />
              {s} ✕
            </button>
          ))}
        </div>
      </div>

      {symbols.length === 0 ? (
        <p className="subtle chart-hint">Click any stock in the tables above to chart it — add several to compare.</p>
      ) : error ? (
        <p className="error">Error: {error}</p>
      ) : (
        <div style={{ height: 320, opacity: loading ? 0.6 : 1 }}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={data} margin={{ left: 4, right: 16, top: 8, bottom: 4 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e6e7ea" />
              <XAxis dataKey="date" tickFormatter={fmtDate} minTickGap={48} tick={{ fontSize: 11 }} />
              <YAxis
                tickFormatter={(v: number) => (normalize ? `${v > 0 ? '+' : ''}${v.toFixed(0)}%` : `$${v}`)}
                width={54}
                tick={{ fontSize: 11 }}
                domain={['auto', 'auto']}
              />
              {normalize && <ReferenceLine y={0} stroke="#b0b4bb" />}
              <Tooltip
                labelFormatter={(d) => new Date(`${d}T00:00:00`).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' })}
                formatter={(v: number, name: string) => [normalize ? `${v >= 0 ? '+' : ''}${v.toFixed(2)}%` : `$${v.toFixed(2)}`, name]}
              />
              <Legend />
              {symbols.map((s) => (
                <Line key={s} type="monotone" dataKey={s} stroke={colorOf(s)} strokeWidth={1.8} dot={false} connectNulls isAnimationActive={false} />
              ))}
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}
