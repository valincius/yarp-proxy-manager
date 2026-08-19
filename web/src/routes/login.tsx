import { Title } from '@solidjs/meta';
import { useNavigate } from '@solidjs/router';
import { createSignal, Show } from 'solid-js';
import { api, ApiError } from '../lib/api';
import { useAuth } from '../lib/auth';

export default function Login() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [error, setError] = createSignal<string | null>(null);
  const [busy, setBusy] = createSignal(false);
  const [oidcEnabled, setOidcEnabled] = createSignal(false);
  void api.get<{ enabled: boolean }>('/auth/external-enabled').then((r) => setOidcEnabled(r.enabled));

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await auth.login(email(), password());
      navigate('/admin', { replace: true });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Login failed.');
      setBusy(false);
    }
  }

  return (
    <main class="flex min-h-screen items-center justify-center bg-slate-100">
      <Title>Login - YARP Proxy Manager</Title>
      <form class="w-full max-w-sm rounded-lg border border-slate-200 bg-white p-8 shadow-sm" onSubmit={submit}>
        <h1 class="mb-6 text-xl font-semibold text-slate-800">YARP Proxy Manager</h1>
        <Show when={error()}>
          <div class="mb-4 rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error()}</div>
        </Show>
        <div class="space-y-4">
          <label class="block">
            <span class="text-sm font-medium text-slate-700">Email</span>
            <input
              type="email"
              required
              class="mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
              value={email()}
              onInput={(e) => setEmail(e.currentTarget.value)}
              placeholder="admin@example.com"
            />
          </label>
          <label class="block">
            <span class="text-sm font-medium text-slate-700">Password</span>
            <input
              type="password"
              required
              class="mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
              value={password()}
              onInput={(e) => setPassword(e.currentTarget.value)}
            />
          </label>
        </div>
        <button
          type="submit"
          disabled={busy()}
          class="mt-6 w-full rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
        >
          {busy() ? 'Signing in…' : 'Sign in'}
        </button>
        <Show when={oidcEnabled()}>
          <a
            href="/api/v1/auth/external-login"
            class="mt-3 block w-full rounded-md border border-slate-300 px-4 py-2 text-center text-sm font-semibold text-slate-700 hover:bg-slate-50"
          >
            Sign in with SSO
          </a>
        </Show>
      </form>
    </main>
  );
}
