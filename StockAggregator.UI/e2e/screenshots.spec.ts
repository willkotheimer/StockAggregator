import { test, expect, type Page } from '@playwright/test';

// Screenshots land in the repo-root docs/screenshots folder (relative to the UI
// dir this suite runs from). Comment out the `shot(...)` calls once captured;
// the assertions around them stay as an e2e smoke suite for the dashboard.
const SHOT_DIR = '../docs/screenshots';

// Screenshots for the README have been captured, so capture is off and this file
// now runs purely as an e2e smoke suite. Flip back to true to regenerate the
// images in docs/screenshots (start from a clean build so routes are current).
const CAPTURE = false;

async function shot(page: Page, name: string, fullPage = true) {
  if (!CAPTURE) return;
  await page.screenshot({ path: `${SHOT_DIR}/${name}.png`, fullPage });
}

// Wait for the page to settle. We can't use networkidle: Vite's HMR socket and
// react-query's background refetches keep the network live. Instead wait for the
// persistent overview hero (renders only once every dashboard query resolves),
// then a short beat for chart/table paint.
async function settle(page: Page) {
  await page.locator('.overview-hero').first().waitFor({ state: 'visible', timeout: 30_000 }).catch(() => {});
  await page.waitForTimeout(800);
}

test.describe('dashboard screenshots + smoke', () => {
  test('quotes — default (no stocks charted)', async ({ page }) => {
    await page.goto('/quotes');
    await settle(page);
    // Empty-state placeholder in the plot area.
    await expect(page.locator('.chart-empty')).toBeVisible();
    await expect(page.locator('.drawer-open')).toContainText('Browse stocks');
    await shot(page, 'quotes-empty', false);
  });

  test('quotes — browse panel open', async ({ page }) => {
    await page.goto('/quotes');
    await settle(page);
    await page.locator('.drawer-open').click();
    await expect(page.locator('.browse-panel.show')).toBeVisible();
    await expect(page.locator('.quotes-table').first()).toBeVisible();
    await page.waitForTimeout(400); // offcanvas slide-in
    await shot(page, 'quotes-panel-open', false);
  });

  test('quotes — chart populated', async ({ page }) => {
    await page.goto('/quotes');
    await settle(page);
    await page.locator('.drawer-open').click();
    await expect(page.locator('.quotes-table').first()).toBeVisible();
    // Add the first ETF (+ its members) to the chart via the "+" button.
    await page.locator('.chart-add').first().click();
    // Close the panel so the populated chart is unobscured.
    await page.locator('.drawer-close').click();
    await expect(page.locator('.browse-panel.show')).toHaveCount(0);
    // Lines drawn + legend populated.
    await expect(page.locator('.chart-legend .cl-sym').first()).toBeVisible();
    await expect(page.locator('.recharts-line').first()).toBeVisible();
    await page.waitForTimeout(500);
    await shot(page, 'quotes-populated', false);
  });

  test('rotations', async ({ page }) => {
    await page.goto('/rotations');
    await settle(page);
    await expect(page.getByRole('heading', { name: /Rotations/i }).first()).toBeVisible();
    await shot(page, 'rotations');
  });

  test('signals', async ({ page }) => {
    await page.goto('/signals');
    await settle(page);
    await expect(page.getByRole('heading', { name: /Hidden Signals/i }).first()).toBeVisible();
    await shot(page, 'signals');
  });

  test('rebound', async ({ page }) => {
    await page.goto('/rebound');
    await settle(page);
    await expect(page.getByRole('heading', { name: /Rebound base rates/i }).first()).toBeVisible();
    await shot(page, 'rebound');
  });

  test('ranges', async ({ page }) => {
    await page.goto('/ranges');
    await settle(page);
    await expect(page.getByRole('heading', { name: /Ranges/i }).first()).toBeVisible();
    await shot(page, 'ranges');
  });

  test('crawlers', async ({ page }) => {
    test.setTimeout(300_000);
    await page.goto('/crawlers');
    await settle(page);
    await expect(page.getByRole('heading', { name: /Steady Crawlers/i }).first()).toBeVisible();
    // The crawler screen recomputes a trend fit per symbol with no caching (~150s),
    // so the table lands well after the heading — wait generously for the rows.
    await expect(page.locator('.crawler-table tbody tr').first()).toBeVisible({ timeout: 240_000 });
    await page.waitForTimeout(400);
    await shot(page, 'crawlers');
  });
});
