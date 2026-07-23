import { useEffect, useRef, useState } from 'react';
import { Offcanvas } from 'bootstrap';
import { useNavigate } from 'react-router-dom';
import { fetchCorrelations, fetchCrawlers, fetchHiddenSignal, fetchRotations } from '../api/client';
import type { CorrelationResponse, CrawlerResponse, HiddenSignalResponse, RotationResponse } from '../types';

const UP = '#157f3b';
const DOWN = '#c62828';

function pct(v: number | null | undefined, digits = 2): string {
  if (v == null) return '—';
  return `${v >= 0 ? '+' : ''}${v.toFixed(digits)}%`;
}

export default function OverviewPanel() {
  const ref = useRef<HTMLDivElement>(null);
  const oc = useRef<Offcanvas | null>(null);
  const navigate = useNavigate();

  const [rot, setRot] = useState<RotationResponse | null>(null);
  const [sig, setSig] = useState<HiddenSignalResponse | null>(null);
  const [corr, setCorr] = useState<CorrelationResponse | null>(null);
  const [crawl, setCrawl] = useState<CrawlerResponse | null>(null);
  const loaded = useRef(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    oc.current = Offcanvas.getOrCreateInstance(el, { scroll: true });
    const onShow = () => {
      if (loaded.current) return;
      loaded.current = true;
      fetchRotations().then(setRot).catch(() => {});
      fetchHiddenSignal().then(setSig).catch(() => {});
      fetchCorrelations().then(setCorr).catch(() => {});
      fetchCrawlers().then(setCrawl).catch(() => {});
    };
    el.addEventListener('show.bs.offcanvas', onShow);
    return () => { el.removeEventListener('show.bs.offcanvas', onShow); oc.current?.dispose(); };
  }, []);

  const go = (path: string) => { oc.current?.hide(); navigate(path); };

  const leader = rot?.rows[0];
  const laggard = rot && rot.rows.length ? rot.rows[rot.rows.length - 1] : undefined;
  const flagged = sig?.signals.filter((s) => s.isHiddenSignal) ?? [];
  const opposing = corr?.mostOpposing[0];
  const aligned = corr?.mostAligned[0];
  const topCrawler = crawl?.rows.find((r) => r.isSteadyCrawler) ?? crawl?.rows[0];
  const asOf = rot?.asOfDate ?? crawl?.asOfDate;

  return (
    <>
      <button type="button" className="overview-btn" onClick={() => oc.current?.toggle()}>▾ Overview</button>

      <div className="offcanvas offcanvas-top overview-panel" tabIndex={-1} ref={ref} aria-labelledby="overviewLabel">
        <div className="offcanvas-header">
          <h5 className="offcanvas-title" id="overviewLabel">Overview{asOf ? <span className="ov-asof"> · as of {asOf}</span> : null}</h5>
          <button type="button" className="drawer-close" onClick={() => oc.current?.hide()} aria-label="Close">✕</button>
        </div>
        <div className="offcanvas-body">
          <div className="overview-grid">
            <div className="overview-card">
              <h3>Rotations <button className="ov-view" onClick={() => go('/rotations')}>View →</button></h3>
              {rot ? (
                <>
                  <div className="ov-stat"><span>Leading</span><span><strong>{leader?.etf}</strong> <span style={{ color: (leader?.changePct ?? 0) >= 0 ? UP : DOWN }}>{pct(leader?.changePct)}</span></span></div>
                  <div className="ov-stat"><span>Lagging</span><span><strong>{laggard?.etf}</strong> <span style={{ color: (laggard?.changePct ?? 0) >= 0 ? UP : DOWN }}>{pct(laggard?.changePct)}</span></span></div>
                </>
              ) : <p className="ov-loading">…</p>}
            </div>

            <div className="overview-card">
              <h3>Hidden signals <button className="ov-view" onClick={() => go('/signals')}>View →</button></h3>
              {sig ? (
                <>
                  <div className="ov-big">{flagged.length}</div>
                  <div className="ov-stat"><span>flagged today</span><span>{flagged[0] ? `top ${flagged[0].etf}` : '—'}</span></div>
                </>
              ) : <p className="ov-loading">…</p>}
            </div>

            <div className="overview-card">
              <h3>Correlations <button className="ov-view" onClick={() => go('/rotations')}>View →</button></h3>
              {corr ? (
                <>
                  <div className="ov-stat"><span>Most opposing</span><span><strong>{opposing?.a} ↔ {opposing?.b}</strong> <span style={{ color: '#2a78d6' }}>{opposing?.corr.toFixed(2)}</span></span></div>
                  <div className="ov-stat"><span>Most aligned</span><span><strong>{aligned?.a} ↔ {aligned?.b}</strong> <span style={{ color: DOWN }}>{aligned?.corr.toFixed(2)}</span></span></div>
                </>
              ) : <p className="ov-loading">…</p>}
            </div>

            <div className="overview-card">
              <h3>Steady crawlers <button className="ov-view" onClick={() => go('/crawlers')}>View →</button></h3>
              {crawl ? (
                topCrawler ? (
                  <>
                    <div className="ov-stat"><span><strong>{topCrawler.symbol}</strong> {topCrawler.isSteadyCrawler ? '· steady' : ''}</span><span style={{ color: (topCrawler.returnPct ?? 0) >= 0 ? UP : DOWN }}>{pct(topCrawler.returnPct, 1)}</span></div>
                    <div className="ov-stat"><span>steadiness</span><span>{topCrawler.steadiness?.toFixed(2) ?? '—'}</span></div>
                  </>
                ) : <p className="subtle">none</p>
              ) : <p className="ov-loading">…</p>}
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
