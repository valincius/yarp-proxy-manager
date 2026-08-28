import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import Modal from '../../components/Modal';
import type { RedirectHost, RedirectHostInput } from '../../lib/types';

const loadRedirects = query(async (): Promise<RedirectHost[]> => api.get('/redirects'), 'redirects');

const inputClass =
  'mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500';

function toMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors : [error.message];
  }
  return [error instanceof Error ? error.message : 'An unexpected error occurred.'];
}

function ErrorBanner(props: { messages: string[] | null }) {
  return (
    <Show when={props.messages && props.messages.length > 0}>
      <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
        <ul class="list-disc space-y-1 pl-5">
          <For each={props.messages!}>{(message) => <li>{message}</li>}</For>
        </ul>
      </div>
    </Show>
  );
}

export default function Redirects() {
  const redirects = createMemo(() => loadRedirects());
  const [editing, setEditing] = createSignal<RedirectHost | null>(null);
  const [showForm, setShowForm] = createSignal(false);

  async function remove(redirect: RedirectHost) {
    if (!confirm(`Delete redirect "${redirect.name}"?`)) {
      return;
    }
    await api.del(`/redirects/${redirect.id}`);
    revalidate('redirects');
  }

  return (
    <section>
      <Title>Redirection Hosts - YARP Proxy Manager</Title>
      <div class="page-header mb-6 flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">Redirection Hosts</h1>
        <button
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          onClick={() => {
            setEditing(null);
            setShowForm(true);
          }}
        >
          + New Redirect
        </button>
      </div>

      <Modal
        open={showForm()}
        title={editing() ? `Edit redirect "${editing()!.name}"` : 'New redirect'}
        onClose={() => setShowForm(false)}
      >
        <RedirectForm
          initial={editing() ?? undefined}
          onDone={() => {
            setShowForm(false);
            revalidate('redirects');
          }}
        />
      </Modal>

      <Show when={redirects().length > 0} fallback={<p class="text-sm text-slate-500">No redirects yet.</p>}>
        <table class="responsive-card-table w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
          <thead>
            <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3">Name</th>
              <th class="px-4 py-3">Domains</th>
              <th class="px-4 py-3">Redirect to</th>
              <th class="px-4 py-3">Status</th>
              <th class="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            <For each={redirects()}>
              {(redirect) => (
                <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                  <td data-label="Name" class="px-4 py-3 font-medium text-slate-800">{redirect.name}</td>
                  <td data-label="Domains" class="px-4 py-3 text-slate-600">{redirect.domainNames.join(', ')}</td>
                  <td data-label="Redirect to" class="px-4 py-3 text-slate-600">
                    {redirect.forwardScheme}://{redirect.forwardHost}:{redirect.forwardPort}
                  </td>
                  <td data-label="Status" class="px-4 py-3">
                    <span
                      class={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                        redirect.enabled ? 'bg-green-100 text-green-700' : 'bg-slate-200 text-slate-600'
                      }`}
                    >
                      {redirect.enabled ? `Active (${redirect.statusCode})` : 'Disabled'}
                    </span>
                    {redirect.preservePath ? (
                      <span
                        class="ml-1 inline-flex rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-700"
                        title="The original request path and query string are appended to the redirect target."
                      >
                        path preserved
                      </span>
                    ) : null}
                  </td>
                  <td data-label="Actions" class="px-4 py-3">
                    <div class="flex items-center justify-end gap-2">
                      <button
                        class="text-xs font-medium text-blue-600 hover:text-blue-700"
                        onClick={() => {
                          setEditing(redirect);
                          setShowForm(true);
                        }}
                      >
                        Edit
                      </button>
                      <button class="text-xs font-medium text-red-600 hover:text-red-700" onClick={() => void remove(redirect)}>
                        Delete
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

function RedirectForm(props: { initial?: RedirectHost; onDone: () => void }) {
  const [name, setName] = createSignal(props.initial?.name ?? '');
  const [domains, setDomains] = createSignal(props.initial?.domainNames.join(', ') ?? '');
  const [forwardScheme, setForwardScheme] = createSignal(props.initial?.forwardScheme ?? 'http');
  const [forwardHost, setForwardHost] = createSignal(props.initial?.forwardHost ?? '');
  const [forwardPort, setForwardPort] = createSignal(props.initial?.forwardPort ?? 80);
  const [statusCode, setStatusCode] = createSignal(props.initial?.statusCode ?? 301);
  const [preservePath, setPreservePath] = createSignal(props.initial?.preservePath ?? true);
  const [enabled, setEnabled] = createSignal(props.initial?.enabled ?? true);
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const input: RedirectHostInput = {
      name: name(),
      domainNames: domains()
        .split(',')
        .map((d) => d.trim())
        .filter(Boolean),
      enabled: enabled(),
      statusCode: statusCode() as 301 | 302,
      preservePath: preservePath(),
      forwardScheme: forwardScheme() as 'http' | 'https',
      forwardHost: forwardHost(),
      forwardPort: Number(forwardPort()),
      certificateId: null,
    };
    try {
      if (props.initial) {
        await api.put(`/redirects/${props.initial.id}`, input);
      } else {
        await api.post('/redirects', input);
      }
      props.onDone();
    } catch (e) {
      setError(toMessages(e));
      setBusy(false);
    }
  }

  return (
    <form class="space-y-4" onSubmit={submit}>
      <ErrorBanner messages={error()} />
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Domains</span>
          <input
            class={inputClass}
            value={domains()}
            onInput={(e) => setDomains(e.currentTarget.value)}
            placeholder="old.example.com"
            required
          />
          <span class="mt-1 block text-xs text-slate-500">
            Requests for these hostnames are redirected. Comma-separate multiple domains.
          </span>
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Redirect scheme</span>
          <select class={inputClass} value={forwardScheme()} onChange={(e) => setForwardScheme(e.currentTarget.value as 'http' | 'https')}>
            <option value="http">http</option>
            <option value="https">https</option>
          </select>
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Redirect hostname</span>
          <input class={inputClass} value={forwardHost()} onInput={(e) => setForwardHost(e.currentTarget.value)} placeholder="example.com" required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Redirect port</span>
          <input class={inputClass} type="number" min={1} max={65535} value={forwardPort()} onInput={(e) => setForwardPort(Number(e.currentTarget.value))} required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Status code</span>
          <select class={inputClass} value={statusCode()} onChange={(e) => setStatusCode(Number(e.currentTarget.value) as 301 | 302)}>
            <option value={301}>301 Moved Permanently</option>
            <option value={302}>302 Found</option>
          </select>
        </label>
      </div>
      <div class="flex gap-6">
        <label class="inline-flex items-center gap-2 text-sm text-slate-700">
          <input type="checkbox" class="h-4 w-4 rounded border-slate-300" checked={preservePath()} onChange={(e) => setPreservePath(e.currentTarget.checked)} />
          Preserve path and query
        </label>
        <label class="inline-flex items-center gap-2 text-sm text-slate-700">
          <input type="checkbox" class="h-4 w-4 rounded border-slate-300" checked={enabled()} onChange={(e) => setEnabled(e.currentTarget.checked)} />
          Enabled
        </label>
      </div>
      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Saving…' : props.initial ? 'Save changes' : 'Create redirect'}
        </button>
        <button type="button" class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={props.onDone}>
          Cancel
        </button>
      </div>
    </form>
  );
}
