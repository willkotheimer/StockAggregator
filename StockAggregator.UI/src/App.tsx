import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import { QuotesProvider } from './context/QuotesContext';
import QuotesPage from './pages/QuotesPage';
import CorrelationsPage from './pages/CorrelationsPage';
import AnalyticsPage from './pages/AnalyticsPage';

const tabClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'tab active' : 'tab');

export default function App() {
  return (
    <QuotesProvider>
      <div className="app">
        <header className="app-header">
          <h1>StockAggregator</h1>
          <nav className="tabs">
            <NavLink to="/quotes" className={tabClass}>Quotes</NavLink>
            <NavLink to="/correlations" className={tabClass}>Correlations</NavLink>
            <NavLink to="/analytics" className={tabClass}>Analytics</NavLink>
          </nav>
        </header>
        <main className="app-main">
          <Routes>
            <Route path="/" element={<Navigate to="/quotes" replace />} />
            <Route path="/quotes" element={<QuotesPage />} />
            <Route path="/correlations" element={<CorrelationsPage />} />
            <Route path="/analytics" element={<AnalyticsPage />} />
          </Routes>
        </main>
      </div>
    </QuotesProvider>
  );
}
