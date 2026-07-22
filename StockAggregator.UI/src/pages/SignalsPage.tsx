import { fetchHiddenSignal } from '../api/client';
import { useApi } from '../hooks/useApi';

function pct(v: number | null): string {
  if (v == null) return '—';
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`;
}

function dirClass(v: number | null): string {
  if (v == null) return '';
  return v >= 0 ? 'up' : 'down';
}

export default function SignalsPage() {
  const { data, loading, error } = useApi(fetchHiddenSignal);

  if (loading) return <p>Loading…</p>;
  if (error) return <p className="error">Error: {error}</p>;
  if (!data || data.signals.length === 0) return <p>No analytics computed yet — check back after the next nightly rollup.</p>;

  return (
    <section className="page">
      <div className="analytics-head">
        <h2>Hidden Signals</h2>
        <span className="asof">computed as of {data.asOfDate}</span>
      </div>
      <p className="subtle">
        ETFs rising while most tracked members are flat or negative — the gain is coming from a few
        names (or holdings you don&apos;t track). Flagged cards are the strongest tells.
      </p>

      <div className="signal-grid">
        {data.signals.map((s) => (
          <div key={s.etf} className={s.isHiddenSignal ? 'signal-card flagged' : 'signal-card'}>
            <div className="signal-top">
              <span className="signal-etf">{s.etf}</span>
              <span className="signal-desc">{s.description}</span>
              {s.isHiddenSignal && <span className="badge">HIDDEN</span>}
            </div>
            <div className="signal-meta">
              <span className={`signal-change ${dirClass(s.etfChangePct)}`}>{pct(s.etfChangePct)}</span>
              <span className="muted">· {s.membersUp}/{s.membersTracked} members up</span>
            </div>
            <div className="member-list">
              {s.members.map((m) => (
                <div key={m.symbol} className="member-row">
                  <span className="member-sym">{m.symbol}</span>
                  <span className={`member-val ${dirClass(m.changePct)}`}>{pct(m.changePct)}</span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
