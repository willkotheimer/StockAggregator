import { useEffect, useMemo, useState } from 'react';
import { fetchAvailableDates, fetchDays } from '../api/client';
import { useApi } from '../hooks/useApi';
import type { WeekQuotesResponse } from '../types';
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

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [days, setDays] = useState<WeekQuotesResponse | null>(null);
  const [loadingDays, setLoadingDays] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const availableSet = useMemo(() => new Set(available ?? []), [available]);

  // Default to the latest available day once the list loads.
  useEffect(() => {
    if (available && available.length > 0) {
      setSelected((prev) => (prev.size === 0 ? new Set([available[available.length - 1]]) : prev));
    }
  }, [available]);

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

  const sortedDates = [...selected].sort();

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

      <div className="day-tables">
        {loadingDays && <p>Loading…</p>}
        {error && <p className="error">Error: {error}</p>}
        {!loadingDays && !error && sortedDates.length === 0 && (
          <p className="subtle">Pick one or more days from the calendar to compare side by side.</p>
        )}
        {days && sortedDates.map((date) => (
          <QuotesTable
            key={date}
            data={{ snapshots: days.snapshots.filter((s) => s.date === date), rows: days.rows }}
            caption={formatDate(date)}
            expanded={expanded}
            onToggleGroup={toggleGroup}
          />
        ))}
      </div>
    </section>
  );
}
