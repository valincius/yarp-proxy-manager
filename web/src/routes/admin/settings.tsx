import { Title } from '@solidjs/meta';
import { createEffect, createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../lib/toast';
import type { AcmeSettings, DockerSettings, NotFoundSettings } from '../../lib/types';

const loadAcmeSettings = query(async (): Promise<AcmeSettings> => api.get('/acme-settings'), 'acme-settings');

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

export default function Settings() {
  return (
    <section class="space-y-8">
      <Title>Settings - YARP Proxy Manager</Title>
      <h1 class="text-2xl font-semibold text-slate-800">Settings</h1>
      <AcmeSection />
      <NotFoundSection />
      <DockerSection />
    </section>
  );
}

function AcmeSection() {
  const settings = createMemo(() => loadAcmeSettings());
  const toast = useToast();
  const [email, setEmail] = createSignal('');
  const [staging, setStaging] = createSignal(false);
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  createEffect(settings, (s) => {
    setEmail(s.email);
    setStaging(s.staging);
  });

  async function save(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.put('/acme-settings', { email: email(), directoryUrl: '', staging: staging() });
      revalidate('acme-settings');
      toast.push('ACME settings saved.', 'success');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-1 text-lg font-medium text-slate-800">Certificates — ACME account</h2>
      <p class="mb-4 text-sm text-slate-500">
        Let's Encrypt account used when requesting certificates. Applies to all new certificate requests.
      </p>
      <ErrorBanner messages={error()} />
      <form class="grid grid-cols-1 gap-4 sm:grid-cols-2" onSubmit={save}>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Account email</span>
          <input type="email" required class={inputClass} value={email()} onInput={(e) => setEmail(e.currentTarget.value)} />
          <span class="mt-1 block text-xs text-slate-500">Renewal notices and rate-limit recovery are sent to this address.</span>
        </label>
        <label class="flex items-end gap-2 pb-2">
          <input
            type="checkbox"
            class="h-4 w-4 rounded border-slate-300 text-blue-600"
            checked={staging()}
            onChange={(e) => setStaging(e.currentTarget.checked)}
          />
          <span class="text-sm text-slate-700">Use Let's Encrypt staging CA (no rate limits)</span>
        </label>
        <div>
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Saving…' : 'Save ACME settings'}
          </button>
        </div>
      </form>
    </section>
  );
}

const DEFAULT_404_TEMPLATE = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>404 — Not Found</title>
  <style>
    body { font-family: system-ui, sans-serif; background: #f1f5f9; color: #0f172a;
           display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }
    .card { background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 48px;
            text-align: center; }
    h1 { font-size: 56px; margin: 0; color: #2563eb; }
    .code { color: #64748b; margin-top: 12px; font-size: 14px; }
  </style>
</head>
<body>
  <div class="card">
    <h1>404</h1>
    <p>The page you requested could not be found.</p>
    <div class="code">{{host}}{{path}}</div>
  </div>
</body>
</html>`;

function NotFoundSection() {
  const loadSettings = query(async (): Promise<NotFoundSettings> => api.get('/settings/not-found'), 'not-found-settings');
  const settings = createMemo(() => loadSettings());
  const toast = useToast();
  const [mode, setMode] = createSignal<'Default' | 'Empty' | 'Custom'>('Default');
  const [template, setTemplate] = createSignal(DEFAULT_404_TEMPLATE);
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  createEffect(settings, (s) => {
    setMode(s.mode);
    setTemplate(s.template || DEFAULT_404_TEMPLATE);
  });

  async function save(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.put('/settings/not-found', { mode: mode(), template: mode() === 'Custom' ? template() : null });
      revalidate('not-found-settings');
      toast.push('404 page settings saved.', 'success');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-1 text-lg font-medium text-slate-800">404 page</h2>
      <p class="mb-4 text-sm text-slate-500">
        What the proxy port returns when no route matches a request (e.g. an unknown hostname or path).
      </p>
      <ErrorBanner messages={error()} />
      <form class="space-y-4" onSubmit={save}>
        <div>
          <span class="text-sm font-medium text-slate-700">Response</span>
          <div class="mt-2 flex flex-col gap-2 sm:flex-row">
            <label class="inline-flex items-center gap-2 text-sm text-slate-700">
              <input type="radio" name="nf-mode" checked={mode() === 'Default'} onChange={() => setMode('Default')} class="h-4 w-4" />
              Built-in page
            </label>
            <label class="inline-flex items-center gap-2 text-sm text-slate-700">
              <input type="radio" name="nf-mode" checked={mode() === 'Empty'} onChange={() => setMode('Empty')} class="h-4 w-4" />
              Empty body
            </label>
            <label class="inline-flex items-center gap-2 text-sm text-slate-700">
              <input type="radio" name="nf-mode" checked={mode() === 'Custom'} onChange={() => setMode('Custom')} class="h-4 w-4" />
              Custom HTML
            </label>
          </div>
        </div>
        <Show when={mode() === 'Custom'}>
          <label class="block">
            <span class="text-sm font-medium text-slate-700">404.html template</span>
            <textarea rows={10} class={`${inputClass} font-mono text-xs`} value={template()} onInput={(e) => setTemplate(e.currentTarget.value)} />
            <span class="mt-1 block text-xs text-slate-500">
              Placeholders: <code>{'{{host}}'}</code> (request hostname), <code>{'{{path}}'}</code> (request path),{' '}
              <code>{'{{method}}'}</code> (HTTP method), <code>{'{{now}}'}</code> (current time).
            </span>
          </label>
          <div class="rounded-md border border-slate-200 bg-slate-50 p-3">
            <p class="mb-2 text-xs font-medium uppercase tracking-wide text-slate-500">Preview</p>
            <div class="max-h-40 overflow-auto rounded border border-slate-200 bg-white text-xs">
              <Show when={mode() === 'Custom'}>
                <div innerHTML={previewHtml(template())} />
              </Show>
            </div>
          </div>
        </Show>
        <div>
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Saving…' : 'Save 404 page'}
          </button>
        </div>
      </form>
    </section>
  );
}

function previewHtml(template: string): string {
  return template
    .replaceAll('{{host}}', 'app.example.com')
    .replaceAll('{{path}}', '/missing')
    .replaceAll('{{method}}', 'GET')
    .replaceAll('{{now}}', new Date().toLocaleString());
}

function DockerSection() {
  const loadDocker = query(async (): Promise<DockerSettings> => api.get('/settings/docker'), 'docker-settings');
  const docker = createMemo(() => loadDocker());
  const toast = useToast();
  const [enabled, setEnabled] = createSignal(false);
  const [host, setHost] = createSignal('');
  const [network, setNetwork] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [syncing, setSyncing] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  createEffect(docker, (d) => {
    setEnabled(d.enabled);
    setHost(d.host ?? '');
    setNetwork(d.network ?? '');
  });

  async function save(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.put('/settings/docker', { enabled: enabled(), host: host() || null, network: network() || null });
      revalidate('docker-settings');
      toast.push('Docker integration settings saved.', 'success');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  async function syncNow() {
    setSyncing(true);
    setError(null);
    try {
      const result = await api.post<DockerSettings>('/settings/docker/sync');
      revalidate('docker-settings');
      toast.push(result.lastError ? `Sync finished with errors: ${result.lastError}` : 'Sync complete.', result.lastError ? 'error' : 'success');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setSyncing(false);
    }
  }

  const status = () => docker();

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <div class="mb-4 flex items-center justify-between">
        <div>
          <h2 class="text-lg font-medium text-slate-800">Docker integration</h2>
          <p class="mt-1 text-sm text-slate-500">
            Traefik-style autodiscovery: containers labelled{' '}
            <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs">proxy-manager.enable=true</code> are published
            as proxy hosts automatically and disposed when the container disappears.
          </p>
        </div>
        <button
          class="shrink-0 rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm font-medium text-slate-700 shadow-sm hover:bg-slate-50 disabled:opacity-50"
          onClick={() => void syncNow()}
          disabled={syncing()}
        >
          {syncing() ? 'Syncing…' : 'Sync now'}
        </button>
      </div>

      <ErrorBanner messages={error()} />

      <div class="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatusTile label="Enabled" value={status().enabled ? 'Yes' : 'No'} />
        <StatusTile label="Discovered containers" value={String(status().discoveredContainers)} />
        <StatusTile label="Managed hosts" value={String(status().managedHosts)} />
        <StatusTile label="Last sync" value={formatLastSync(status().lastSyncAt)} />
      </div>
      <Show when={status().lastError}>
        <div class="mb-4 rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          <p class="font-medium">Last sync error</p>
          <p class="mt-1 break-words text-xs">{status().lastError}</p>
        </div>
      </Show>

      <form class="grid grid-cols-1 gap-4 sm:grid-cols-3" onSubmit={save}>
        <label class="flex items-end gap-2 pb-2 text-sm text-slate-700 sm:col-span-3">
          <input
            type="checkbox"
            class="h-4 w-4 rounded border-slate-300 text-blue-600"
            checked={enabled()}
            onChange={(e) => setEnabled(e.currentTarget.checked)}
          />
          Enable Docker autodiscovery
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Docker engine endpoint (optional)</span>
          <input class={inputClass} value={host()} onInput={(e) => setHost(e.currentTarget.value)} placeholder="Leave empty for default (npipe/unix socket)" />
          <span class="mt-1 block text-xs text-slate-500">e.g. <code>tcp://host:2375</code> for a remote engine.</span>
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Network (optional)</span>
          <input class={inputClass} value={network()} onInput={(e) => setNetwork(e.currentTarget.value)} placeholder="e.g. proxy" />
          <span class="mt-1 block text-xs text-slate-500">Which network's IP to use for each container; defaults to the first with an address.</span>
        </label>
        <div class="flex items-end">
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Saving…' : 'Save Docker settings'}
          </button>
        </div>
      </form>

      <div class="mt-4 rounded-md border border-slate-200 bg-slate-50 p-3">
        <p class="text-xs font-medium uppercase tracking-wide text-slate-500">Supported labels</p>
        <ul class="mt-2 grid grid-cols-1 gap-1 text-xs text-slate-600 sm:grid-cols-2">
          <li><code class="rounded bg-white px-1.5 py-0.5">proxy-manager.enable=true</code> — opt in</li>
          <li><code class="rounded bg-white px-1.5 py-0.5">proxy-manager.host=app.example.com</code> — domain(s), comma-separated</li>
          <li><code class="rounded bg-white px-1.5 py-0.5">proxy-manager.port=8080</code> — container port</li>
          <li><code class="rounded bg-white px-1.5 py-0.5">proxy-manager.scheme=http|https</code> — upstream scheme</li>
          <li><code class="rounded bg-white px-1.5 py-0.5">proxy-manager.name=My App</code> — display name</li>
        </ul>
      </div>
    </section>
  );
}

function StatusTile(props: { label: string; value: string }) {
  return (
    <div class="rounded-md border border-slate-200 bg-slate-50 px-3 py-2">
      <div class="text-[11px] font-medium uppercase tracking-wide text-slate-500">{props.label}</div>
      <div class="mt-0.5 truncate text-sm font-semibold text-slate-800">{props.value}</div>
    </div>
  );
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function formatLastSync(value: string | null): string {
  return value ? formatDateTime(value) : 'never';
}
