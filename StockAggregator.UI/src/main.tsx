import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import './bootstrap-offcanvas.scss';
import './index.css';

// Apply the saved theme (default dark) before first paint to avoid a flash.
document.documentElement.dataset.theme = localStorage.getItem('theme') ?? 'dark';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </React.StrictMode>,
);
