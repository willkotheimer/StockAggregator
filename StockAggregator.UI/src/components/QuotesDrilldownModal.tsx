import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { fetchAvailableDates, fetchDays } from '../api/client';
import { useApi } from '../hooks/useApi';
import type { WeekQuotesResponse } from '../types';
import QuotesTable from './QuotesTable';

function niceDate(iso: string): string {
  return new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, {
    weekday: 'short', month: 'short', day: 'numeric',
  });
}

interface Props {
  etfs: string[];
  onClose: () => void;
}

/// A closeable modal that drills into one or two ETF groups' quotes for the
/// current day (or the latest available). Reuses QuotesTable; no calendar.
export default function QuotesDrilldownModal({ etfs, onClose }: Props) {
  const { data: available } = useApi(fetchAvailableDates);
  const [days, setDays] = useState<WeekQuotesResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(etfs));

  const etfSet = useMemo(() => new Set(etfs.map((e) => e.toUpperCase())), [etfs]);

  // Today if it has data, else the latest available day.
  const targetDay = useMemo(() => {
    if (!available || available.length === 0) return null;
    const now = new Date();
    const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    return available.includes(today) ? today : available[available.length - 1];
  }, [available]);

  useEffect(() => {
    if (!targetDay) return;
    let live = true;
    setLoading(true);
    setError(null);
    fetchDays([targetDay])
      .then((d) => { if (live) setDays(d); })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [targetDay]);

  // Close on Escape.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const toggleGroup = (etf: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(etf)) next.delete(etf);
      else next.add(etf);
      return next;
    });

  const rows = days ? days.rows.filter((r) => etfSet.has(r.groupEtf.toUpperCase())) : [];
  const title = etfs.length > 1 && etfs[0] !== etfs[1] ? `${etfs[0]} × ${etfs[1]}` : etfs[0];

  return createPortal(
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h3>{title}{targetDay ? ` — ${niceDate(targetDay)}` : ''}</h3>
          <button type="button" className="modal-close" onClick={onClose} aria-label="Close">×</button>
        </div>
        <div className="modal-body">
          {loading && <p>Loading…</p>}
          {error && <p className="error">Error: {error}</p>}
          {!loading && !error && days && (
            rows.length === 0 ? (
              <p className="subtle">No quotes for {targetDay ? niceDate(targetDay) : 'today'} yet.</p>
            ) : (
              <QuotesTable
                data={{ snapshots: days.snapshots.filter((s) => s.date === targetDay), rows }}
                expanded={expanded}
                onToggleGroup={toggleGroup}
              />
            )
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
}
