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

export interface EtfGroup {
  etf: string;
  description: string;
  members: string[];
}

export interface ReboundEpisode {
  anchorDate: string;
  anchorPrice: number;
  extremeDate: string;
  extremePrice: number;
  movePct: number;
  anchorToExtremeDays: number;
  extremeToReversalDays: number | null;
  anchorToReversalDays: number | null;
}

export interface ReboundCurrent {
  anchorDate: string;
  anchorPrice: number;
  extremeDate: string;
  extremePrice: number;
  maxMovePct: number;
  currentMovePct: number;
  daysSinceAnchor: number;
  daysSinceExtreme: number;
}

export interface ReboundBaseRate {
  comparableMovePct: number;
  episodeCount: number;
  medianReversalDays: number;
  minReversalDays: number;
  maxReversalDays: number;
  shortWindowDays: number;
  reversedWithinShort: number;
  longWindowDays: number;
  reversedWithinLong: number;
}

export interface ReboundResponse {
  symbol: string;
  mode: 'trough' | 'surge';
  thresholdPct: number;
  historyStart: string | null;
  asOfDate: string | null;
  lastClose: number | null;
  barCount: number;
  current: ReboundCurrent | null;
  baseRate: ReboundBaseRate | null;
  episodes: ReboundEpisode[];
}
