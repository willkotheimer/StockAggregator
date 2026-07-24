import { useEffect, useState } from 'react';
import { Bar, BarChart, Cell, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { fetchCorrelations, fetchRotations } from '../api/client';
import { useApi } from '../hooks/useApi';
import type { CorrelationResponse } from '../types';
import QuotesDrilldownModal from '../components/QuotesDrilldownModal';

const UP = '#157f3b';
const DOWN = '#c62828';
const WINDOWS = [30, 60, 90];

// Diverging blue <-> gray <-> red (validated CVD-safe pair; light-mode surface).
const NEUTRAL = [240, 239, 236]; // #f0efec
const POS = [214, 69, 69]; // red  -> move together
const NEG = [42, 120, 214]; // blue -> move oppositely

function corrColor(v: number | null): string {
  if (v == null) return 'transparent';
  const pole = v >= 0 ? POS : NEG;
  const t = Math.min(1, Math.abs(v));
  const c = NEUTRAL.map((n, i) => Math.round(n + (pole[i] - n) * t));
  return `rgb(${c[0]}, ${c[1]}, ${c[2]})`;
}

function corrText(v: number | null): string {
  if (v == null) return 'var(--muted)';
  // The red (positive) pole reads better with black; only strong blue takes white.
  if (v >= 0) return '#1a1d21';
  return Math.abs(v) > 0.55 ? '#fff' : '#1a1d21';
}

function fmtCorr(v: number | null): string {
  if (v == null) return '—';
  return (v >= 0 ? '+' : '') + v.toFixed(2);
}

export default function RotationsPage() {
  const { data, loading, error } = useApi(fetchRotations);

  const [window, setWindow] = useState(60);
  const [corr, setCorr] = useState<CorrelationResponse | null>(null);
  const [drill, setDrill] = useState<{ a: string; b: string } | null>(null);

  useEffect(() => {
    let live = true;
    fetchCorrelations(window)
      .then((d) => { if (live) setCorr(d); })
      .catch(() => {}); // correlations stay hidden until they load
    return () => { live = false; };
  }, [window]);

  if (loading) return <p>Loading…</p>;
  if (error) return <p className="error">Error: {error}</p>;
  if (!data || data.rows.length === 0) return <p>No analytics computed yet — check back after the next nightly rollup.</p>;

  const chartData = data.rows.map((r) => ({ etf: r.etf, description: r.description, changePct: r.changePct ?? 0 }));

  // Two-line Y-axis tick: ticker on top, ETF description beneath.
  const renderEtfTick = ({ x, y, payload }: { x: number; y: number; payload: { value: string } }) => {
    const row = chartData.find((d) => d.etf === payload.value);
    return (
      <g transform={`translate(${x},${y})`}>
        <text x={-8} y={-1} textAnchor="end" fontSize={12} fontWeight={700} className="rot-tick-sym">{payload.value}</text>
        <text x={-8} y={11} textAnchor="end" fontSize={10} className="rot-tick-desc">{row?.description ?? ''}</text>
      </g>
    );
  };

  return (
    <section className="page rotations-page">
      <div className="rot-corr-layout">
        <div className="rot-col">
          <div className="analytics-head">
            <h2>Rotations</h2>
            <span className="asof">computed as of {data.asOfDate}</span>
          </div>
          <p className="subtle">Sector ETFs ranked by daily change — leaders on top, laggards below.</p>

          <div className="chart-wrap" style={{ height: chartData.length * 34 + 48 }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={chartData} layout="vertical" margin={{ left: 8, right: 28, top: 8, bottom: 8 }}>
                <XAxis type="number" tickFormatter={(v) => `${v}%`} />
                <YAxis type="category" dataKey="etf" width={150} tick={renderEtfTick} tickLine={false} axisLine={false} />
                <ReferenceLine x={0} stroke="#b0b4bb" />
                <Tooltip
                  formatter={(v: number) => [`${v.toFixed(2)}%`, 'change']}
                  labelFormatter={(etf) => {
                    const row = chartData.find((r) => r.etf === etf);
                    return row ? `${row.etf} — ${row.description}` : String(etf);
                  }}
                />
                <Bar dataKey="changePct" radius={[0, 3, 3, 0]} isAnimationActive={false}>
                  {chartData.map((d) => (
                    <Cell key={d.etf} fill={d.changePct >= 0 ? UP : DOWN} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Correlations — hidden entirely until loaded */}
        {corr && corr.symbols.length > 0 && (
          <div className="corr-col">
            <div className="analytics-head">
              <h2>Correlations</h2>
              <span className="asof">last {corr.windowDays} trading days · as of {corr.asOfDate}</span>
            </div>
            <p className="subtle">
              How the sector ETFs&apos; daily moves relate over the window. <strong style={{ color: DOWN }}>Red</strong> = they
              move together (redundant); <strong style={{ color: '#2a78d6' }}>blue</strong> = they move oppositely (natural
              hedges). Correlation, not causation.
            </p>

            <div className="pill-row">
              <span className="pill-caption">window:</span>
              {WINDOWS.map((w) => (
                <button key={w} className={`pill pill-mode${w === window ? ' active' : ''}`} onClick={() => setWindow(w)}>
                  {w}d
                </button>
              ))}
            </div>

            <div className="corr-layout">
          <div className="corr-heat">
            <div className="table-wrap" style={{ display: 'inline-block', maxWidth: '100%' }}>
              <table className="corr-grid">
                <thead>
                  <tr>
                    <th className="corr-corner"></th>
                    {corr.symbols.map((s, i) => (
                      <th key={s} className="corr-colhead">
                        <span className="corr-colsym">{s}</span> <span className="corr-coldesc">{corr.descriptions[i]}</span>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {corr.symbols.map((rowSym, i) => (
                    <tr key={rowSym}>
                      <th className="corr-rowhead">
                        <span className="corr-rowsym">{rowSym}</span> <span className="corr-rowdesc">{corr.descriptions[i]}</span>
                      </th>
                      {corr.symbols.map((colSym, j) => {
                        const v = corr.matrix[i][j];
                        return (
                          <td key={colSym} className="corr-cell" style={{ background: corrColor(v) }}>
                            <button
                              type="button"
                              className="corr-cell-link"
                              style={{ color: i === j ? '#1a1d21' : corrText(v) }}
                              onClick={() => setDrill({ a: rowSym, b: colSym })}
                              title={`${rowSym} × ${colSym}: ${fmtCorr(v)} — open quotes`}
                            >
                              {i === j ? '1.00' : fmtCorr(v)}
                            </button>
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="corr-legend">
              <span>−1 opposite</span>
              <span className="corr-legend-bar" />
              <span>+1 together</span>
            </div>
          </div>

          <div className="corr-pairs">
            <div className="corr-pair-col">
              <h3>Most opposing <span className="muted">(hedges)</span></h3>
              {corr.mostOpposing.map((p) => (
                <div key={`${p.a}-${p.b}`} className="corr-pair-row">
                  <span>{p.a} ↔ {p.b}</span>
                  <span className="corr-pair-val" style={{ color: '#2a78d6' }}>{fmtCorr(p.corr)}</span>
                </div>
              ))}
            </div>
            <div className="corr-pair-col">
              <h3>Most aligned <span className="muted">(redundant)</span></h3>
              {corr.mostAligned.map((p) => (
                <div key={`${p.a}-${p.b}`} className="corr-pair-row">
                  <span>{p.a} ↔ {p.b}</span>
                  <span className="corr-pair-val" style={{ color: DOWN }}>{fmtCorr(p.corr)}</span>
                </div>
              ))}
            </div>
            </div>
          </div>
        </div>
        )}
      </div>

      {drill && (
        <QuotesDrilldownModal
          etfs={drill.a === drill.b ? [drill.a] : [drill.a, drill.b]}
          onClose={() => setDrill(null)}
        />
      )}
    </section>
  );
}
