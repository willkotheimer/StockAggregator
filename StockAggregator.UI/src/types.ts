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

export interface RotationRow {
  rank: number;
  etf: string;
  description: string;
  changePct: number | null;
}

export interface RotationResponse {
  asOfDate: string | null;
  rows: RotationRow[];
}

export interface MemberChange {
  symbol: string;
  changePct: number | null;
}

export interface HiddenSignal {
  etf: string;
  description: string;
  etfChangePct: number | null;
  membersTracked: number;
  membersUp: number;
  membersDownOrFlat: number;
  isHiddenSignal: boolean;
  topMemberSymbol: string | null;
  topMemberChangePct: number | null;
  members: MemberChange[];
}

export interface HiddenSignalResponse {
  asOfDate: string | null;
  signals: HiddenSignal[];
}
