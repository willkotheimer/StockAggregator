import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Offcanvas } from 'bootstrap';
import { useSearchParams } from 'react-router-dom';
import { fetchAvailableDates, fetchDays, fetchEtfGroups } from '../api/client';
import { useApi } from '../hooks/useApi';
import type { SymbolRow, WeekQuotesResponse } from '../types';
import Calendar from '../components/Calendar';
import QuotesTable from '../components/QuotesTable';
import StockChart from '../components/StockChart';

// Categorical palette (dataviz), assigned to charted symbols in selection order.
const CHART_PALETTE = ['#2a78d6', '#008300', '#e87ba4', '#eda100', '#1baf7a', '#eb6834', '#4a3aa7', '#e34948'];
const CHART_MAX = 20;
const up = (s: string) => s.toUpperCase();

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
  // Chart selection: ETFs (which bring their members) + individually-picked stocks.
  const [selectedEtfs, setSelectedEtfs] = useState<string[]>([]);
  const [selectedStocks, setSelectedStocks] = useState<string[]>([]);
  const didInitDay = useRef(false);

  // Bootstrap 5 offcanvas for the browse panel. No backdrop so the chart stays
  // visible/live; the chart is pushed right by the panel width when it opens.
  const offcanvasRef = useRef<HTMLDivElement>(null);
  const offcanvas = useRef<Offcanvas | null>(null);
  const [panelOpen, setPanelOpen] = useState(false);
  useEffect(() => {
    const el = offcanvasRef.current;
    if (!el) return;
    offcanvas.current = Offcanvas.getOrCreateInstance(el, { backdrop: false, scroll: true });
    const onShow = () => setPanelOpen(true);
    const onHide = () => setPanelOpen(false);
    el.addEventListener('show.bs.offcanvas', onShow);
    el.addEventListener('hide.bs.offcanvas', onHide);
    return () => {
      el.removeEventListener('show.bs.offcanvas', onShow);
      el.removeEventListener('hide.bs.offcanvas', onHide);
      offcanvas.current?.dispose();
    };
  }, []);

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

  const membersOf = useMemo(() => {
    const m = new Map<string, string[]>();
    for (const g of groups ?? []) m.set(g.etf, g.members);
    return m;
  }, [groups]);

  // Derive the display hierarchy and the flat, deduped, ordered symbol list.
  const selection = useMemo(() => {
    const etfGroups = selectedEtfs.map((etf) => ({ etf, members: membersOf.get(etf) ?? [] }));
    const memberSet = new Set(etfGroups.flatMap((g) => g.members.map(up)));
    const standalone = selectedStocks.filter((s) => !memberSet.has(up(s)));
    const ordered: string[] = [];
    const seen = new Set<string>();
    const push = (sym: string) => { if (!seen.has(up(sym))) { seen.add(up(sym)); ordered.push(sym); } };
    for (const g of etfGroups) { push(g.etf); g.members.forEach(push); }
    standalone.forEach(push);
    return { etfGroups, standalone, chartSymbols: ordered.slice(0, CHART_MAX) };
  }, [selectedEtfs, selectedStocks, membersOf]);

  const chartSymbols = selection.chartSymbols;
  const chartSet = useMemo(() => new Set(chartSymbols.map(up)), [chartSymbols]);
  const etfSelected = useMemo(() => new Set(selectedEtfs.map(up)), [selectedEtfs]);
  const colorOf = useCallback(
    (s: string) => CHART_PALETTE[Math.max(0, chartSymbols.findIndex((x) => up(x) === up(s))) % CHART_PALETTE.length],
    [chartSymbols],
  );

  const removeEtfAll = (etf: string) => {
    setSelectedEtfs((p) => p.filter((e) => up(e) !== up(etf)));
    const members = new Set((membersOf.get(etf) ?? []).map(up));
    setSelectedStocks((p) => p.filter((s) => !members.has(up(s))));
  };
  const removeEtfOnly = (etf: string) => {
    const members = membersOf.get(etf) ?? [];
    setSelectedEtfs((p) => p.filter((e) => up(e) !== up(etf)));
    setSelectedStocks((p) => { const have = new Set(p.map(up)); return [...p, ...members.filter((m) => !have.has(up(m)))]; });
  };
  const toggleChartEtf = (etf: string) => (etfSelected.has(up(etf)) ? removeEtfAll(etf) : setSelectedEtfs((p) => [...p, etf]));
  const toggleStock = (symbol: string) =>
    setSelectedStocks((p) => (p.some((s) => up(s) === up(symbol)) ? p.filter((s) => up(s) !== up(symbol)) : [...p, symbol]));
  const removeStock = (symbol: string) => setSelectedStocks((p) => p.filter((s) => up(s) !== up(symbol)));

  const filterRows = (rows: SymbolRow[]) =>
    visibleEtfs ? rows.filter((r) => visibleEtfs.has(r.groupEtf)) : rows;

  const sortedDates = [...selected].sort();
  const allShown = groups != null && visibleEtfs != null && visibleEtfs.size === groups.length;

  return (
    <section className="page quotes-page">
      <div className={`quotes-main${panelOpen ? ' pushed' : ''}`}>
        {!panelOpen && (
          <button type="button" className="drawer-open" onClick={() => offcanvas.current?.show()}>☰ Browse</button>
        )}
        <StockChart
          symbols={chartSymbols}
          colorOf={colorOf}
          etfGroups={selection.etfGroups}
          standalone={selection.standalone}
          onRemoveEtfAll={removeEtfAll}
          onRemoveEtfOnly={removeEtfOnly}
          onRemoveStock={removeStock}
          onPlotClick={() => offcanvas.current?.toggle()}
        />
      </div>

      <div className="offcanvas offcanvas-start browse-panel" tabIndex={-1} ref={offcanvasRef} aria-labelledby="browseLabel">
        <div className="offcanvas-header">
          <h5 className="offcanvas-title" id="browseLabel">Browse</h5>
          <button type="button" className="drawer-close" onClick={() => offcanvas.current?.hide()} aria-label="Close">✕</button>
        </div>
        <div className="offcanvas-body">
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
              <p className="subtle">Pick one or more days from the calendar.</p>
            )}
            {days && sortedDates.map((date) => (
              <QuotesTable
                key={date}
                data={{ snapshots: days.snapshots.filter((s) => s.date === date), rows: filterRows(days.rows) }}
                caption={formatDate(date)}
                expanded={expanded}
                onToggleGroup={toggleGroup}
                chartSymbols={chartSet}
                onToggleChart={toggleStock}
                onToggleEtf={toggleChartEtf}
                colorOf={colorOf}
              />
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
