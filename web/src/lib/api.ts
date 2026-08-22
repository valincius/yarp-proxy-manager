const BASE = '/api/v1';

export class ApiError extends Error {
  readonly status: number;
  readonly errors?: string[];

  constructor(status: number, title: string, errors?: string[]) {
    super(title);
    this.name = 'ApiError';
    this.status = status;
    this.errors = errors;
  }
}

let xsrfToken = '';

/** Sets the antiforgery token echoed on mutating requests (identity-bound, so refresh after login). */
export function setXsrfToken(token: string): void {
  xsrfToken = token;
}

function buildHeaders(init?: RequestInit): HeadersInit {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (xsrfToken) {
    headers['X-XSRF-TOKEN'] = xsrfToken;
  }
  if (init?.headers) {
    Object.assign(headers, init.headers);
  }
  return headers;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(BASE + path, {
    ...init,
    credentials: 'same-origin',
    headers: buildHeaders(init),
  });

  if (response.status === 204) {
    return undefined as T;
  }

  if (!response.ok) {
    let title = response.statusText;
    let errors: string[] | undefined;
    try {
      const problem: { title?: string; errors?: unknown } = await response.json();
      title = problem.title ?? title;
      if (problem.errors) {
        errors = Array.isArray(problem.errors)
          ? (problem.errors as string[])
          : Object.values(problem.errors as Record<string, string[]>).flat();
      }
    } catch {
      // Not a ProblemDetails body — keep the status text.
    }
    throw new ApiError(response.status, title, errors);
  }

  // Some endpoints (e.g. backup/restore) return 200 with an empty body — treat
  // those like 204 instead of failing on JSON.parse of an empty string.
  const text = await response.text();
  if (text.length === 0) {
    return undefined as T;
  }
  return JSON.parse(text) as T;
}

export const api = {
  get: <T>(path: string): Promise<T> => request<T>(path),
  post: <T>(path: string, body?: unknown): Promise<T> =>
    request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body?: unknown): Promise<T> =>
    request<T>(path, { method: 'PUT', body: JSON.stringify(body) }),
  patch: <T>(path: string, body?: unknown): Promise<T> =>
    request<T>(path, { method: 'PATCH', body: JSON.stringify(body) }),
  del: <T = void>(path: string): Promise<T> => request<T>(path, { method: 'DELETE' }),
};

/** Fetches a fresh antiforgery token and stores it for subsequent requests. */
export async function fetchXsrfToken(): Promise<void> {
  const data = await api.get<{ token: string }>('/auth/antiforgery');
  setXsrfToken(data.token);
}
