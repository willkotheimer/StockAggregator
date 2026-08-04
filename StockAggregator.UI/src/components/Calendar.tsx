import { useMemo, useState } from 'react';

const WEEKDAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
const keyOf = (y: number, m: number, d: number) => `${y}-${pad(m + 1)}-${pad(d)}`; // m is 0-indexed
const dowOf = (iso: string) => new Date(`${iso}T00:00:00`).getDay();

export type CalendarMode = 'days' | 'range';

export interface DateRange {
  start: string;
  end: string | null;
}

interface CalendarProps {
  /// Snapshot days — selectable in 'days' mode, where the intraday grid needs captures.
  availableDates: Set<string>;
  /// Trading days in the backfilled history — selectable in 'range' mode. Much wider.
  tradingDates: Set<string>;
  selected: Set<string>;
  onToggleDate: (date: string) => void;
  onSetDates: (dates: string[]) => void;
  mode: CalendarMode;
  onSetMode: (mode: CalendarMode) => void;
  range: DateRange | null;
  onSetRange: (range: DateRange | null) => void;
  /// Weekday kept within the range (0=Sun..6=Sat), or null for every trading day.
  dow: number | null;
  onSetDow: (dow: number | null) => void;
}

/// Month calendar with two modes. 'days' multi-selects snapshot days for the
/// side-by-side intraday tables; 'range' picks a start/end span of trading days for
/// the close-only matrix, with an optional weekday filter applied inside the span.
export default function Calendar({
  availableDates, tradingDates, selected, onToggleDate, onSetDates,
  mode, onSetMode, range, onSetRange, dow, onSetDow,
}: CalendarProps) {
  // In range mode the selectable set is the full daily history, so the picker is not
  // limited to days the snapshot capture happened to run.
  const selectable = mode === 'range' ? tradingDates : availableDates;

  const latest = useMemo(() => {
    const arr = [...selectable].sort();
    return arr.length ? arr[arr.length - 1] : null;
  }, [selectable]);

  const [view, setView] = useState(() => {
    const arr = [...availableDates].sort();
    const base = arr.length ? new Date(`${arr[arr.length - 1]}T00:00:00`) : new Date();
    return { y: base.getFullYear(), m: base.getMonth() };
  });

  const cells = useMemo(() => {
    const startDow = new Date(view.y, view.m, 1).getDay();
    const daysInMonth = new Date(view.y, view.m + 1, 0).getDate();
    const out: (number | null)[] = [];
    for (let i = 0; i < startDow; i++) out.push(null);
    for (let d = 1; d <= daysInMonth; d++) out.push(d);
    return out;
  }, [view]);

  const monthAvailable = useMemo(
    () => [...selectable].filter((d) => d.startsWith(`${view.y}-${pad(view.m + 1)}-`)).sort(),
    [selectable, view],
  );
  const mondays = monthAvailable.filter((d) => dowOf(d) === 1);
  const fridays = monthAvailable.filter((d) => dowOf(d) === 5);

  const prevMonth = () => setView((v) => (v.m === 0 ? { y: v.y - 1, m: 11 } : { y: v.y, m: v.m - 1 }));
  const nextMonth = () => setView((v) => (v.m === 11 ? { y: v.y + 1, m: 0 } : { y: v.y, m: v.m + 1 }));

  // Range clicks cycle: first sets the start, second closes the span (swapping if
  // the user clicked backwards), a third starts over.
  const clickRange = (k: string) => {
    if (!range || range.end !== null) {
      onSetRange({ start: k, end: null });
      return;
    }
    onSetRange(k < range.start ? { start: k, end: range.start } : { start: range.start, end: k });
  };

  const inRange = (k: string) =>
    range !== null && range.end !== null && k >= range.start && k <= range.end;

  // Quick spans measured back from the newest trading day, snapped to what exists.
  const spanBack = (days: number) => {
    if (!latest) return;
    const end = new Date(`${latest}T00:00:00`);
    const start = new Date(end);
    start.setDate(start.getDate() - days);
    const startIso = `${start.getFullYear()}-${pad(start.getMonth() + 1)}-${pad(start.getDate())}`;
    onSetRange({ start: startIso, end: latest });
    setView({ y: end.getFullYear(), m: end.getMonth() });
  };

  return (
    <div className="calendar">
      <div className="cal-modes">
        <button
          type="button"
          className={`cal-mode${mode === 'days' ? ' active' : ''}`}
          onClick={() => onSetMode('days')}
        >
          Days
        </button>
        <button
          type="button"
          className={`cal-mode${mode === 'range' ? ' active' : ''}`}
          onClick={() => onSetMode('range')}
        >
          Range
        </button>
      </div>

      <div className="cal-head">
        <button type="button" onClick={prevMonth} aria-label="previous month">‹</button>
        <span className="cal-title">{MONTHS[view.m]} {view.y}</span>
        <button type="button" onClick={nextMonth} aria-label="next month">›</button>
      </div>

      <div className="cal-grid">
        {WEEKDAYS.map((w) => <div key={w} className="cal-dow">{w}</div>)}
        {cells.map((d, i) => {
          if (d === null) return <div key={`b${i}`} className="cal-cell empty" />;
          const k = keyOf(view.y, view.m, d);
          const avail = selectable.has(k);
          const isEnd = mode === 'range' && (range?.start === k || range?.end === k);
          const sel = mode === 'range' ? isEnd || inRange(k) : selected.has(k);
          // Days inside the span that the weekday filter excludes stay visibly in
          // the range but muted, so the filter's effect is legible on the calendar.
          const muted = mode === 'range' && inRange(k) && dow != null && dowOf(k) !== dow;
          return (
            <button
              key={k}
              type="button"
              className={
                `cal-cell${avail ? ' avail' : ' off'}${sel ? ' sel' : ''}` +
                `${isEnd ? ' cal-edge' : ''}${muted ? ' cal-muted' : ''}`
              }
              disabled={!avail}
              onClick={() => (mode === 'range' ? clickRange(k) : onToggleDate(k))}
            >
              {d}
            </button>
          );
        })}
      </div>

      {mode === 'days' ? (
        <div className="cal-quick">
          {latest && <button type="button" onClick={() => onSetDates([latest])}>Latest</button>}
          <button type="button" disabled={!mondays.length} onClick={() => onSetDates(mondays)}>All Mondays</button>
          <button type="button" disabled={!fridays.length} onClick={() => onSetDates(fridays)}>All Fridays</button>
          <button type="button" disabled={!monthAvailable.length} onClick={() => onSetDates(monthAvailable)}>Whole month</button>
          <button type="button" disabled={!selected.size} onClick={() => onSetDates([])}>Clear</button>
        </div>
      ) : (
        <>
          <div className="cal-quick">
            <button type="button" disabled={!latest} onClick={() => spanBack(30)}>1M</button>
            <button type="button" disabled={!latest} onClick={() => spanBack(91)}>3M</button>
            <button type="button" disabled={!latest} onClick={() => spanBack(182)}>6M</button>
            <button type="button" disabled={!latest} onClick={() => spanBack(365)}>1Y</button>
            <button type="button" disabled={!range} onClick={() => onSetRange(null)}>Clear</button>
          </div>

          {/* The weekday filter narrows the span rather than replacing it, so
              "Fridays" reads week-over-week across the whole range. */}
          <div className="cal-quick cal-dow-filter">
            <span className="pill-caption">Every:</span>
            <button
              type="button"
              className={dow == null ? 'active' : ''}
              onClick={() => onSetDow(null)}
            >
              Trading day
            </button>
            {[1, 2, 3, 4, 5].map((d) => (
              <button
                key={d}
                type="button"
                className={dow === d ? 'active' : ''}
                onClick={() => onSetDow(dow === d ? null : d)}
              >
                {WEEKDAYS[d]}
              </button>
            ))}
          </div>

          <p className="cal-range-hint subtle">
            {!range
              ? 'Click a start date, then an end date.'
              : range.end === null
                ? `Start ${range.start} — now click the end date.`
                : `${range.start} → ${range.end}`}
          </p>
        </>
      )}
    </div>
  );
}
