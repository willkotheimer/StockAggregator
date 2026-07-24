import { useMemo } from 'react';
import { useQueries } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { fetchCorrelations, fetchCrawlers, fetchHiddenSignal, fetchRotations } from '../api/client';

const UP = '#157f3b';
const DOWN = '#c62828';

function pct(v: number | null | undefined, digits = 2): string {
  if (v == null) return '—';
  return `${v >= 0 ? '+' : ''}${v.toFixed(digits)}%`;
}

export function fmtUpdated(ms: number): string {
  if (!ms || !Number.isFinite(ms)) return '';
  const d = new Date(ms);
  const sameDay = d.toDateString() === new Date().toDateString();
  return sameDay
    ? d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
    : d.toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

/// A full-width "at a glance" hero shown on every page. Stays hidden until all
/// four analytics have loaded (React Query caches them so it loads once), so the
/// user never sees a half-populated box.
export default function OverviewHero() {
  const navigate = useNavigate();

  const results = useQueries({
    queries: [
      { queryKey: ['rotations'], queryFn: fetchRotations },
      { queryKey: ['hiddenSignal'], queryFn: fetchHiddenSignal },
      { queryKey: ['correlations', 60], queryFn: () => fetchCorrelations(60) },
      { queryKey: ['crawlers', 63], queryFn: () => fetchCrawlers(63) },
    ],
  });

  const [rotR, sigR, corrR, crawlR] = results;
  const allReady = results.every((r) => r.isSuccess);
  const lastUpdated = Math.min(...results.map((r) => r.dataUpdatedAt || Infinity));
  const revalidating = results.some((r) => r.isFetching);

  const view = useMemo(() => {
    if (!rotR.data || !sigR.data || !corrR.data || !crawlR.data) return null;
    const rot = rotR.data, sig = sigR.data, corr = corrR.data, crawl = crawlR.data;
    return {
      asOf: rot.asOfDate ?? crawl.asOfDate,
      leader: rot.rows[0],
      laggard: rot.rows.length ? rot.rows[rot.rows.length - 1] : undefined,
      flagged: sig.signals.filter((s) => s.isHiddenSignal),
      opposing: corr.mostOpposing[0],
      aligned: corr.mostAligned[0],
      topCrawler: crawl.rows.find((r) => r.isSteadyCrawler) ?? crawl.rows[0],
    };
  }, [rotR.data, sigR.data, corrR.data, crawlR.data]);

  // Hidden entirely until everything is in.
  if (!allReady || !view) return null;

  const { leader, laggard, flagged, opposing, aligned, topCrawler } = view;

  return (
    <div className="overview-hero">
      <div className="overview-hero-head">
        <span className="overview-hero-title">At a glance</span>
        {view.asOf && <span className="ov-asof">as of {view.asOf}</span>}
        {lastUpdated !== Infinity && (
          <span className="ov-asof">· updated {fmtUpdated(lastUpdated)}{revalidating ? ' · refreshing…' : ''}</span>
        )}
      </div>
      <div className="overview-grid">
        <div className="overview-card">
          <h3>Rotations <button className="ov-view" onClick={() => navigate('/rotations')}>View →</button></h3>
          <div className="ov-stat"><span>Leading</span><span><strong>{leader?.etf}</strong> <span style={{ color: (leader?.changePct ?? 0) >= 0 ? UP : DOWN }}>{pct(leader?.changePct)}</span></span></div>
          <div className="ov-stat"><span>Lagging</span><span><strong>{laggard?.etf}</strong> <span style={{ color: (laggard?.changePct ?? 0) >= 0 ? UP : DOWN }}>{pct(laggard?.changePct)}</span></span></div>
        </div>

        <div className="overview-card">
          <h3>Hidden signals <button className="ov-view" onClick={() => navigate('/signals')}>View →</button></h3>
          <div className="ov-big">{flagged.length}</div>
          <div className="ov-stat"><span>flagged today</span><span>{flagged[0] ? `top ${flagged[0].etf}` : '—'}</span></div>
        </div>

        <div className="overview-card">
          <h3>Correlations <button className="ov-view" onClick={() => navigate('/rotations')}>View →</button></h3>
          <div className="ov-stat"><span>Most opposing</span><span><strong>{opposing?.a} ↔ {opposing?.b}</strong> <span style={{ color: '#2a78d6' }}>{opposing?.corr.toFixed(2)}</span></span></div>
          <div className="ov-stat"><span>Most aligned</span><span><strong>{aligned?.a} ↔ {aligned?.b}</strong> <span style={{ color: DOWN }}>{aligned?.corr.toFixed(2)}</span></span></div>
        </div>

        <div className="overview-card">
          <h3>Steady crawlers <button className="ov-view" onClick={() => navigate('/crawlers')}>View →</button></h3>
          {topCrawler ? (
            <>
              <div className="ov-stat"><span><strong>{topCrawler.symbol}</strong>{topCrawler.isSteadyCrawler ? ' · steady' : ''}</span><span style={{ color: (topCrawler.returnPct ?? 0) >= 0 ? UP : DOWN }}>{pct(topCrawler.returnPct, 1)}</span></div>
              <div className="ov-stat"><span>steadiness</span><span>{topCrawler.steadiness?.toFixed(2) ?? '—'}</span></div>
            </>
          ) : <p className="subtle">none</p>}
        </div>
      </div>
    </div>
  );
}
