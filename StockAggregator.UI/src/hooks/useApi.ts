import { useEffect, useState } from 'react';

interface ApiState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
}

/// Fetches once on mount. `fn` is expected to be a stable module-level function.
export function useApi<T>(fn: () => Promise<T>): ApiState<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    setLoading(true);
    setError(null);
    fn()
      .then((d) => { if (live) setData(d); })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [fn]);

  return { data, loading, error };
}
