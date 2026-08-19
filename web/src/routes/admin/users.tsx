import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import type { UserDto } from '../../lib/types';

const loadUsers = query(async (): Promise<UserDto[]> => api.get('/users'), 'users');

const inputClass =
  'mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500';

function toMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors : [error.message];
  }
  return [error instanceof Error ? error.message : 'An unexpected error occurred.'];
}

export default function Users() {
  const users = createMemo(() => loadUsers());
  const [email, setEmail] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [role, setRole] = createSignal('User');
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function create(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.post('/users', { email: email(), password: password(), role: role() });
      setEmail('');
      setPassword('');
      revalidate('users');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  async function remove(user: UserDto) {
    if (!confirm(`Delete user "${user.email}"?`)) {
      return;
    }
    await api.del(`/users/${user.id}`);
    revalidate('users');
  }

  async function toggle(user: UserDto) {
    await api.patch(`/users/${user.id}/enable`, { enabled: user.lockoutEnd === null });
    revalidate('users');
  }

  async function resetPassword(user: UserDto) {
    const newPassword = prompt(`New password for ${user.email}:`);
    if (!newPassword) {
      return;
    }
    await api.post(`/users/${user.id}/reset-password`, { password: newPassword });
    revalidate('users');
  }

  return (
    <section>
      <Title>Users - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">Users</h1>

      <form class="mb-8 grid max-w-2xl grid-cols-1 gap-4 rounded-lg border border-slate-200 bg-white p-6 shadow-sm sm:grid-cols-3" onSubmit={create}>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Email</span>
          <input type="email" class={inputClass} value={email()} onInput={(e) => setEmail(e.currentTarget.value)} required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Password</span>
          <input type="password" class={inputClass} value={password()} onInput={(e) => setPassword(e.currentTarget.value)} required />
        </label>
        <div>
          <span class="text-sm font-medium text-slate-700">Role</span>
          <select class={inputClass} value={role()} onChange={(e) => setRole(e.currentTarget.value)}>
            <option value="User">User</option>
            <option value="Admin">Admin</option>
          </select>
        </div>
        <div class="sm:col-span-3">
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Creating…' : 'Create user'}
          </button>
          <Show when={error()}>
            <span class="ml-3 text-sm text-red-600">{error()!.join(' ')}</span>
          </Show>
        </div>
      </form>

      <table class="w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
        <thead>
          <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
            <th class="px-4 py-3">Email</th>
            <th class="px-4 py-3">Roles</th>
            <th class="px-4 py-3">Status</th>
            <th class="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <For each={users()}>
            {(user) => (
              <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                <td class="px-4 py-3 font-medium text-slate-800">{user.email}</td>
                <td class="px-4 py-3 text-slate-600">{user.roles.join(', ')}</td>
                <td class="px-4 py-3">
                  <span
                    class={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      user.lockoutEnd === null ? 'bg-green-100 text-green-700' : 'bg-slate-200 text-slate-600'
                    }`}
                  >
                    {user.lockoutEnd === null ? 'Active' : 'Disabled'}
                  </span>
                </td>
                <td class="px-4 py-3">
                  <div class="flex items-center justify-end gap-2">
                    <button class="text-xs font-medium text-slate-600 hover:text-slate-900" onClick={() => void toggle(user)}>
                      {user.lockoutEnd === null ? 'Disable' : 'Enable'}
                    </button>
                    <button class="text-xs font-medium text-blue-600 hover:text-blue-700" onClick={() => void resetPassword(user)}>
                      Reset password
                    </button>
                    <button class="text-xs font-medium text-red-600 hover:text-red-700" onClick={() => void remove(user)}>
                      Delete
                    </button>
                  </div>
                </td>
              </tr>
            )}
          </For>
        </tbody>
      </table>
    </section>
  );
}
