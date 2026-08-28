import { expect, test, type Page } from '@playwright/test';

const host = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Example app',
  domainNames: ['app.example.com'],
  enabled: true,
  scheme: 'https',
  forwardHost: '10.0.0.25',
  forwardPort: 8080,
  blockCommonExploits: true,
  forceHttps: false,
  certificateId: null,
  accessListId: null,
  requestHeaders: [],
  responseHeaders: [],
  locations: [],
  destinations: [],
  loadBalancingPolicy: null,
  healthCheckEnabled: false,
  healthCheckPath: null,
  healthCheckIntervalSeconds: 10,
};

async function mockApi(page: Page): Promise<void> {
  await page.route('**/api/v1/**', async (route) => {
    const path = new URL(route.request().url()).pathname.replace('/api/v1', '');
    const data: Record<string, unknown> = {
      '/auth/session': { authenticated: true, email: 'admin@example.com', displayName: 'Admin', roles: ['Admin'] },
      '/auth/antiforgery': { token: 'visual-test-token' },
      '/hosts': [host],
      '/health': { routes: 1, clusters: 1 },
      '/redirects': [],
      '/access-lists': [],
      '/streams': [],
      '/streams/status': {},
      '/certificates': { certificates: [], credentials: [] },
      '/users': [],
      '/diagnostics/overview': {
        uptimeSeconds: 600,
        totalRequests: 12,
        totalFailed: 1,
        trackedHosts: 1,
        routes: 1,
        clusters: 1,
        certificates: { total: 0, failed: 0, expiringSoon: 0 },
        traceEndpoint: null,
        streams: [],
        bufferedSamples: 0,
      },
      '/diagnostics/traffic': [],
      '/diagnostics/requests': [],
      '/acme-settings': {},
      '/dns-credentials': [],
      '/settings/not-found': {},
      '/settings/docker': {},
      '/api-keys': [],
    };

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(data[path] ?? {}),
    });
  });
}

test.describe('admin responsive visual coverage', () => {
  test.beforeEach(async ({ page }) => {
    await mockApi(page);
  });

  test('dashboard desktop', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
    await expect(page).toHaveScreenshot('dashboard-desktop.png', { fullPage: false });
  });

  test('dashboard mobile with collapsed navigation', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Open navigation' })).toBeVisible();
    await expect(page).toHaveScreenshot('dashboard-mobile.png', { fullPage: false });
  });

  test('mobile navigation opens without shifting the page off-screen', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/admin');
    await page.getByRole('button', { name: 'Open navigation' }).click();
    await expect(page.getByRole('button', { name: 'Close navigation' })).toBeVisible();
    await expect(page.getByRole('navigation')).toBeVisible();
    await expect(page).toHaveScreenshot('dashboard-mobile-menu-open.png', { fullPage: false });
  });

  test('hosts use a mobile card layout', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/admin/hosts');
    await expect(page.getByRole('heading', { name: 'Proxy Hosts' })).toBeVisible();
    await expect(page.locator('table.responsive-card-table')).toBeVisible();
    await expect(page).toHaveScreenshot('hosts-mobile-card.png', { fullPage: false });
  });
});
