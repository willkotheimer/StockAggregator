import type {
  EtfGroup,
  HiddenSignalResponse,
  ReboundResponse,
  RotationResponse,
  WeekQuotesResponse,
} from '../types';

// In dev the API runs separately on :5080; in a production build the API serves
// this app, so call it same-origin (relative). Override with VITE_API_BASE_URL.
const baseUrl =
  import.meta.env.VITE_API_BASE_URL ?? (import.meta.env.DEV ? 'http://localhost:5080' : '');
const apiKey = import.meta.env.VITE_API_KEY ?? '';

async function getJson<T>(path: string): Promise<T> {
  const headers: Record<string, string> = {};
  if (apiKey) {
    headers['X-Api-Key'] = apiKey;
  }

  const res = await fetch(`${baseUrl}${path}`, { headers });
  if (!res.ok) {
    throw new Error(`API ${res.status}: ${await res.text()}`);
  }

  return (await res.json()) as T;
}

export const fetchWeek = (days = 5) =>
  getJson<WeekQuotesResponse>(`/api/quotes/week?days=${days}`);

export const fetchAvailableDates = () =>
  getJson<string[]>('/api/quotes/available-dates');

export const fetchDays = (dates: string[]) =>
  getJson<WeekQuotesResponse>(`/api/quotes/days?dates=${dates.join(',')}`);

export const fetchRotations = () =>
  getJson<RotationResponse>('/api/analytics/rotations');

export const fetchHiddenSignal = () =>
  getJson<HiddenSignalResponse>('/api/analytics/hidden-signal');

export const fetchEtfGroups = () =>
  getJson<EtfGroup[]>('/api/analytics/etf-groups');

export const fetchRebound = (symbol: string, mode: 'trough' | 'surge', threshold = 10) =>
  getJson<ReboundResponse>(
    `/api/analytics/rebound/${encodeURIComponent(symbol)}?mode=${mode}&threshold=${threshold}`,
  );
