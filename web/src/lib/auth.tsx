import { createContext, createEffect, createSignal, useContext, type ParentProps } from 'solid-js';
import { api, ApiError, fetchXsrfToken } from './api';
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

  createEffect(() => {
    void refreshSession();
  });

  async function refreshSession(): Promise<void> {
    try {
      setSession(await api.get<Session>('/auth/session'));
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setSession(null);
      } else {
        throw error;
      }
    } finally {
      // The antiforgery token is bound to the user identity, so it must be
      // re-fetched after any authentication-state change.
      await fetchXsrfToken();
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
