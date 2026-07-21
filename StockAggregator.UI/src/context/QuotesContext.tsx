import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import type { WeekQuotesResponse } from '../types';
import { fetchWeek } from '../api/client';

interface QuotesState {
  data: WeekQuotesResponse | null;
  loading: boolean;
  error: string | null;
  days: number;
  setDays: (days: number) => void;
  reload: () => void;
}

const QuotesContext = createContext<QuotesState | undefined>(undefined);

export function QuotesProvider({ children }: { children: ReactNode }) {
  const [data, setData] = useState<WeekQuotesResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [days, setDays] = useState(5);

  const load = useCallback((requestedDays: number) => {
    setLoading(true);
    setError(null);
    fetchWeek(requestedDays)
      .then(setData)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load(days);
  }, [days, load]);

  return (
    <QuotesContext.Provider
      value={{ data, loading, error, days, setDays, reload: () => load(days) }}
    >
      {children}
    </QuotesContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useQuotes(): QuotesState {
  const ctx = useContext(QuotesContext);
  if (!ctx) {
    throw new Error('useQuotes must be used within a QuotesProvider');
  }
  return ctx;
}
