import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup } from '@solidjs/testing-library';
import App from './App';

const SESSION = {
  authenticated: true,
  email: 'admin@example.com',
  displayName: 'Administrator',
  roles: ['Admin'],
};

const USERS = [
  { id: '11111111-1111-1111-1111-111111111111', email: 'admin@example.com', displayName: 'Administrator', roles: ['Admin'], lockoutEnd: null },
  { id: '22222222-2222-2222-2222-222222222222', email: 'user@example.com', displayName: 'User', roles: ['User'], lockoutEnd: '2099-01-01T00:00:00Z' },
];

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
    if (url === '/api/v1/users') return jsonResponse(USERS);
    return jsonResponse({ error: `unmocked ${url}` }, 500);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

beforeEach(() => {
  window.history.replaceState(null, '', '/admin/users');
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('users page', () => {
  it('lists users with icon row actions', async () => {
    installFetchMock();
    render(() => <App />);

    await screen.findByRole('heading', { name: 'Users' });
    // The session email also appears in the sidebar footer, so match all.
    expect(screen.getAllByText('admin@example.com').length).toBeGreaterThan(0);
    expect(screen.getByText('user@example.com')).toBeInTheDocument();

    // Icon-only actions with tooltips: power toggle, reset password, delete.
    expect(screen.getByRole('button', { name: 'Disable' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enable' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Reset password' })).toHaveLength(2);
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(2);
  });

  it('creates a user through the modal with role descriptions', async () => {
    installFetchMock();
    render(() => <App />);

    await screen.findByRole('heading', { name: 'Users' });
    await fireEvent.click(screen.getByRole('button', { name: '+ Create user' }));

    // The modal dialog opens and contains the form + role legend.
    const dialog = await screen.findByRole('dialog', { name: 'Create a user' });
    expect(dialog).toBeInTheDocument();
    expect(screen.getByLabelText(/Email/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Password/)).toBeInTheDocument();
    expect(screen.getByText('Everything a User can do, plus:')).toBeInTheDocument();
    expect(screen.getByText(/Cannot:/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create user' })).toBeInTheDocument();
  });
});
