import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { api, ApiError } from '../../lib/api';
import { query, revalidate } from '@solidjs/router';
import { createMemo } from 'solid-js';
import Modal from '../../components/Modal';
import { useToast } from '../../lib/toast';
import type { CertificateDto, DnsCredentialDto } from '../../lib/types';

const loadCertificates = query(
  async (): Promise<{ certificates: CertificateDto[]; credentials: DnsCredentialDto[] }> => {
    const [certificates, credentials] = await Promise.all([
      api.get<CertificateDto[]>('/certificates'),
      api.get<DnsCredentialDto[]>('/dns-credentials'),
    ]);
    return { certificates, credentials };
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
  const [showIssue, setShowIssue] = createSignal(false);
  const [showUpload, setShowUpload] = createSignal(false);
  const [viewing, setViewing] = createSignal<CertificateDto | null>(null);

  return (
    <section class="space-y-8">
      <Title>SSL Certificates - YARP Proxy Manager</Title>
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">SSL Certificates</h1>
        <div class="flex gap-2">
          <button
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
            onClick={() => setShowIssue(true)}
          >
            + Request certificate
          </button>
          <button
            class="rounded-md border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 shadow-sm hover:bg-slate-50"
            onClick={() => setShowUpload(true)}
          >
            Upload
          </button>
        </div>
      </div>

      <Modal open={showIssue()} title="Request a new certificate" onClose={() => setShowIssue(false)}>
        <IssueSection credentials={data().credentials} onDone={() => setShowIssue(false)} />
      </Modal>

      <Modal open={showUpload()} title="Upload a certificate" onClose={() => setShowUpload(false)}>
        <UploadSection onDone={() => setShowUpload(false)} />
      </Modal>

      <Modal
        open={viewing() !== null}
        title={viewing() ? `Certificate: ${viewing()!.name}` : ''}
        onClose={() => setViewing(null)}
        size="max-w-xl"
      >
        <CertificateDetail certificate={viewing()} onClose={() => setViewing(null)} />
      </Modal>

      <div>
        <div class="mb-3 flex items-center justify-between">
          <h2 class="text-lg font-medium text-slate-800">Certificates</h2>
          <span class="text-xs text-slate-500">
            ACME account settings and DNS credentials live on the{' '}
            <a href="/admin/settings" class="text-blue-600 hover:text-blue-700">
              Settings
            </a>{' '}
            page.
          </span>
        </div>
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
                  <CertificateRow certificate={certificate} onView={() => setViewing(certificate)} />
                )}
              </For>
            </tbody>
          </table>
        </Show>
      </div>
    </section>
  );
}

function CertificateDetail(props: { certificate: CertificateDto | null; onClose: () => void }) {
  const toast = useToast();
  const certificate = () => props.certificate;
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function renew() {
    setBusy(true);
    setError(null);
    try {
      await api.post(`/certificates/${certificate()!.id}/renew`);
      toast.push(`Renewal started for "${certificate()!.name}".`, 'info');
      revalidate('certificates');
      props.onClose();
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!confirm(`Delete certificate "${certificate()?.name}"?`)) {
      return;
    }
    try {
      await api.del(`/certificates/${certificate()!.id}`);
      toast.push('Certificate deleted.', 'success');
      revalidate('certificates');
      props.onClose();
    } catch (e) {
      setError(toMessages(e));
    }
  }

  return (
    <Show when={certificate()}>
      <div class="space-y-4">
        <ErrorBanner messages={error()} />
        <dl class="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Detail label="Name" value={certificate()!.name} />
          <Detail label="Provider" value={certificate()!.provider === 'Acme' ? 'Let\'s Encrypt (ACME)' : 'Manual upload'} />
          <Detail label="Status" value={certificate()!.status} />
          <Detail label="Challenge" value={certificate()!.challengeType === 'Dns01' ? 'DNS-01 (TXT record)' : certificate()!.challengeType === 'Http01' ? 'HTTP-01' : '—'} />
          <div class="sm:col-span-2">
            <dt class="text-xs font-medium uppercase tracking-wide text-slate-500">Domains</dt>
            <dd class="mt-1 flex flex-wrap gap-1">
              <For each={certificate()!.domains}>
                {(domain) => (
                  <code class="rounded bg-slate-100 px-2 py-0.5 text-xs text-slate-700">{domain}</code>
                )}
              </For>
            </dd>
          </div>
          <Detail label="Not valid before" value={formatDate(certificate()!.notBefore)} />
          <Detail label="Not valid after" value={formatDate(certificate()!.notAfter)} />
          <Detail label="Last renewal attempt" value={formatDate(certificate()!.lastRenewalAttempt)} />
          <Detail label="Created" value={formatDate(certificate()!.createdAt)} />
        </dl>
        <Show when={certificate()!.lastRenewalError}>
          <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            <p class="font-medium">Last renewal error</p>
            <p class="mt-1 break-words text-xs">{certificate()!.lastRenewalError}</p>
          </div>
        </Show>
        <div class="flex items-center gap-3 pt-2">
          <Show when={certificate()!.provider === 'Acme'}>
            <button
              disabled={busy()}
              class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
              onClick={() => void renew()}
            >
              {busy() ? 'Renewing…' : 'Renew now'}
            </button>
          </Show>
          <button
            class="rounded-md border border-red-300 bg-white px-4 py-2 text-sm font-semibold text-red-700 shadow-sm hover:bg-red-50"
            onClick={() => void remove()}
          >
            Delete
          </button>
          <button class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={props.onClose}>
            Close
          </button>
        </div>
      </div>
    </Show>
  );
}

