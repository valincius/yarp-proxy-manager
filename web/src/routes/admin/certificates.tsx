import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { api, ApiError } from '../../lib/api';
import { query, revalidate } from '@solidjs/router';
import { createMemo } from 'solid-js';
import type { AcmeSettings, CertificateDto, DnsCredentialDto } from '../../lib/types';

const loadCertificates = query(
  async (): Promise<{ certificates: CertificateDto[]; credentials: DnsCredentialDto[]; settings: AcmeSettings }> => {
    const [certificates, credentials, settings] = await Promise.all([
      api.get<CertificateDto[]>('/certificates'),
      api.get<DnsCredentialDto[]>('/dns-credentials'),
      api.get<AcmeSettings>('/acme-settings'),
    ]);
    return { certificates, credentials, settings };
  },
  'certificates',
);

const inputClass =
  'mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500';

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

function toMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors : [error.message];
  }
  return [error instanceof Error ? error.message : 'An unexpected error occurred.'];
}

export default function Certificates() {
  const data = createMemo(() => loadCertificates());

  return (
    <section class="space-y-8">
      <Title>SSL Certificates - YARP Proxy Manager</Title>
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">SSL Certificates</h1>
      </div>

      <SettingsSection />
      <IssueSection credentials={data().credentials} />
      <UploadSection />
      <CredentialsSection credentials={data().credentials} />

      <div>
        <h2 class="mb-3 text-lg font-medium text-slate-800">Certificates</h2>
        <Show when={data().certificates.length > 0} fallback={<p class="text-sm text-slate-500">No certificates yet.</p>}>
          <table class="w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
            <thead>
              <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
                <th class="px-4 py-3">Name</th>
                <th class="px-4 py-3">Domains</th>
                <th class="px-4 py-3">Provider</th>
                <th class="px-4 py-3">Status</th>
                <th class="px-4 py-3">Expires</th>
                <th class="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              <For each={data().certificates}>
                {(certificate) => (
                  <CertificateRow certificate={certificate} />
                )}
              </For>
            </tbody>
          </table>
        </Show>
      </div>
    </section>
  );
}

function CertificateRow(props: { certificate: CertificateDto }) {
  const certificate = () => props.certificate;
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function renew() {
    setBusy(true);
    setError(null);
    try {
      await api.post(`/certificates/${certificate().id}/renew`);
      revalidate('certificates');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!confirm(`Delete certificate "${certificate().name}"?`)) {
      return;
    }
    try {
      await api.del(`/certificates/${certificate().id}`);
      revalidate('certificates');
    } catch (e) {
      setError(toMessages(e));
    }
  }

  const expiry = () => {
    const notAfter = certificate().notAfter;
    if (!notAfter) return { label: '—', tone: 'text-slate-500' };
    const days = Math.floor((new Date(notAfter).getTime() - Date.now()) / 86_400_000);
    if (days < 0) return { label: `expired ${-days}d ago`, tone: 'text-red-600' };
    if (days < 30) return { label: `${days}d`, tone: 'text-amber-600' };
    return { label: `${days}d`, tone: 'text-slate-600' };
  };

  return (
    <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
      <td class="px-4 py-3 font-medium text-slate-800">{certificate().name}</td>
      <td class="px-4 py-3 text-slate-600">{certificate().domains.join(', ')}</td>
      <td class="px-4 py-3 text-slate-600">{certificate().provider}</td>
      <td class="px-4 py-3">
        <StatusBadge status={certificate().status} />
        <Show when={certificate().lastRenewalError}>
          <div class="mt-1 max-w-[16rem] truncate text-xs text-red-600" title={certificate().lastRenewalError!}>
            {certificate().lastRenewalError}
          </div>
        </Show>
      </td>
      <td class={`px-4 py-3 ${expiry().tone}`}>{expiry().label}</td>
      <td class="px-4 py-3">
        <div class="flex items-center justify-end gap-2">
          <Show when={certificate().provider === 'Acme'}>
            <button
              disabled={busy()}
              class="text-xs font-medium text-blue-600 hover:text-blue-700 disabled:opacity-50"
              onClick={() => void renew()}
            >
              {busy() ? 'Renewing…' : 'Renew'}
            </button>
          </Show>
          <button class="text-xs font-medium text-red-600 hover:text-red-700" onClick={() => void remove()}>
            Delete
          </button>
        </div>
        <ErrorBanner messages={error()} />
      </td>
    </tr>
  );
}

