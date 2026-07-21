import { QuotesProvider } from './context/QuotesContext';
import QuotesPage from './pages/QuotesPage';

export default function App() {
  return (
    <QuotesProvider>
      <div className="app">
        <header className="app-header">
          <h1>StockAggregator</h1>
        </header>
        <main className="app-main">
          <QuotesPage />
        </main>
      </div>
    </QuotesProvider>
  );
}
