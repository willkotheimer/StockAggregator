import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import QuotesPage from './pages/QuotesPage';
import RotationsPage from './pages/RotationsPage';
import SignalsPage from './pages/SignalsPage';
import ReboundPage from './pages/ReboundPage';

const tabClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'tab active' : 'tab');

export default function App() {
  return (
    <div className="app">
      <header className="app-header">
        <h1>StockAggregator</h1>
        <nav className="tabs">
          <NavLink to="/quotes" className={tabClass}>Quotes</NavLink>
          <NavLink to="/rotations" className={tabClass}>Rotations</NavLink>
          <NavLink to="/signals" className={tabClass}>Signals</NavLink>
          <NavLink to="/rebound" className={tabClass}>Rebound</NavLink>
        </nav>
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<Navigate to="/quotes" replace />} />
          <Route path="/quotes" element={<QuotesPage />} />
          <Route path="/rotations" element={<RotationsPage />} />
          <Route path="/signals" element={<SignalsPage />} />
          <Route path="/rebound" element={<ReboundPage />} />
        </Routes>
      </main>
    </div>
  );
}
