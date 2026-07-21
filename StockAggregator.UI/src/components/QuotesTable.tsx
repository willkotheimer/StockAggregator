import { useMemo, useState } from 'react';
import {
  createColumnHelper,
  flexRender,
  getCoreRowModel,
  useReactTable,
  type ColumnDef,
} from '@tanstack/react-table';
import type { QuoteCell, SnapshotColumn, SymbolRow, WeekQuotesResponse } from '../types';

const columnHelper = createColumnHelper<SymbolRow>();

function formatDateHeader(dateIso: string): string {
  const d = new Date(`${dateIso}T00:00:00`);
  return d.toLocaleDateString(undefined, { weekday: 'short', month: 'numeric', day: 'numeric' });
}

// "08:30 CT" -> "8:30 AM"; "13:00 CT" -> "1 PM".
function formatRunLabel(runLabel: string): string {
  const match = runLabel.match(/^(\d{1,2}):(\d{2})/);
  if (!match) {
    return runLabel.replace(' CT', '');
  }
  let hour = Number(match[1]);
  const minutes = match[2];
  const period = hour >= 12 ? 'PM' : 'AM';
  hour = hour % 12 || 12;
  return minutes === '00' ? `${hour} ${period}` : `${hour}:${minutes} ${period}`;
}

function CellView({ cell }: { cell: QuoteCell | undefined }) {
  if (!cell || cell.price == null) {
    return <span className="cell-empty">—</span>;
  }

  const cp = cell.changePercent;
  const dir = cp == null ? '' : cp >= 0 ? 'up' : 'down';

  return (
    <span className="cell">
      <span className="price">{cell.price.toFixed(2)}</span>
      {cp != null && (
        <span className={`chg ${dir}`}>
          {cp >= 0 ? '+' : ''}
          {cp.toFixed(2)}%
        </span>
      )}
    </span>
  );
}

export default function QuotesTable({ data }: { data: WeekQuotesResponse }) {
  // Which ETF groups are expanded. Default closed: only ETF rows show.
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const toggle = (etf: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(etf)) {
        next.delete(etf);
      } else {
        next.add(etf);
      }
      return next;
    });

  // Show every ETF row, plus member rows only for expanded groups.
  const visibleRows = useMemo(
    () => data.rows.filter((row) => row.isEtf || expanded.has(row.groupEtf)),
    [data.rows, expanded],
  );

  // Grouped columns: a group per trading day, a sub-column per capture time.
  const columns = useMemo<ColumnDef<SymbolRow, unknown>[]>(() => {
    const byDate = new Map<string, SnapshotColumn[]>();
    for (const snap of data.snapshots) {
      const existing = byDate.get(snap.date) ?? [];
      existing.push(snap);
      byDate.set(snap.date, existing);
    }

    const symbolColumn = columnHelper.accessor((row) => row.symbol, {
      id: 'symbol',
      header: 'Symbol',
      cell: (info) => {
        const row = info.row.original;
        if (row.isEtf) {
          return (
            <span className="symbol symbol-etf">
              <span className="caret">{expanded.has(row.groupEtf) ? '▾' : '▸'}</span>
              {info.getValue()}
              {row.description && <span className="etf-desc">{row.description}</span>}
            </span>
          );
        }
        return <span className="symbol symbol-member">{info.getValue()}</span>;
      },
    }) as ColumnDef<SymbolRow, unknown>;

    const dayGroups: ColumnDef<SymbolRow, unknown>[] = [];
    for (const [date, snaps] of byDate) {
      dayGroups.push(
        columnHelper.group({
          id: date,
          header: formatDateHeader(date),
          columns: snaps.map((snap) =>
            columnHelper.accessor((row) => row.cells[snap.key], {
              id: snap.key,
              header: formatRunLabel(snap.runLabel),
              cell: (info) => <CellView cell={info.getValue()} />,
            }),
          ),
        }) as ColumnDef<SymbolRow, unknown>,
      );
    }

    return [symbolColumn, ...dayGroups];
  }, [data.snapshots, expanded]);

  const table = useReactTable({
    data: visibleRows,
    columns,
    getRowId: (row) => row.id,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <div className="table-wrap">
      <table className="quotes-table">
        <thead>
          {table.getHeaderGroups().map((headerGroup) => (
            <tr key={headerGroup.id}>
              {headerGroup.headers.map((header) => (
                <th key={header.id} colSpan={header.colSpan} className={header.depth === 1 ? 'day-header' : ''}>
                  {header.isPlaceholder
                    ? null
                    : flexRender(header.column.columnDef.header, header.getContext())}
                </th>
              ))}
            </tr>
          ))}
        </thead>
        <tbody>
          {table.getRowModel().rows.map((row) => (
            <tr
              key={row.id}
              className={row.original.isEtf ? 'row-etf' : 'row-stock'}
              onClick={row.original.isEtf ? () => toggle(row.original.groupEtf) : undefined}
            >
              {row.getVisibleCells().map((cell) => (
                <td key={cell.id}>
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
