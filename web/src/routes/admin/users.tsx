import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import Modal from '../../components/Modal';
import { DeleteButton, KeyButton, PowerButton } from '../../components/ActionButtons';
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

/** What each role can do, mirroring the API's [Authorize] attributes. */
const ROLE_CAPABILITIES: Record<string, { summary: string; can: string[]; cannot: string[] }> = {
  Admin: {
    summary: 'Full access. Can do everything a User can, plus administration.',
    can: [
      'Manage users (create, enable/disable, reset passwords, delete)',
      'Create and delete API keys',
      'Backup and restore the configuration',
      'Change general settings (404 page, Docker integration)',
      'View captured request bodies in Diagnostics',
    ],
    cannot: [],
  },
  User: {
    summary: 'Manages the proxy itself — routing and certificates.',
    can: [
      'Manage proxy hosts, redirection hosts, access lists and streams',
      'Request, upload, renew and delete SSL certificates',
      'Manage DNS credentials and the ACME account',
      'View the audit log and traffic statistics in Diagnostics',
    ],
    cannot: ['Manage users, API keys, backups or general settings'],
  },
};

export default function Users() {
  const users = createMemo(() => loadUsers());
  const [showCreate, setShowCreate] = createSignal(false);

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
      <div class="page-header mb-6 flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">Users</h1>
        <button
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          onClick={() => setShowCreate(true)}
        >
          + Create user
        </button>
      </div>

      <Modal open={showCreate()} title="Create a user" size="max-w-xl" onClose={() => setShowCreate(false)}>
        <CreateUserForm onDone={() => setShowCreate(false)} />
      </Modal>

      <table class="responsive-card-table w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
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
                <td data-label="Email" class="px-4 py-3 font-medium text-slate-800">{user.email}</td>
                <td data-label="Roles" class="px-4 py-3 text-slate-600">{user.roles.join(', ')}</td>
                <td data-label="Status" class="px-4 py-3">
                  <span
                    class={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      user.lockoutEnd === null ? 'bg-green-100 text-green-700' : 'bg-slate-200 text-slate-600'
                    }`}
                  >
                    {user.lockoutEnd === null ? 'Active' : 'Disabled'}
                  </span>
                </td>
                <td data-label="Actions" class="px-4 py-3">
                  <div class="flex items-center justify-end gap-1">
                    <PowerButton
                      enabled={user.lockoutEnd === null}
                      onClick={() => void toggle(user)}
                    />
                    <KeyButton onClick={() => void resetPassword(user)} />
                    <DeleteButton onClick={() => void remove(user)} />
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

function CreateUserForm(props: { onDone: () => void }) {
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
      revalidate('users');
      props.onDone();
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form class="space-y-4" onSubmit={create}>
      <Show when={error()}>
        <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          <ul class="list-disc space-y-1 pl-5">
            <For each={error()!}>{(message) => <li>{message}</li>}</For>
          </ul>
        </div>
      </Show>
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Email</span>
          <input type="email" class={inputClass} value={email()} onInput={(e) => setEmail(e.currentTarget.value)} required autofocus />
          <span class="mt-1 block text-xs text-slate-500">The login address for this user.</span>
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Password</span>
          <input type="password" class={inputClass} value={password()} onInput={(e) => setPassword(e.currentTarget.value)} required />
          <span class="mt-1 block text-xs text-slate-500">At least 5 characters.</span>
        </label>
      </div>
      <div>
        <span class="text-sm font-medium text-slate-700">Role</span>
        <select class={inputClass} value={role()} onChange={(e) => setRole(e.currentTarget.value)}>
          <option value="User">User</option>
          <option value="Admin">Admin</option>
        </select>
        <p class="mt-1 text-xs text-slate-500">{ROLE_CAPABILITIES[role()].summary}</p>
      </div>

      <div class="grid grid-cols-1 gap-3 rounded-md border border-slate-200 bg-slate-50 p-4 sm:grid-cols-2">
        <div>
          <p class="text-xs font-semibold uppercase tracking-wide text-slate-600">Admin</p>
          <p class="mt-1 text-xs text-slate-600">Everything a User can do, plus:</p>
          <ul class="mt-1 list-disc space-y-1 pl-4 text-xs text-slate-600">
            <For each={ROLE_CAPABILITIES.Admin.can}>{(item) => <li>{item}</li>}</For>
          </ul>
        </div>
        <div>
          <p class="text-xs font-semibold uppercase tracking-wide text-slate-600">User</p>
          <ul class="mt-1 list-disc space-y-1 pl-4 text-xs text-slate-600">
            <For each={ROLE_CAPABILITIES.User.can}>{(item) => <li>{item}</li>}</For>
          </ul>
          <p class="mt-2 text-xs text-slate-500">
            <span class="font-medium text-slate-600">Cannot:</span> {ROLE_CAPABILITIES.User.cannot.join(', ')}.
          </p>
        </div>
      </div>

      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Creating…' : 'Create user'}
        </button>
        <button type="button" class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={props.onDone}>
          Cancel
        </button>
      </div>
    </form>
  );
}
