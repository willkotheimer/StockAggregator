import { Bar, BarChart, Cell, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { fetchRotations } from '../api/client';
import { useApi } from '../hooks/useApi';

const UP = '#157f3b';
const DOWN = '#c62828';

export default function RotationsPage() {
  const { data, loading, error } = useApi(fetchRotations);

  if (loading) return <p>Loading…</p>;
  if (error) return <p className="error">Error: {error}</p>;
  if (!data || data.rows.length === 0) return <p>No analytics computed yet — check back after the next nightly rollup.</p>;

  const chartData = data.rows.map((r) => ({
    etf: r.etf,
    description: r.description,
    changePct: r.changePct ?? 0,
  }));

  return (
    <section className="page">
      <div className="analytics-head">
        <h2>Rotations</h2>
        <span className="asof">computed as of {data.asOfDate}</span>
      </div>
      <p className="subtle">Sector ETFs ranked by daily change — leaders on top, laggards below.</p>

      <div className="chart-wrap" style={{ height: chartData.length * 34 + 48 }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData} layout="vertical" margin={{ left: 8, right: 28, top: 8, bottom: 8 }}>
            <XAxis type="number" tickFormatter={(v) => `${v}%`} />
            <YAxis type="category" dataKey="etf" width={52} tickLine={false} />
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
    </section>
  );
}
