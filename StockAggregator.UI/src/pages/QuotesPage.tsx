import { useQuotes } from '../context/QuotesContext';
import QuotesTable from '../components/QuotesTable';

const DAY_OPTIONS = [1, 3, 5, 10];

export default function QuotesPage() {
  const { data, loading, error, days, setDays, reload } = useQuotes();

  const hasRows = data && data.rows.length > 0;

  return (
    <section className="page">
      <div className="toolbar">
        <label>
          Trading days:{' '}
          <select value={days} onChange={(e) => setDays(Number(e.target.value))}>
            {DAY_OPTIONS.map((d) => (
              <option key={d} value={d}>{d}</option>
            ))}
          </select>
        </label>
        <button onClick={reload} disabled={loading}>Refresh</button>
        <span className="legend">
          <span className="swatch swatch-etf" /> ETF
          <span className="swatch swatch-stock" /> Stock
        </span>
      </div>

      {loading && <p>Loading…</p>}
      {error && <p className="error">Error: {error}</p>}
      {!loading && !error && !hasRows && <p>No quotes captured yet for this range.</p>}
      {!loading && !error && hasRows && <QuotesTable data={data} />}
    </section>
  );
}
