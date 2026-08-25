import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, cleanup, waitFor } from '@solidjs/testing-library';
import App from './App';

const SESSION = {
  authenticated: true,
  email: 'admin@example.com',
  displayName: 'Administrator',
  roles: ['Admin'],
};

const ACME = { email: 'admin@example.com', directoryUrl: '', staging: false };
const NOT_FOUND = { mode: 'Default', template: null };
const DOCKER = {
  enabled: false,
  host: null,
  network: null,
  lastSyncAt: null,
  lastError: null,
  managedHosts: 0,
  discoveredContainers: 0,
};

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function installFetchMock(overrides: Record<string, Response | (() => Response)> = {}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const key = `${init?.method ?? 'GET'} ${url}`;
    const hit = overrides[key] ?? overrides[url];
    if (hit) return typeof hit === 'function' ? hit() : hit;
    if (url === '/api/v1/auth/session') return jsonResponse(SESSION);
    if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'xsrf-test' });
    if (url === '/api/v1/auth/external-enabled') return jsonResponse({ enabled: false });
    if (url === '/api/v1/hosts') return jsonResponse([]);
    if (url === '/api/v1/health') return jsonResponse({ routes: 0, clusters: 0 });
    if (url === '/api/v1/acme-settings') return jsonResponse(ACME);
    if (url === '/api/v1/dns-credentials') return jsonResponse([]);
    if (url === '/api/v1/settings/not-found') return jsonResponse(NOT_FOUND);
    if (url === '/api/v1/settings/docker') return jsonResponse(DOCKER);
    if (url === '/api/v1/api-keys') return jsonResponse([]);
    return jsonResponse({ error: `unmocked ${url}` }, 500);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

beforeEach(() => {
  window.history.replaceState(null, '', '/admin/settings');
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('settings hub', () => {
  it('renders all sections as one page with a section nav and fetches the moved-in data', async () => {
    const fetchMock = installFetchMock();
    render(() => <App />);

    // The hub sub-nav must list every section.
    await screen.findByRole('heading', { name: 'Settings' });
    for (const label of ['Certificates', 'DNS credentials', '404 page', 'Docker', 'Backup & Restore', 'API Keys']) {
      expect(screen.getByRole('link', { name: label })).toBeInTheDocument();
    }

    // Every section heading appears on the single page.
    await screen.findByRole('heading', { name: 'Certificates — ACME account' });
    expect(screen.getByRole('heading', { name: 'DNS credentials (DNS-01)' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '404 page' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Docker integration' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Backup & Restore' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'API Keys' })).toBeInTheDocument();

    // The moved-in sections load their data in the same render.
    await waitFor(() => {
      expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/dns-credentials'))).toBe(true);
    });
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/api-keys'))).toBe(true);
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/settings/docker'))).toBe(true);
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/acme-settings'))).toBe(true);
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/settings/not-found'))).toBe(true);
  });
});
