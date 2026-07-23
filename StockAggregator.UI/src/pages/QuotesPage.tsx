import { useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { fetchAvailableDates, fetchDays, fetchEtfGroups } from '../api/client';
import { useApi } from '../hooks/useApi';
import type { SymbolRow, WeekQuotesResponse } from '../types';
import Calendar from '../components/Calendar';
import QuotesTable from '../components/QuotesTable';

function formatDate(iso: string): string {
  return new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
  });
}

export default function QuotesPage() {
  const { data: available, loading: datesLoading, error: datesError } = useApi(fetchAvailableDates);
  const { data: groups } = useApi(fetchEtfGroups);
  const [searchParams] = useSearchParams();

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [days, setDays] = useState<WeekQuotesResponse | null>(null);
  const [loadingDays, setLoadingDays] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  // null until initialised; then the set of ETF groups currently shown.
  const [visibleEtfs, setVisibleEtfs] = useState<Set<string> | null>(null);
  const didInitDay = useRef(false);

  const availableSet = useMemo(() => new Set(available ?? []), [available]);

  // One-time day init: a ?day= deep link wins, else default to the latest day.
  useEffect(() => {
    if (!available || available.length === 0 || didInitDay.current) return;
    didInitDay.current = true;
    const dayParam = searchParams.get('day');
    setSelected(new Set([dayParam && availableSet.has(dayParam) ? dayParam : available[available.length - 1]]));
  }, [available, availableSet, searchParams]);

  // ETF visibility: a ?etfs= deep link shows only those; otherwise show all.
  useEffect(() => {
    if (!groups) return;
    const all = groups.map((g) => g.etf);
    const etfsParam = searchParams.get('etfs');
    if (etfsParam) {
      const wanted = new Set(etfsParam.split(',').map((s) => s.trim().toUpperCase()));
      setVisibleEtfs(new Set(all.filter((e) => wanted.has(e.toUpperCase()))));
    } else {
      setVisibleEtfs(new Set(all));
    }
  }, [groups, searchParams]);

  // Fetch the selected days' data whenever the selection changes.
  useEffect(() => {
    const dates = [...selected].sort();
    if (dates.length === 0) {
      setDays(null);
      return;
    }
    let live = true;
    setLoadingDays(true);
    setError(null);
    fetchDays(dates)
      .then((d) => { if (live) setDays(d); })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setLoadingDays(false); });
    return () => { live = false; };
  }, [selected]);

  const toggleDate = (date: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(date)) next.delete(date);
      else next.add(date);
      return next;
    });

  const toggleGroup = (etf: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(etf)) next.delete(etf);
      else next.add(etf);
      return next;
    });

  const toggleEtf = (etf: string) =>
    setVisibleEtfs((prev) => {
      const next = new Set(prev ?? []);
      if (next.has(etf)) next.delete(etf);
      else next.add(etf);
      return next;
    });

  const showAllEtfs = () => setVisibleEtfs(new Set((groups ?? []).map((g) => g.etf)));

  // Expand/collapse every ETF group at once (only the ones currently shown).
  const shownEtfs = useMemo(
    () => (groups ?? []).map((g) => g.etf).filter((e) => !visibleEtfs || visibleEtfs.has(e)),
    [groups, visibleEtfs],
  );
  const allExpanded = shownEtfs.length > 0 && shownEtfs.every((e) => expanded.has(e));
  const openAll = () => setExpanded(new Set(shownEtfs));
  const collapseAll = () => setExpanded(new Set());

  const filterRows = (rows: SymbolRow[]) =>
    visibleEtfs ? rows.filter((r) => visibleEtfs.has(r.groupEtf)) : rows;

  const sortedDates = [...selected].sort();
  const allShown = groups != null && visibleEtfs != null && visibleEtfs.size === groups.length;

  return (
    <section className="page quotes-page">
      {datesLoading && <p>Loading calendar…</p>}
      {datesError && <p className="error">Error: {datesError}</p>}
      {available && (
        <Calendar
          availableDates={availableSet}
          selected={selected}
          onToggleDate={toggleDate}
          onSetDates={(dates) => setSelected(new Set(dates))}
        />
      )}

      {groups && visibleEtfs && (
        <div className="pill-row etf-filter">
          <span className="pill-caption">ETFs:</span>
          {groups.map((g) => (
            <button
              key={g.etf}
              type="button"
              className={`pill pill-etf${visibleEtfs.has(g.etf) ? ' active' : ''}`}
              title={g.description}
              onClick={() => toggleEtf(g.etf)}
            >
              {g.etf}
            </button>
          ))}
          <button type="button" className="pill" disabled={allShown} onClick={showAllEtfs}>All</button>
        </div>
      )}

      {days && sortedDates.length > 0 && shownEtfs.length > 0 && (
        <div className="pill-row">
          <button type="button" className="pill" onClick={allExpanded ? collapseAll : openAll}>
            {allExpanded ? 'Collapse all' : 'Open all'}
          </button>
        </div>
      )}

      <div className="day-tables">
        {loadingDays && <p>Loading…</p>}
        {error && <p className="error">Error: {error}</p>}
        {!loadingDays && !error && sortedDates.length === 0 && (
          <p className="subtle">Pick one or more days from the calendar to compare side by side.</p>
        )}
        {days && sortedDates.map((date) => (
          <QuotesTable
            key={date}
            data={{ snapshots: days.snapshots.filter((s) => s.date === date), rows: filterRows(days.rows) }}
            caption={formatDate(date)}
            expanded={expanded}
            onToggleGroup={toggleGroup}
          />
        ))}
      </div>
    </section>
  );
}
