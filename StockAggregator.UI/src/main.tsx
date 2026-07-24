import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { createSyncStoragePersister } from '@tanstack/query-sync-storage-persister';
import App from './App';
import './bootstrap-offcanvas.scss';
import './index.css';

// Apply the saved theme (default dark) before first paint to avoid a flash.
document.documentElement.dataset.theme = localStorage.getItem('theme') ?? 'dark';

// Cache analytics responses AND persist them to localStorage (free, no server):
// on load the last-known data shows instantly with a "last updated" time, then
// React Query refetches in the background and swaps in fresh data when it returns.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 5 * 60 * 1000, gcTime: 24 * 60 * 60 * 1000, refetchOnWindowFocus: false, retry: 1 },
  },
});
const persister = createSyncStoragePersister({ storage: window.localStorage, key: 'sa-rq-cache' });

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <PersistQueryClientProvider client={queryClient} persistOptions={{ persister, maxAge: 24 * 60 * 60 * 1000 }}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </PersistQueryClientProvider>
  </React.StrictMode>,
);
