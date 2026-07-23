import { useEffect, useState } from 'react';
import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import QuotesPage from './pages/QuotesPage';
import RotationsPage from './pages/RotationsPage';
import SignalsPage from './pages/SignalsPage';
import ReboundPage from './pages/ReboundPage';
import RangesPage from './pages/RangesPage';
import CrawlersPage from './pages/CrawlersPage';

const tabClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'tab active' : 'tab');

type Theme = 'light' | 'dark';

export default function App() {
  const [theme, setTheme] = useState<Theme>(
    () => (localStorage.getItem('theme') as Theme) || 'dark',
  );

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('theme', theme);
  }, [theme]);

  return (
    <div className="app">
      <header className="app-header">
        <h1>StockAggregator</h1>
        <nav className="tabs">
          <NavLink to="/quotes" className={tabClass}>Quotes</NavLink>
          <NavLink to="/rotations" className={tabClass}>Rotations</NavLink>
          <NavLink to="/signals" className={tabClass}>Signals</NavLink>
          <NavLink to="/rebound" className={tabClass}>Rebound</NavLink>
          <NavLink to="/ranges" className={tabClass}>Ranges</NavLink>
          <NavLink to="/crawlers" className={tabClass}>Crawlers</NavLink>
        </nav>
        <button
          type="button"
          className="theme-toggle"
          onClick={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))}
          title={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
          aria-label="Toggle theme"
        >
          {theme === 'dark' ? '☀' : '☾'}
        </button>
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<Navigate to="/quotes" replace />} />
          <Route path="/quotes" element={<QuotesPage />} />
          <Route path="/rotations" element={<RotationsPage />} />
          <Route path="/signals" element={<SignalsPage />} />
          <Route path="/rebound" element={<ReboundPage />} />
          <Route path="/ranges" element={<RangesPage />} />
          <Route path="/crawlers" element={<CrawlersPage />} />
        </Routes>
      </main>
    </div>
  );
}
