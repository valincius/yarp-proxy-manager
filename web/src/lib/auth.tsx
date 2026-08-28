import { createContext, createSignal, useContext, type ParentProps } from 'solid-js';
import { api, ApiError, fetchXsrfToken, setXsrfToken } from './api';
import type { Session } from './types';

interface AuthValue {
  session: () => Session | null;
  ready: () => boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthValue>();

export function AuthProvider(props: ParentProps) {
  const [session, setSession] = createSignal<Session | null>(null);
  const [ready, setReady] = createSignal(false);

  // One-shot side effect: fetch the session on mount. Reads no signals, so
  // under Solid 2's compute/effect split it must be called directly rather
  // than wrapped in createEffect.
  void refreshSession();

  async function refreshSession(): Promise<void> {
    try {
      setSession(await api.get<Session>('/auth/session'));
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setSession(null);
      } else {
        // A temporarily unavailable API should not leave the entire app on a
        // permanent loading screen. The login page remains available so the
        // user can retry once the backend is reachable again.
        setSession(null);
      }
    } finally {
      // The antiforgery token is bound to the user identity, so it must be
      // re-fetched after any authentication-state change.
      try {
        await fetchXsrfToken();
      } catch {
        // The next mutating request will surface the API error normally.
        setXsrfToken('');
      }
      setReady(true);
    }
  }

  async function login(email: string, password: string): Promise<void> {
    setSession(await api.post<Session>('/auth/login', { email, password }));
    await fetchXsrfToken();
  }

  async function logout(): Promise<void> {
    await api.post('/auth/logout');
    setSession(null);
    await fetchXsrfToken();
  }

  const value: AuthValue = { session, ready, login, logout };
  // In Solid 2 the Context object is itself the provider component.
  return <AuthContext value={value}>{props.children}</AuthContext>;
}

export function useAuth(): AuthValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
