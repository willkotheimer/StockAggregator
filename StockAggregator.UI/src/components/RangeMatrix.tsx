import { useMemo, useState } from 'react';
import type { RangeMatrixResponse, RangeMatrixRow, TrendSummary } from '../types';
import { SYMBOL_NAMES } from '../symbolNames';

type SortKey = 'default' | 'net' | 'band' | 'position';

function formatDateHeader(dateIso: string): string {
  return new Date(`${dateIso}T00:00:00`).toLocaleDateString(undefined, { month: 'numeric', day: 'numeric' });
}

function fmtPct(v: number | null): string {
  if (v == null) return '—';
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`;
}

/// Close + move versus the previous *selected* column, so a Fridays-only range
/// reads week-over-week rather than day-over-day.
function CloseCell({ close, change }: { close: number | null; change: number | null }) {
  if (close == null) return <span className="cell-empty">—</span>;
  const dir = change == null ? '' : change >= 0 ? 'up' : 'down';
  return (
    <span className="cell">
      <span className="price">{close.toFixed(2)}</span>
      {change != null && <span className={`chg ${dir}`}>{fmtPct(change)}</span>}
    </span>
  );
}

/// Describes what happened over the range — deliberately not a forecast. "Channel"
/// plus a low band position is the mean-reversion setup; a strong net with a high
/// band position is momentum continuation.
function TrendCell({ trend }: { trend: TrendSummary }) {
  const { direction, netPct, bandPct, positionInBand } = trend;

  if (direction === 'Unknown') {
    return <span className="cell-empty">—</span>;
  }

  if (direction === 'Channeling') {
    const half = bandPct == null ? null : bandPct / 2;
    return (
      <span className="trend trend-flat">
        <span className="trend-label">Channel</span>
        <span className="trend-num">{half == null ? '—' : `±${half.toFixed(2)}%`}</span>
        {positionInBand != null && (
          <span className="trend-pos" title="Where the last close sits between the range low and high">
            at {positionInBand.toFixed(0)}% of band
          </span>
        )}
      </span>
    );
  }

  const dir = direction === 'Up' ? 'up' : 'down';
  return (
    <span className={`trend trend-${dir}`}>
      <span className="trend-label">{direction}</span>
      <span className="trend-num">{fmtPct(netPct)}</span>
      {positionInBand != null && (
        <span className="trend-pos" title="Where the last close sits between the range low and high">
          at {positionInBand.toFixed(0)}% of band
        </span>
      )}
    </span>
  );
}

interface Props {
  data: RangeMatrixResponse;
  /// ETFs whose member rows are expanded. Collapsed by default: the point of range
  /// mode is the wide date axis, so the row axis starts small.
  expanded: Set<string>;
  onToggleGroup: (etf: string) => void;
  visibleEtfs: Set<string> | null;
  chartSymbols?: Set<string>;
  onToggleChart?: (symbol: string) => void;
  onAddEtf?: (etf: string) => void;
  colorOf?: (symbol: string) => string;
}

export default function RangeMatrix({
  data, expanded, onToggleGroup, visibleEtfs, chartSymbols, onToggleChart, onAddEtf, colorOf,
}: Props) {
  const [sort, setSort] = useState<SortKey>('default');
  const [desc, setDesc] = useState(true);

  const rows = useMemo(() => {
    const shown = data.rows.filter(
      (r) => (!visibleEtfs || visibleEtfs.has(r.groupEtf)) && (r.isEtf || expanded.has(r.groupEtf)),
    );

    if (sort === 'default') return shown;

    // Sorting flattens the ETF grouping on purpose — ranking across every symbol is
    // the whole point of a screener column.
    const pick = (r: RangeMatrixRow): number | null =>
      sort === 'net' ? r.trend.netPct : sort === 'band' ? r.trend.bandPct : r.trend.positionInBand;

    return [...shown].sort((a, b) => {
      const av = pick(a);
      const bv = pick(b);
      if (av == null && bv == null) return 0;
      if (av == null) return 1; // rows with no verdict sink to the bottom either way
      if (bv == null) return -1;
      return desc ? bv - av : av - bv;
    });
  }, [data.rows, expanded, visibleEtfs, sort, desc]);

  const toggleSort = (key: SortKey) => {
    if (sort === key) {
      setDesc((d) => !d);
      return;
    }
    setSort(key);
    setDesc(true);
  };

  const sortArrow = (key: SortKey) => (sort === key ? (desc ? ' ▼' : ' ▲') : '');

  // Where official closes stop and rollup prices begin, used to caption the seam
  // and to mark the affected column headers.
  const provisionalIndex = data.provisionalFrom ? data.dates.indexOf(data.provisionalFrom) : -1;

  if (data.dates.length === 0) {
    return <p className="subtle">No trading days in that range{data.dayOfWeek != null && ' for that weekday'}.</p>;
  }

  return (
    <div className="table-wrap range-matrix-wrap">
      <div className="table-caption">
        {data.from} → {data.to} · {data.dates.length} column{data.dates.length === 1 ? '' : 's'} · closing price
      </div>
      {data.provisionalFrom && (
        <p className="rm-provisional subtle">
          Closes through {provisionalIndex > 0 ? data.dates[provisionalIndex - 1] : '—'}; from{' '}
          {data.provisionalFrom} the price is the last snapshot of the session (14:30 CT), not the closing print.
        </p>
      )}
      {/* Only the grid scrolls — the caption and the provisional note sit outside it,
          so they stay put instead of sliding away when the date axis is scrolled. */}
      <div className="range-matrix-scroll">
      <table className="quotes-table range-matrix">
        <thead>
          <tr>
            <th className="rm-sym-col">Symbol</th>
            {data.dates.map((d, i) => (
              <th
                key={d}
                className={`rm-date${provisionalIndex >= 0 && i >= provisionalIndex ? ' rm-provisional-col' : ''}`}
                title={provisionalIndex >= 0 && i >= provisionalIndex ? 'Last snapshot of the session (14:30 CT), not the closing print' : undefined}
              >
                {formatDateHeader(d)}
                {provisionalIndex >= 0 && i >= provisionalIndex && <span className="rm-prov-mark">*</span>}
              </th>
            ))}
            <th className="rm-trend rm-trend-verdict rm-sortable" onClick={() => toggleSort('net')} title="Net move, first to last close. Click to sort.">
              Trend{sortArrow('net')}
            </th>
            <th className="rm-trend rm-trend-band rm-sortable" onClick={() => toggleSort('band')} title="Low-to-high spread over the range. Click to sort.">
              Band{sortArrow('band')}
            </th>
            <th className="rm-trend rm-trend-pos rm-sortable" onClick={() => toggleSort('position')} title="Where the last close sits in the band. Click to sort.">
              In band{sortArrow('position')}
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const inChart = chartSymbols?.has(row.symbol);
            const name = SYMBOL_NAMES[row.symbol];
            return (
              <tr
                key={row.id}
                className={row.isEtf ? 'row-etf' : 'row-stock'}
                onClick={
                  row.isEtf
                    ? () => onToggleGroup(row.groupEtf)
                    : onToggleChart
                      ? () => onToggleChart(row.symbol)
                      : undefined
                }
              >
                <td className="rm-sym-col">
                  {row.isEtf ? (
                    <span className="symbol symbol-etf">
                      <span className="caret">{expanded.has(row.groupEtf) ? '▾' : '▸'}</span>
                      {inChart && <span className="chart-dot" style={{ background: colorOf?.(row.symbol) }} />}
                      {row.symbol}
                      {row.description && <span className="etf-desc">{row.description}</span>}
                      {onAddEtf && (
                        <button
                          type="button"
                          className={`chart-add${inChart ? ' active' : ''}`}
                          title={inChart ? 'Remove ETF from chart' : 'Add ETF + stocks to chart'}
                          onClick={(e) => { e.stopPropagation(); onAddEtf(row.groupEtf); }}
                        >{inChart ? '✓' : '+'}</button>
                      )}
                    </span>
                  ) : (
                    <span className="symbol symbol-member">
                      {inChart && <span className="chart-dot" style={{ background: colorOf?.(row.symbol) }} />}
                      {row.symbol}
                      {name && <span className="member-name">{name}</span>}
                    </span>
                  )}
                </td>
                {data.dates.map((d, i) => (
                  <td key={d} className="rm-date">
                    <CloseCell close={row.closes[i] ?? null} change={row.changes[i] ?? null} />
                  </td>
                ))}
                <td className="rm-trend rm-trend-verdict"><TrendCell trend={row.trend} /></td>
                <td className="rm-trend rm-trend-band rm-num">{row.trend.bandPct == null ? '—' : `${row.trend.bandPct.toFixed(2)}%`}</td>
                <td className="rm-trend rm-trend-pos rm-num">{row.trend.positionInBand == null ? '—' : `${row.trend.positionInBand.toFixed(0)}%`}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      </div>
    </div>
  );
}
