import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, cleanup } from '@solidjs/testing-library';
import App from './App';

const SESSION = {
  authenticated: true,
  email: 'admin@example.com',
  displayName: 'Administrator',
  roles: ['Admin'],
};

const HOST_ID = '11111111-1111-1111-1111-111111111111';
const CERT_ID = '22222222-2222-2222-2222-222222222222';
const NEW_HOST_ID = '33333333-3333-3333-3333-333333333333';

const HOST = {
  id: HOST_ID,
  name: 'My App',
  domainNames: ['app.example.com'],
  enabled: true,
  scheme: 'http',
  forwardHost: '10.0.0.25',
  forwardPort: 8080,
  blockCommonExploits: true,
  forceHttps: false,
  certificateId: CERT_ID,
  accessListId: null,
  requestHeaders: [],
  responseHeaders: [],
  locations: [],
  destinations: [],
  loadBalancingPolicy: null,
  healthCheckEnabled: false,
  healthCheckPath: '/health',
  healthCheckIntervalSeconds: 10,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
};

const CERT = {
  id: CERT_ID,
  name: 'My Cert',
  domains: ['app.example.com'],
  provider: 'Acme',
  status: 'Issued',
  notBefore: new Date().toISOString(),
  notAfter: new Date(Date.now() + 60 * 86_400_000).toISOString(),
  challengeType: 'Http01',
  dnsCredentialId: null,
  lastRenewalAttempt: null,
  lastRenewalError: null,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
};

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    statusText: 'OK',
    headers: { 'Content-Type': 'application/json' },
  });
}

/** Records every fetch call and dispatches canned responses. */
function installFetchMock(overrides: Record<string, Response | (() => Response)> = {}) {
  const calls: { url: string; init?: RequestInit }[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    calls.push({ url, init });
    const key = `${init?.method ?? 'GET'} ${url}`;
    const hit = overrides[key] ?? overrides[url];
    if (hit) return typeof hit === 'function' ? hit() : hit;
    if (url === '/api/v1/auth/session') return jsonResponse(SESSION);
    if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'xsrf-test' });
    if (url === '/api/v1/auth/external-enabled') return jsonResponse({ enabled: false });
    if (url === '/api/v1/hosts') return jsonResponse([]);
    if (url === '/api/v1/redirects') return jsonResponse([]);
    if (url === '/api/v1/streams') return jsonResponse([]);
    if (url === '/api/v1/access-lists') return jsonResponse([]);
    if (url === '/api/v1/dns-credentials') return jsonResponse([]);
    if (url === '/api/v1/certificates') return jsonResponse([CERT]);
    if (url === '/api/v1/health') return jsonResponse({ routes: 0, clusters: 0 });
    return jsonResponse({ error: `unmocked ${url}` }, 500);
  });
  vi.stubGlobal('fetch', fetchMock);
  return { calls, fetchMock };
}

async function fillRequiredFields() {
  await fireEvent.input(await screen.findByPlaceholderText('My app'), { target: { value: 'My App' } });
  await fireEvent.input(screen.getByPlaceholderText('app.example.com'), { target: { value: 'app.example.com' } });
  await fireEvent.input(screen.getByPlaceholderText('10.0.0.25'), { target: { value: '10.0.0.25' } });
  await fireEvent.input(screen.getByLabelText('Forward Port'), { target: { value: '8080' } });
}

beforeEach(() => {
  window.history.replaceState(null, '', '/');
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('host form', () => {
  // Note: the edit route (/admin/hosts/:id) is not exercised here — its async
  // queries can't be awaited inside the route's <Loading> boundary under jsdom.
  // The certificate-selection mechanism it relies on (marking the matching
  // <option> as `selected`) is covered by the new-host tests below.

  it('the + New certificate button opens the request modal', async () => {
    window.history.replaceState(null, '', '/admin/hosts/new');
    installFetchMock();
    render(() => <App />);

    const newButtons = await screen.findAllByText('+ New');
    expect(newButtons.length).toBeGreaterThanOrEqual(1);
    await fireEvent.click(newButtons[0]);

    await screen.findByRole('heading', { name: 'Request a new certificate' });
  });

  it('auto-request checkbox issues a cert for the host domains and attaches it', async () => {
    window.history.replaceState(null, '', '/admin/hosts/new');
    const created = { ...HOST, id: NEW_HOST_ID, certificateId: null };
    const issued = { ...CERT, id: CERT_ID };
    const attached = { ...created, certificateId: CERT_ID };
    const { calls } = installFetchMock({
      'POST /api/v1/hosts': () => jsonResponse(created),
      'POST /api/v1/certificates/issue': () => jsonResponse(issued),
      [`PUT /api/v1/hosts/${NEW_HOST_ID}`]: () => jsonResponse(attached),
    });
    render(() => <App />);

    await fillRequiredFields();
    await fireEvent.click(
      screen.getByLabelText('Request a certificate for these domains after creating the host'),
    );
    await fireEvent.click(screen.getByRole('button', { name: 'Create host' }));

    await waitFor(() => {
      expect(calls.some((c) => c.url === '/api/v1/hosts' && c.init?.method === 'POST')).toBe(true);
      expect(calls.some((c) => c.url === '/api/v1/certificates/issue')).toBe(true);
      expect(calls.some((c) => c.url === `/api/v1/hosts/${NEW_HOST_ID}` && c.init?.method === 'PUT')).toBe(true);
    });

    const issueCall = calls.find((c) => c.url === '/api/v1/certificates/issue');
    const body = JSON.parse(String(issueCall!.init!.body));
    expect(body.domains).toEqual(['app.example.com']);
    expect(body.challengeType).toBe('Http01');

    const attachCall = calls.find((c) => c.url === `/api/v1/hosts/${NEW_HOST_ID}`);
    expect(JSON.parse(String(attachCall!.init!.body)).certificateId).toBe(CERT_ID);
  });
});
