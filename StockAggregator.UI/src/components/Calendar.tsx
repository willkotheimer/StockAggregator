import { useMemo, useState } from 'react';

const WEEKDAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
const keyOf = (y: number, m: number, d: number) => `${y}-${pad(m + 1)}-${pad(d)}`; // m is 0-indexed
const dowOf = (iso: string) => new Date(`${iso}T00:00:00`).getDay();

interface CalendarProps {
  availableDates: Set<string>;
  selected: Set<string>;
  onToggleDate: (date: string) => void;
  onSetDates: (dates: string[]) => void;
}

/// Multi-select month calendar. Only days with captured quotes are selectable;
/// quick-picks operate on the visible month.
export default function Calendar({ availableDates, selected, onToggleDate, onSetDates }: CalendarProps) {
  const latest = useMemo(() => {
    const arr = [...availableDates].sort();
    return arr.length ? arr[arr.length - 1] : null;
  }, [availableDates]);

  const [view, setView] = useState(() => {
    const base = latest ? new Date(`${latest}T00:00:00`) : new Date();
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
    () => [...availableDates].filter((d) => d.startsWith(`${view.y}-${pad(view.m + 1)}-`)).sort(),
    [availableDates, view],
  );
  const mondays = monthAvailable.filter((d) => dowOf(d) === 1);
  const fridays = monthAvailable.filter((d) => dowOf(d) === 5);

  const prevMonth = () => setView((v) => (v.m === 0 ? { y: v.y - 1, m: 11 } : { y: v.y, m: v.m - 1 }));
  const nextMonth = () => setView((v) => (v.m === 11 ? { y: v.y + 1, m: 0 } : { y: v.y, m: v.m + 1 }));

  return (
    <div className="calendar">
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
          const avail = availableDates.has(k);
          const sel = selected.has(k);
          return (
            <button
              key={k}
              type="button"
              className={`cal-cell${avail ? ' avail' : ' off'}${sel ? ' sel' : ''}`}
              disabled={!avail}
              onClick={() => onToggleDate(k)}
            >
              {d}
            </button>
          );
        })}
      </div>

      <div className="cal-quick">
        {latest && <button type="button" onClick={() => onSetDates([latest])}>Latest</button>}
        <button type="button" disabled={!mondays.length} onClick={() => onSetDates(mondays)}>All Mondays</button>
        <button type="button" disabled={!fridays.length} onClick={() => onSetDates(fridays)}>All Fridays</button>
        <button type="button" disabled={!monthAvailable.length} onClick={() => onSetDates(monthAvailable)}>Whole month</button>
        <button type="button" disabled={!selected.size} onClick={() => onSetDates([])}>Clear</button>
      </div>
    </div>
  );
}
