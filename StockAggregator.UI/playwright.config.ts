import { defineConfig, devices } from '@playwright/test';

// Drives the built UI against a locally-running API (which talks to Azure SQL via
// your `az login`). Doubles as: (a) a screenshot generator for the README, and
// (b) a smoke/e2e suite for the dashboard pages. The screenshot calls in the spec
// are commented out once the images are captured; the assertions remain as tests.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 20_000 },
  reporter: 'list',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://localhost:5173',
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 2, // crisp screenshots on the README
  },
  // Start both the API (:5080) and the Vite UI (:5173). Reuses them if already up.
  webServer: [
    {
      command: 'dotnet run --project ../StockAggregatorApp --urls http://localhost:5080',
      url: 'http://localhost:5080/api/health',
      timeout: 180_000,
      reuseExistingServer: true,
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      timeout: 120_000,
      reuseExistingServer: true,
    },
  ],
});
