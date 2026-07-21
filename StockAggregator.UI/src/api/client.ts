import type { WeekQuotesResponse } from '../types';

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080';
const apiKey = import.meta.env.VITE_API_KEY ?? '';

export async function fetchWeek(days = 5): Promise<WeekQuotesResponse> {
  const headers: Record<string, string> = {};
  if (apiKey) {
    headers['X-Api-Key'] = apiKey;
  }

  const res = await fetch(`${baseUrl}/api/quotes/week?days=${days}`, { headers });
  if (!res.ok) {
    throw new Error(`API ${res.status}: ${await res.text()}`);
  }

  return (await res.json()) as WeekQuotesResponse;
}
