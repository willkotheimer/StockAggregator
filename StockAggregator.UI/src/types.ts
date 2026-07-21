export interface QuoteCell {
  price: number | null;
  changePercent: number | null;
}

export interface SnapshotColumn {
  key: string;
  date: string;
  runLabel: string;
  capturedAtUtc: string;
}

export interface SymbolRow {
  id: string;
  symbol: string;
  isEtf: boolean;
  groupEtf: string;
  description: string | null;
  cells: Record<string, QuoteCell>;
}

export interface WeekQuotesResponse {
  snapshots: SnapshotColumn[];
  rows: SymbolRow[];
}
