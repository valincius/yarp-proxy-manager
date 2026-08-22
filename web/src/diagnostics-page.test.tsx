import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, cleanup, waitFor } from '@solidjs/testing-library';
import App from './App';

const SESSION = {
  authenticated: true,
  email: 'admin@example.com',
  displayName: 'Administrator',
  roles: ['Admin'],
};

const OVERVIEW = {
  startedAt: new Date().toISOString(),
  totalRequests: 0,
  totalFailed: 0,
  trackedHosts: 0,
  bufferedSamples: 0,
  captureEnabled: false,
  captureSize: 4096,
  traceEndpoint: null,
  routes: 0,
  clusters: 0,
  proxyHosts: 0,
  streams: [],
  certificates: { total: 0, failed: 0, expiringSoon: 0 },
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
    return jsonResponse({ error: `unmocked ${url}` }, 500);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

beforeEach(() => {
  window.history.replaceState(null, '', '/admin/diagnostics');
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('diagnostics page', () => {
  it('renders with empty data', async () => {
    const fetchMock = installFetchMock({
      'GET /api/v1/diagnostics/overview': () => jsonResponse(OVERVIEW),
      'GET /api/v1/diagnostics/traffic?window=5m': () => jsonResponse([]),
      'GET /api/v1/diagnostics/requests?limit=100': () => jsonResponse([]),
    });
    render(() => <App />);

    await waitFor(() => {
      expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/diagnostics/overview'))).toBe(true);
    });
    await screen.findByRole('heading', { name: 'Diagnostics' });
    expect(screen.getByText(/Live traffic statistics/)).toBeInTheDocument();
  });
});
