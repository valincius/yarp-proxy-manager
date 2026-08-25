import { Title } from '@solidjs/meta';
import { createEffect, createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../lib/toast';
import Modal from '../../components/Modal';
import { DeleteButton } from '../../components/ActionButtons';
import type { AcmeSettings, DnsCredentialDto, DockerSettings, NotFoundSettings } from '../../lib/types';

const loadAcmeSettings = query(async (): Promise<AcmeSettings> => api.get('/acme-settings'), 'acme-settings');
const loadDnsCredentials = query(
  async (): Promise<DnsCredentialDto[]> => api.get('/dns-credentials'),
  'dns-credentials',
);

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

const navPill =
  'rounded-full border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:border-blue-400 hover:text-blue-700';

const NAV_ITEMS = [
  { href: '#acme', label: 'Certificates' },
  { href: '#dns-credentials', label: 'DNS credentials' },
  { href: '#not-found', label: '404 page' },
  { href: '#docker', label: 'Docker' },
  { href: '#backup', label: 'Backup & Restore' },
  { href: '#api-keys', label: 'API Keys' },
] as const;

function SectionNav() {
  return (
    <nav class="sticky top-0 z-20 -mx-8 mb-8 border-b border-slate-200 bg-slate-100/95 px-8 py-3 backdrop-blur">
      <div class="flex flex-wrap gap-2">
        <For each={NAV_ITEMS}>
          {(item) => (
            <a href={item.href} class={navPill}>
              {item.label}
            </a>
          )}
        </For>
      </div>
    </nav>
  );
}

export default function Settings() {
  return (
    <section>
      <Title>Settings - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">Settings</h1>
      <SectionNav />
      <div class="flex flex-col gap-8">
        <AcmeSection />
        <DnsCredentialsSection />
        <NotFoundSection />
        <DockerSection />
        <BackupSection />
        <ApiKeysSection />
      </div>
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
    <section id="acme" class="scroll-mt-24 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
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

function DnsCredentialsSection() {
  const credentials = createMemo(() => loadDnsCredentials());
  const [showAdd, setShowAdd] = createSignal(false);

  async function remove(credential: DnsCredentialDto) {
    if (!confirm(`Delete DNS credential "${credential.name}"?`)) {
      return;
    }
    await api.del(`/dns-credentials/${credential.id}`);
    // Keep both cache entries fresh: this section ('dns-credentials') and the
    // SSL page's combined query ('certificates'), which feeds the DNS-01 picker.
    revalidate('dns-credentials');
    revalidate('certificates');
  }

  return (
    <section id="dns-credentials" class="scroll-mt-24 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <div class="mb-4 flex items-center justify-between">
        <div>
          <h2 class="mb-1 text-lg font-medium text-slate-800">DNS credentials (DNS-01)</h2>
          <p class="text-sm text-slate-500">
            Used to publish TXT records for DNS-01 certificate challenges. Stored encrypted with the app's Data
            Protection keys.
          </p>
        </div>
        <button
          class="shrink-0 rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm font-medium text-slate-700 shadow-sm hover:bg-slate-50"
          onClick={() => setShowAdd(true)}
        >
          + Add credential
        </button>
      </div>
      <Show
        when={credentials().length > 0}
        fallback={
          <p class="text-sm text-slate-500">
            No DNS credentials yet. DNS-01 (wildcard) certificate requests need at least one.
          </p>
        }
      >
        <ul class="divide-y divide-slate-100 text-sm">
          <For each={credentials()}>
            {(credential) => (
              <li class="flex items-center justify-between py-2">
                <span class="text-slate-700">
                  {credential.name} <span class="text-xs text-slate-400">({credential.provider})</span>
                </span>
                <DeleteButton onClick={() => void remove(credential)} />
              </li>
            )}
          </For>
        </ul>
      </Show>
      <Modal open={showAdd()} title="Add DNS credential" onClose={() => setShowAdd(false)}>
        <CredentialForm onDone={() => setShowAdd(false)} />
      </Modal>
    </section>
  );
}

function CredentialForm(props: { onDone: () => void }) {
  const [name, setName] = createSignal('');
  const [token, setToken] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function add(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.post('/dns-credentials', { name: name(), apiToken: token() });
      revalidate('dns-credentials');
      revalidate('certificates');
      props.onDone();
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form class="space-y-4" onSubmit={add}>
      <ErrorBanner messages={error()} />
      <label class="block">
        <span class="text-sm font-medium text-slate-700">Name</span>
        <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} placeholder="Cloudflare" required />
      </label>
      <label class="block">
        <span class="text-sm font-medium text-slate-700">API token</span>
        <input type="password" class={inputClass} value={token()} onInput={(e) => setToken(e.currentTarget.value)} placeholder="Cloudflare API token" required />
        <span class="mt-1 block text-xs text-slate-500">
          Used to publish TXT records for DNS-01 challenges. Stored encrypted with the app's Data Protection keys.
        </span>
      </label>
      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Adding…' : 'Add credential'}
        </button>
        <button type="button" class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={props.onDone}>
          Cancel
        </button>
      </div>
    </form>
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
    <section id="not-found" class="scroll-mt-24 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
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
    <section id="docker" class="scroll-mt-24 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
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

function BackupSection() {
  const [busy, setBusy] = createSignal(false);
  const [message, setMessage] = createSignal<string | null>(null);
  const [error, setError] = createSignal<string[] | null>(null);

  async function exportBackup() {
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const response = await fetch('/api/v1/backup', { credentials: 'same-origin' });
      if (!response.ok) {
        throw new Error(`Export failed (${response.status}).`);
      }
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `yarp-proxy-manager-backup-${new Date().toISOString().slice(0, 10)}.json`;
      link.click();
      URL.revokeObjectURL(url);
      setMessage('Backup downloaded.');
    } catch (e) {
      setError([e instanceof Error ? e.message : 'Export failed.']);
    } finally {
      setBusy(false);
    }
  }

  async function restoreBackup(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    if (!confirm('Restoring replaces the current configuration (hosts, redirects, streams, access lists). Continue?')) {
      input.value = '';
      return;
    }

    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const payload = JSON.parse(await file.text());
      await api.post('/backup/validate', payload);
      await api.post('/backup/restore', payload);
      // Restore replaces hosts, redirects, streams and access lists — drop the
      // router query cache for every affected page so they refetch on visit.
      revalidate('host');
      revalidate('hosts-list');
      revalidate('dashboard');
      revalidate('redirects');
      revalidate('streams');
      revalidate('access-lists');
      setMessage('Configuration restored.');
    } catch (e) {
      setError(e instanceof ApiError ? e.errors ?? [e.message] : ['Restore failed — the file may not be a valid backup.']);
    } finally {
      setBusy(false);
      input.value = '';
    }
  }

  return (
    <section id="backup" class="scroll-mt-24 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-1 text-lg font-medium text-slate-800">Backup & Restore</h2>
      <p class="mb-4 text-sm text-slate-500">
        Downloads hosts, redirects, streams and access lists as JSON. Certificate private keys are not included —
        back up the container's <code class="rounded bg-slate-100 px-1">/data</code> volume for those.
      </p>
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="rounded-md border border-slate-200 bg-slate-50 p-4">
          <h3 class="text-sm font-semibold text-slate-800">Export configuration</h3>
          <p class="mb-3 mt-1 text-xs text-slate-500">Download the current configuration as a JSON backup file.</p>
          <button
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
            onClick={() => void exportBackup()}
          >
            {busy() ? 'Working…' : 'Download backup'}
          </button>
        </div>
        <div class="rounded-md border border-slate-200 bg-slate-50 p-4">
          <h3 class="text-sm font-semibold text-slate-800">Restore configuration</h3>
          <p class="mb-3 mt-1 text-xs text-slate-500">Choose a backup file to replace the current configuration.</p>
          <input type="file" accept="application/json" disabled={busy()} onChange={(e) => void restoreBackup(e)} />
        </div>
      </div>
      <div class="mt-4">
        <Show when={message()}>
          <div class="rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-700">{message()}</div>
        </Show>
        <Show when={error()}>
          <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            <ul class="list-disc space-y-1 pl-5">
              <For each={error()!}>{(e) => <li>{e}</li>}</For>
            </ul>
          </div>
        </Show>
      </div>
    </section>
  );
}

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

function ApiKeysSection() {
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
    <section id="api-keys" class="scroll-mt-24 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <div class="mb-4 flex items-center justify-between">
        <div>
          <h2 class="mb-1 text-lg font-medium text-slate-800">API Keys</h2>
          <p class="text-sm text-slate-500">
            API keys authenticate programmatic access to the REST API. Send them in the{' '}
            <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs">X-Api-Key</code> header (or{' '}
            <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs">Authorization: Bearer …</code>). They can
            manage proxy entities (hosts, redirects, access lists, streams, certificates) but not users, settings,
            backups or other API keys. See <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs">docs/API.md</code>{' '}
            for the full reference.
          </p>
        </div>
        <button
          class="shrink-0 rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          onClick={() => setShowCreate(true)}
        >
          + New API Key
        </button>
      </div>

      <Modal open={showCreate()} title="Create an API key" onClose={() => setShowCreate(false)}>
        <CreateKeyForm
          onCreated={(result) => {
            setShowCreate(false);
            setCreated(result);
            revalidate('api-keys');
          }}
          onCancel={() => setShowCreate(false)}
        />
      </Modal>

      <Modal open={created() !== null} title="API key created" onClose={() => setCreated(null)}>
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
                  <td class="px-4 py-3 text-slate-600">{formatDateTime(key.createdAt)}</td>
                  <td class="px-4 py-3 text-slate-600">{key.lastUsedAt ? formatDateTime(key.lastUsedAt) : 'never'}</td>
                  <td class="px-4 py-3">
                    <div class="flex items-center justify-end gap-1">
                      <DeleteButton
                        disabled={busyId() === key.id}
                        label={busyId() === key.id ? 'Deleting…' : 'Delete'}
                        onClick={() => void remove(key)}
                      />
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

function CreateKeyForm(props: { onCreated: (result: CreatedApiKeyDto) => void; onCancel: () => void }) {
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
      <ErrorBanner messages={error()} />
      <label class="block">
        <span class="text-sm font-medium text-slate-700">Name</span>
        <input
          class={inputClass}
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
        <button type="button" class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={props.onCancel}>
          Cancel
        </button>
      </div>
    </form>
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