function Detail(props: { label: string; value: string }) {
  return (
    <div>
      <dt class="text-xs font-medium uppercase tracking-wide text-slate-500">{props.label}</dt>
      <dd class="mt-1 text-sm text-slate-800">{props.value}</dd>
    </div>
  );
}

function formatDate(value: string | null): string {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function CertificateRow(props: { certificate: CertificateDto; onView: () => void }) {
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
          <button class="text-xs font-medium text-slate-600 hover:text-slate-900" onClick={props.onView}>
            View
          </button>
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

function IssueSection(props: { credentials: DnsCredentialDto[]; onDone: () => void }) {
  const [name, setName] = createSignal('');
  const [domains, setDomains] = createSignal('');
  const [challengeType, setChallengeType] = createSignal<'Http01' | 'Dns01'>('Http01');
  const [dnsCredentialId, setDnsCredentialId] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.post<CertificateDto>('/certificates/issue', {
        name: name(),
        domains: domains()
          .split(',')
          .map((d) => d.trim())
          .filter(Boolean),
        challengeType: challengeType(),
        dnsCredentialId: challengeType() === 'Dns01' ? dnsCredentialId() || null : null,
      });
      revalidate('certificates');
      revalidate('hosts');
      props.onDone();
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form class="space-y-4" onSubmit={submit}>
      <ErrorBanner messages={error()} />
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} placeholder="My app cert" required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Domains</span>
          <input class={inputClass} value={domains()} onInput={(e) => setDomains(e.currentTarget.value)} placeholder="app.example.com, *.example.com" required />
          <span class="mt-1 block text-xs text-slate-500">Wildcard domains require DNS-01.</span>
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
            <Show
              when={props.credentials.length > 0}
              fallback={
                <p class="mt-1 rounded-md border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800">
                  No DNS credentials yet. Add one on the{' '}
                  <a href="/admin/settings#dns-credentials" class="font-medium underline hover:text-amber-900">
                    Settings
                  </a>{' '}
                  page to use DNS-01.
                </p>
              }
            >
              <select class={inputClass} value={dnsCredentialId()} onChange={(e) => setDnsCredentialId(e.currentTarget.value)} required>
                <option value="">Select credential…</option>
                <For each={props.credentials}>
                  {(credential) => <option value={credential.id}>{credential.name} ({credential.provider})</option>}
                </For>
              </select>
            </Show>
          </label>
        </Show>
      </div>
      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Requesting…' : 'Request certificate'}
        </button>
        <span class="text-xs text-slate-500">Issuance can take up to a minute (DNS propagation).</span>
      </div>
    </form>
  );
}

function UploadSection(props: { onDone: () => void }) {
  const [name, setName] = createSignal('');
  const [domains, setDomains] = createSignal('');
  const [mode, setMode] = createSignal<'pfx' | 'pem'>('pem');
  const [pfxBase64, setPfxBase64] = createSignal('');
  const [pfxPassword, setPfxPassword] = createSignal('');
  const [certPem, setCertPem] = createSignal('');
  const [keyPem, setKeyPem] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.post<CertificateDto>('/certificates/upload', {
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
      revalidate('certificates');
      revalidate('hosts');
      props.onDone();
    } catch (e) {
      setError(toMessages(e));
    } finally {
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
      </div>
      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Uploading…' : 'Upload certificate'}
        </button>
      </div>
    </form>
  );
}
