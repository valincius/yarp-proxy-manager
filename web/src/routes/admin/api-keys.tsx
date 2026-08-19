import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import Modal from '../../components/Modal';
import { useToast } from '../../lib/toast';

interface ApiKeyDto {
  id: string;
  name: string;
  prefix: string;
  enabled: boolean;
  createdAt: string;
  lastUsedAt: string | null;
}

interface CreatedApiKeyDto {
  key: ApiKeyDto;
  plaintext: string;
}

const loadApiKeys = query(async (): Promise<ApiKeyDto[]> => api.get('/api-keys'), 'api-keys');

function toMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors : [error.message];
  }
  return [error instanceof Error ? error.message : 'An unexpected error occurred.'];
}

export default function ApiKeys() {
  const keys = createMemo(() => loadApiKeys());
  const toast = useToast();
  const [showCreate, setShowCreate] = createSignal(false);
  const [created, setCreated] = createSignal<CreatedApiKeyDto | null>(null);
  const [busyId, setBusyId] = createSignal<string | null>(null);

  async function remove(key: ApiKeyDto) {
    if (!confirm(`Delete API key "${key.name}"? Requests using it will stop working immediately.`)) {
      return;
    }
    setBusyId(key.id);
    try {
      await api.del(`/api-keys/${key.id}`);
      toast.push(`API key "${key.name}" deleted.`, 'success');
      revalidate('api-keys');
    } catch (e) {
      toast.push(toMessages(e).join(' '), 'error');
    } finally {
      setBusyId(null);
    }
  }

  async function copyPlaintext() {
    const plaintext = created()?.plaintext;
    if (!plaintext) return;
    await navigator.clipboard.writeText(plaintext);
    toast.push('Key copied to clipboard.', 'success');
  }

  return (
    <section>
      <Title>API Keys - YARP Proxy Manager</Title>
      <div class="mb-6 flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">API Keys</h1>
        <button
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          onClick={() => setShowCreate(true)}
        >
          + New API Key
        </button>
      </div>

      <p class="mb-6 max-w-2xl text-sm text-slate-500">
        API keys authenticate programmatic access to the REST API. Send them in the{' '}
        <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs">X-Api-Key</code> header (or{' '}
        <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs">Authorization: Bearer …</code>). They can manage
        proxy entities (hosts, redirects, access lists, streams, certificates) but not users, settings, backups or
        other API keys. See <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs">docs/API.md</code> for the full
        reference.
      </p>

      <Modal open={showCreate()} title="Create an API key" onClose={() => setShowCreate(false)}>
        <CreateKeyForm
          onCreated={(result) => {
            setShowCreate(false);
            setCreated(result);
            revalidate('api-keys');
          }}
        />
      </Modal>

      <Modal
        open={created() !== null}
        title="API key created"
        onClose={() => setCreated(null)}
      >
        <div class="space-y-4">
          <div class="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            Copy this key now — it is shown only once and cannot be retrieved again.
          </div>
          <div class="flex items-center gap-2">
            <code class="flex-1 break-all rounded-md border border-slate-300 bg-slate-50 px-3 py-2 text-sm">
              {created()?.plaintext}
            </code>
            <button
              class="shrink-0 rounded-md border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 shadow-sm hover:bg-slate-50"
              onClick={() => void copyPlaintext()}
            >
              Copy
            </button>
          </div>
          <p class="text-xs text-slate-500">
            Example: <code class="rounded bg-slate-100 px-1.5 py-0.5">curl -H "X-Api-Key: {created()?.plaintext}" http://localhost:5081/api/v1/hosts</code>
          </p>
          <button
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
            onClick={() => setCreated(null)}
          >
            Done
          </button>
        </div>
      </Modal>

      <Show when={keys().length > 0} fallback={<p class="text-sm text-slate-500">No API keys yet.</p>}>
        <table class="w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
          <thead>
            <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3">Name</th>
              <th class="px-4 py-3">Key prefix</th>
              <th class="px-4 py-3">Created</th>
              <th class="px-4 py-3">Last used</th>
              <th class="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            <For each={keys()}>
              {(key) => (
                <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                  <td class="px-4 py-3 font-medium text-slate-800">{key.name}</td>
                  <td class="px-4 py-3">
                    <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-700">{key.prefix}…</code>
                  </td>
                  <td class="px-4 py-3 text-slate-600">{formatDate(key.createdAt)}</td>
                  <td class="px-4 py-3 text-slate-600">{key.lastUsedAt ? formatDate(key.lastUsedAt) : 'never'}</td>
                  <td class="px-4 py-3">
                    <div class="flex items-center justify-end gap-2">
                      <button
                        disabled={busyId() === key.id}
                        class="text-xs font-medium text-red-600 hover:text-red-700 disabled:opacity-50"
                        onClick={() => void remove(key)}
                      >
                        {busyId() === key.id ? 'Deleting…' : 'Delete'}
                      </button>
                    </div>
                  </td>
                </tr>
              )}
            </For>
          </tbody>
        </table>
      </Show>
    </section>
  );
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function CreateKeyForm(props: { onCreated: (result: CreatedApiKeyDto) => void }) {
  const [name, setName] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await api.post<CreatedApiKeyDto>('/api-keys', { name: name() });
      props.onCreated(result);
    } catch (e) {
      setError(toMessages(e));
      setBusy(false);
    }
  }

  return (
    <form class="space-y-4" onSubmit={submit}>
      <Show when={error()}>
        <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          <ul class="list-disc space-y-1 pl-5">
            <For each={error()!}>{(message) => <li>{message}</li>}</For>
          </ul>
        </div>
      </Show>
      <label class="block">
        <span class="text-sm font-medium text-slate-700">Name</span>
        <input
          class="mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
          value={name()}
          onInput={(e) => setName(e.currentTarget.value)}
          placeholder="CI deploy script"
          required
          autofocus
        />
        <span class="mt-1 block text-xs text-slate-500">A label to help you remember what uses this key.</span>
      </label>
      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Creating…' : 'Create API key'}
        </button>
        <button type="button" class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={() => props.onCreated}>
          Cancel
        </button>
      </div>
    </form>
  );
}