function StatusBadge(props: { status: string }) {
  const tones: Record<string, string> = {
    Issued: 'bg-green-100 text-green-700',
    Pending: 'bg-amber-100 text-amber-700',
    Failed: 'bg-red-100 text-red-700',
    Revoked: 'bg-slate-200 text-slate-600',
  };
  return (
    <span class={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${tones[props.status] ?? 'bg-slate-200 text-slate-600'}`}>
      {props.status}
    </span>
  );
}

function SettingsSection() {
  const settings = createMemo(() => loadCertificates());
  const [email, setEmail] = createSignal(settings().settings.email);
  const [staging, setStaging] = createSignal(settings().settings.staging);
  const [busy, setBusy] = createSignal(false);
  const [saved, setSaved] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function save(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setSaved(false);
    setError(null);
    try {
      await api.put('/acme-settings', { email: email(), directoryUrl: '', staging: staging() });
      setSaved(true);
      revalidate('certificates');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-4 text-lg font-medium text-slate-800">ACME Account</h2>
      <ErrorBanner messages={error()} />
      <Show when={saved()}>
        <div class="mb-3 rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-700">Settings saved.</div>
      </Show>
      <form class="grid grid-cols-1 gap-4 sm:grid-cols-2" onSubmit={save}>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Account email</span>
          <input type="email" required class={inputClass} value={email()} onInput={(e) => setEmail(e.currentTarget.value)} />
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

function IssueSection(props: { credentials: DnsCredentialDto[] }) {
  const [name, setName] = createSignal('');
  const [domains, setDomains] = createSignal('');
  const [challengeType, setChallengeType] = createSignal<'Http01' | 'Dns01'>('Http01');
  const [dnsCredentialId, setDnsCredentialId] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [result, setResult] = createSignal<string | null>(null);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setResult(null);
    setError(null);
    try {
      const certificate = await api.post<CertificateDto>('/certificates/issue', {
        name: name(),
        domains: domains()
          .split(',')
          .map((d) => d.trim())
          .filter(Boolean),
        challengeType: challengeType(),
        dnsCredentialId: challengeType() === 'Dns01' ? dnsCredentialId() || null : null,
      });
      setResult(`Certificate "${certificate.name}" issued (status: ${certificate.status}).`);
      revalidate('certificates');
      revalidate('hosts');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-4 text-lg font-medium text-slate-800">Request a new certificate</h2>
      <ErrorBanner messages={error()} />
      <Show when={result()}>
        <div class="mb-3 rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-700">{result()}</div>
      </Show>
      <form class="grid grid-cols-1 gap-4 sm:grid-cols-2" onSubmit={submit}>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} placeholder="My app cert" required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Domains</span>
          <input class={inputClass} value={domains()} onInput={(e) => setDomains(e.currentTarget.value)} placeholder="app.example.com, *.example.com" required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Challenge type</span>
          <select class={inputClass} value={challengeType()} onChange={(e) => setChallengeType(e.currentTarget.value as 'Http01' | 'Dns01')}>
            <option value="Http01">HTTP-01 (port 80)</option>
            <option value="Dns01">DNS-01 (TXT record — wildcards)</option>
          </select>
        </label>
        <Show when={challengeType() === 'Dns01'}>
          <label class="block">
            <span class="text-sm font-medium text-slate-700">DNS credential</span>
            <select class={inputClass} value={dnsCredentialId()} onChange={(e) => setDnsCredentialId(e.currentTarget.value)} required>
              <option value="">Select credential…</option>
              <For each={props.credentials}>
                {(credential) => <option value={credential.id}>{credential.name} ({credential.provider})</option>}
              </For>
            </select>
          </label>
        </Show>
        <div class="sm:col-span-2">
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Requesting…' : 'Request certificate'}
          </button>
          <span class="ml-3 text-xs text-slate-500">Issuance can take up to a minute (DNS propagation).</span>
        </div>
      </form>
    </section>
  );
}

function UploadSection() {
  const [name, setName] = createSignal('');
  const [domains, setDomains] = createSignal('');
  const [mode, setMode] = createSignal<'pfx' | 'pem'>('pem');
  const [pfxBase64, setPfxBase64] = createSignal('');
  const [pfxPassword, setPfxPassword] = createSignal('');
  const [certPem, setCertPem] = createSignal('');
  const [keyPem, setKeyPem] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [result, setResult] = createSignal<string | null>(null);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setResult(null);
    setError(null);
    try {
      const certificate = await api.post<CertificateDto>('/certificates/upload', {
        name: name(),
        domains: domains()
          .split(',')
          .map((d) => d.trim())
          .filter(Boolean),
        pfxBase64: mode() === 'pfx' ? pfxBase64() || null : null,
        pfxPassword: mode() === 'pfx' ? pfxPassword() || null : null,
        certificatePem: mode() === 'pem' ? certPem() || null : null,
        privateKeyPem: mode() === 'pem' ? keyPem() || null : null,
      });
      setResult(`Certificate "${certificate.name}" uploaded.`);
      revalidate('certificates');
      revalidate('hosts');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-4 text-lg font-medium text-slate-800">Upload a certificate</h2>
      <ErrorBanner messages={error()} />
      <Show when={result()}>
        <div class="mb-3 rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-700">{result()}</div>
      </Show>
      <form class="grid grid-cols-1 gap-4 sm:grid-cols-2" onSubmit={submit}>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Domains</span>
          <input class={inputClass} value={domains()} onInput={(e) => setDomains(e.currentTarget.value)} placeholder="app.example.com" required />
        </label>
        <div class="sm:col-span-2">
          <label class="mr-4 inline-flex items-center gap-2 text-sm text-slate-700">
            <input type="radio" checked={mode() === 'pem'} onChange={() => setMode('pem')} class="h-4 w-4" />
            PEM (cert + private key)
          </label>
          <label class="inline-flex items-center gap-2 text-sm text-slate-700">
            <input type="radio" checked={mode() === 'pfx'} onChange={() => setMode('pfx')} class="h-4 w-4" />
            PFX (base64)
          </label>
        </div>
        <Show when={mode() === 'pem'}>
          <label class="block sm:col-span-2">
            <span class="text-sm font-medium text-slate-700">Certificate PEM</span>
            <textarea rows={5} class={inputClass} value={certPem()} onInput={(e) => setCertPem(e.currentTarget.value)} required />
          </label>
          <label class="block sm:col-span-2">
            <span class="text-sm font-medium text-slate-700">Private key PEM (PKCS#8)</span>
            <textarea rows={5} class={inputClass} value={keyPem()} onInput={(e) => setKeyPem(e.currentTarget.value)} required />
          </label>
        </Show>
        <Show when={mode() === 'pfx'}>
          <label class="block sm:col-span-2">
            <span class="text-sm font-medium text-slate-700">PFX (base64)</span>
            <textarea rows={5} class={inputClass} value={pfxBase64()} onInput={(e) => setPfxBase64(e.currentTarget.value)} required />
          </label>
          <label class="block sm:col-span-2">
            <span class="text-sm font-medium text-slate-700">PFX password</span>
            <input type="password" class={inputClass} value={pfxPassword()} onInput={(e) => setPfxPassword(e.currentTarget.value)} />
          </label>
        </Show>
        <div class="sm:col-span-2">
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Uploading…' : 'Upload certificate'}
          </button>
        </div>
      </form>
    </section>
  );
}

function CredentialsSection(props: { credentials: DnsCredentialDto[] }) {
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
      setName('');
      setToken('');
      revalidate('certificates');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  async function remove(credential: DnsCredentialDto) {
    if (!confirm(`Delete DNS credential "${credential.name}"?`)) {
      return;
    }
    await api.del(`/dns-credentials/${credential.id}`);
    revalidate('certificates');
  }

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-4 text-lg font-medium text-slate-800">DNS credentials (DNS-01)</h2>
      <ErrorBanner messages={error()} />
      <form class="mb-4 grid grid-cols-1 gap-4 sm:grid-cols-3" onSubmit={add}>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} placeholder="Cloudflare" required />
        </label>
        <label class="block sm:col-span-1">
          <span class="text-sm font-medium text-slate-700">API token</span>
          <input type="password" class={inputClass} value={token()} onInput={(e) => setToken(e.currentTarget.value)} placeholder="Cloudflare API token" required />
        </label>
        <div class="flex items-end">
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Adding…' : 'Add credential'}
          </button>
        </div>
      </form>
      <Show when={props.credentials.length > 0} fallback={<p class="text-sm text-slate-500">No DNS credentials yet.</p>}>
        <ul class="divide-y divide-slate-100 text-sm">
          <For each={props.credentials}>
            {(credential) => (
              <li class="flex items-center justify-between py-2">
                <span class="text-slate-700">
                  {credential.name} <span class="text-xs text-slate-400">({credential.provider})</span>
                </span>
                <button class="text-xs font-medium text-red-600 hover:text-red-700" onClick={() => void remove(credential)}>
                  Delete
                </button>
              </li>
            )}
          </For>
        </ul>
      </Show>
    </section>
  );
}
