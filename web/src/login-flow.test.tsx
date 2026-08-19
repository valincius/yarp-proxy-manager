import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, cleanup } from '@solidjs/testing-library';
import App from './App';

const SESSION = {
  authenticated: true,
  email: 'admin@example.com',
  displayName: 'Administrator',
  roles: ['Admin'],
};

function jsonResponse(body: unknown, status = 200, statusText?: string): Response {
  return new Response(JSON.stringify(body), {
    status,
    // jsdom's Response does not auto-fill the default status text, and the api
    // client falls back to it when the body is not ProblemDetails.
    statusText:
      statusText ??
      (status === 401 ? 'Unauthorized' : status === 500 ? 'Internal Server Error' : 'OK'),
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
    if (url === '/api/v1/auth/session') return jsonResponse({ error: 'Unauthorized' }, 401);
    if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'xsrf-test' });
    if (url === '/api/v1/auth/login') return jsonResponse(SESSION);
    if (url === '/api/v1/auth/external-enabled') return jsonResponse({ enabled: false });
    if (url === '/api/v1/hosts') return jsonResponse([]);
    if (url === '/api/v1/health') return jsonResponse({ routes: 0, clusters: 0 });
    return jsonResponse({ error: `unmocked ${url}` }, 500);
  });
  vi.stubGlobal('fetch', fetchMock);
  return { calls, fetchMock };
}

beforeEach(() => {
  window.history.replaceState(null, '', '/login');
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('login flow', () => {
  it('navigates to /admin after a successful login and stays there', async () => {
    const { calls } = installFetchMock();
    render(() => <App />);

    const email = await screen.findByPlaceholderText('admin@example.com');
    const password = screen.getByLabelText('Password');
    await fireEvent.input(email, { target: { value: 'admin@example.com' } });
    await fireEvent.input(password, { target: { value: 'admin' } });

    await fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    // The dashboard must render and the URL must settle on /admin (no bounce
    // back to /login). Regression test for the admin layout redirect bug.
    await waitFor(() => {
      expect(window.location.pathname).toBe('/admin');
    });
    // "Dashboard" appears in both the sidebar nav link and the page heading.
    await screen.findByRole('heading', { name: 'Dashboard' });
    expect(screen.getByText('Recently Accessed')).toBeInTheDocument();

    // Give any stray redirect a chance to fire, then confirm we're still there.
    await new Promise((r) => setTimeout(r, 50));
    expect(window.location.pathname).toBe('/admin');

    expect(calls.some((c) => c.url === '/api/v1/auth/login')).toBe(true);
    expect(calls.some((c) => c.url === '/api/v1/hosts')).toBe(true);
    expect(calls.some((c) => c.url === '/api/v1/health')).toBe(true);
  });

  it('stays on /login with an error banner when credentials are wrong', async () => {
    installFetchMock({
      'POST /api/v1/auth/login': () => jsonResponse({ error: 'Invalid email or password.' }, 401),
    });
    render(() => <App />);

    const email = await screen.findByPlaceholderText('admin@example.com');
    const password = screen.getByLabelText('Password');
    await fireEvent.input(email, { target: { value: 'admin@example.com' } });
    await fireEvent.input(password, { target: { value: 'wrong' } });

    await fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    // The 401 body is a plain { error } object, not ProblemDetails, so the
    // api client falls back to the status text.
    await screen.findByText('Unauthorized');
    expect(window.location.pathname).toBe('/login');
  });

  it('redirects an unauthenticated visitor from /admin to /login', async () => {
    installFetchMock();
    window.history.replaceState(null, '', '/admin');
    render(() => <App />);

    await waitFor(() => {
      expect(window.location.pathname).toBe('/login');
    });
    await screen.findByPlaceholderText('admin@example.com');
  });
});
