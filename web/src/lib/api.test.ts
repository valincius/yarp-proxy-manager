import { describe, expect, it, vi } from 'vitest';
import { api, ApiError, setXsrfToken } from './api';

function jsonResponse(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('api client', () => {
  it('throws ApiError with parsed errors for ProblemDetails responses', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ title: 'Validation failed', errors: ['One or more domains are invalid.'] }, 400),
      ),
    );

    const error = await api.get('/hosts').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      status: 400,
      message: 'Validation failed',
      errors: ['One or more domains are invalid.'],
    });
  });

  it('sends the XSRF and JSON headers on mutations', async () => {
    setXsrfToken('token-123');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ id: 'abc' }, 201)));

    await api.post('/hosts', { name: 'Test' });

    const fetchMock = vi.mocked(fetch);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/v1/hosts');
    const headers = init?.headers as Record<string, string>;
    expect(headers['X-XSRF-TOKEN']).toBe('token-123');
    expect(headers['Content-Type']).toBe('application/json');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toEqual({ name: 'Test' });
  });

  it('returns undefined for 204 responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    const result = await api.del('/hosts/abc');
    expect(result).toBeUndefined();
  });

  it('returns undefined for 200 responses with an empty body (e.g. backup/restore)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 200 })));

    const result = await api.post('/backup/restore', { hosts: [], redirects: [], streams: [], accessLists: [] });
    expect(result).toBeUndefined();
  });
});
