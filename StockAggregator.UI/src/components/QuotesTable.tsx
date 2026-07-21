import { useMemo } from 'react';
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
  // Build grouped columns: a group per trading day, a sub-column per capture time.
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
        return (
          <span className={row.isEtf ? 'symbol symbol-etf' : 'symbol symbol-member'}>
            {info.getValue()}
            {row.isEtf && row.description && <span className="etf-desc">{row.description}</span>}
          </span>
        );
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
              header: snap.runLabel.replace(' CT', ''),
              cell: (info) => <CellView cell={info.getValue()} />,
            }),
          ),
        }) as ColumnDef<SymbolRow, unknown>,
      );
    }

    return [symbolColumn, ...dayGroups];
  }, [data.snapshots]);

  const table = useReactTable({
    data: data.rows,
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
            <tr key={row.id} className={row.original.isEtf ? 'row-etf' : 'row-stock'}>
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
