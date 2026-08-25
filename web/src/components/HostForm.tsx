import { createSignal, createMemo, For, Show, type ParentProps } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../lib/api';
import Modal from './Modal';
import { useToast } from '../lib/toast';
import type {
  AccessList,
  CertificateDto,
  DnsCredentialDto,
  ProxyDestinationInput,
  ProxyHeaderInput,
  ProxyHost,
  ProxyHostInput,
  ProxyLocationInput,
} from '../lib/types';

const loadCertificates = query(
  async (): Promise<CertificateDto[]> => api.get('/certificates'),
  'host-form-certs',
);

const loadAccessLists = query(
  async (): Promise<AccessList[]> => api.get('/access-lists'),
  'host-form-access-lists',
);

const loadDnsCredentials = query(
  async (): Promise<DnsCredentialDto[]> => api.get('/dns-credentials'),
  'host-form-dns-credentials',
);

interface HostFormProps {
  initial?: ProxyHost;
  submitLabel: string;
  /** Create mode only: shows the auto-request-certificate checkbox. */
  isCreate?: boolean;
  onSubmit: (input: ProxyHostInput, options: HostSubmitOptions) => Promise<void>;
}

export interface HostSubmitOptions {
  /** Request a certificate for the host's domains after saving (create mode). */
  autoCert: boolean;
  /** DNS credential for DNS-01 when the host has wildcard domains. */
  dnsCredentialId: string | null;
}

const inputClass =
  'mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500';

function Field(props: { label: string; hint?: string; children: ParentProps['children'] }) {
  return (
    <label class="block">
      <span class="text-sm font-medium text-slate-700">{props.label}</span>
      {props.children}
      {props.hint ? <span class="mt-1 block text-xs text-slate-500">{props.hint}</span> : null}
    </label>
  );
}

/**
 * Like <see cref="Field"/> but renders a plain <div> instead of a <label>.
 * Used for controls that contain interactive children (select + button):
 * nesting a button inside a <label> makes the browser forward the click to
 * the select, so the button becomes unclickable.
 */
function FieldControl(props: { label: string; hint?: string; children: ParentProps['children'] }) {
  return (
    <div class="block">
      <span class="text-sm font-medium text-slate-700">{props.label}</span>
      {props.children}
      {props.hint ? <span class="mt-1 block text-xs text-slate-500">{props.hint}</span> : null}
    </div>
  );
}

function Toggle(props: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label class="flex cursor-pointer items-center gap-2">
      <input
        type="checkbox"
        class="h-4 w-4 rounded border-slate-300 text-blue-600 focus:ring-blue-500"
        checked={props.checked}
        onChange={(e) => props.onChange(e.currentTarget.checked)}
      />
      <span class="text-sm text-slate-700">{props.label}</span>
    </label>
  );
}

export default function HostForm(props: HostFormProps) {
  const toast = useToast();
  const [name, setName] = createSignal(props.initial?.name ?? '');
  const [domains, setDomains] = createSignal(props.initial?.domainNames.join(', ') ?? '');
  const [enabled, setEnabled] = createSignal(props.initial?.enabled ?? true);
  const [scheme, setScheme] = createSignal(props.initial?.scheme ?? 'http');
  const [forwardHost, setForwardHost] = createSignal(props.initial?.forwardHost ?? '');
  const [forwardPort, setForwardPort] = createSignal(props.initial?.forwardPort ?? 80);
  const [webSockets, setWebSockets] = createSignal(props.initial?.webSocketsEnabled ?? true);
  const [blockExploits, setBlockExploits] = createSignal(props.initial?.blockCommonExploits ?? true);
  const [forceHttps, setForceHttps] = createSignal(props.initial?.forceHttps ?? false);
  const [http2, setHttp2] = createSignal(props.initial?.http2Support ?? true);
  const [certificateId, setCertificateId] = createSignal(props.initial?.certificateId ?? '');
  const [accessListId, setAccessListId] = createSignal(props.initial?.accessListId ?? '');
  const [requestHeaders, setRequestHeaders] = createSignal<ProxyHeaderInput[]>(
    props.initial?.requestHeaders.map(toHeaderInput) ?? [],
  );
  const [responseHeaders, setResponseHeaders] = createSignal<ProxyHeaderInput[]>(
    props.initial?.responseHeaders.map(toHeaderInput) ?? [],
  );
  const [locations, setLocations] = createSignal<ProxyLocationInput[]>(
    props.initial?.locations.map(toLocationInput) ?? [],
  );
  const [destinations, setDestinations] = createSignal<ProxyDestinationInput[]>(
    props.initial?.destinations.length
      ? props.initial.destinations.map((d) => ({ forwardHost: d.forwardHost, forwardPort: d.forwardPort }))
      : props.initial
        ? [{ forwardHost: props.initial.forwardHost, forwardPort: props.initial.forwardPort }]
        : [{ forwardHost: '', forwardPort: 80 }],
  );
  const [loadBalancingPolicy, setLoadBalancingPolicy] = createSignal(props.initial?.loadBalancingPolicy ?? '');
  const [healthCheckEnabled, setHealthCheckEnabled] = createSignal(props.initial?.healthCheckEnabled ?? false);
  const [healthCheckPath, setHealthCheckPath] = createSignal(props.initial?.healthCheckPath ?? '/health');
  const [healthCheckInterval, setHealthCheckInterval] = createSignal(props.initial?.healthCheckIntervalSeconds ?? 10);
  const certificates = createMemo(() => loadCertificates());
  const accessLists = createMemo(() => loadAccessLists());
  const dnsCredentials = createMemo(() => loadDnsCredentials());
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  // Auto-request a certificate on host creation.
  const [autoCert, setAutoCert] = createSignal(false);
  const [dnsCredentialId, setDnsCredentialId] = createSignal('');
  const hasWildcard = createMemo(() =>
    domains()
      .split(',')
      .map((d) => d.trim())
      .some((d) => d.startsWith('*.')),
  );

  // The certificate/access-list options load asynchronously, so a `value={...}`
  // binding on the select would be applied before the options exist and never
  // re-applied. Instead, mark the matching <option> as `selected` — evaluated
  // when each option renders, so the attached certificate/list shows correctly.
  const [showCertModal, setShowCertModal] = createSignal(false);
  const [showAccessListModal, setShowAccessListModal] = createSignal(false);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    if (autoCert() && hasWildcard() && !dnsCredentialId()) {
      setError(['Wildcard domains require DNS-01 — select a DNS credential or uncheck auto-request.']);
      setBusy(false);
      return;
    }

    const input: ProxyHostInput = {
      name: name(),
      domainNames: domains()
        .split(',')
        .map((d) => d.trim())
        .filter(Boolean),
      enabled: enabled(),
      scheme: scheme() as 'http' | 'https',
      forwardHost: forwardHost(),
      forwardPort: Number(forwardPort()),
      webSocketsEnabled: webSockets(),
      blockCommonExploits: blockExploits(),
      forceHttps: forceHttps(),
      http2Support: http2(),
      certificateId: certificateId() || null,
      accessListId: accessListId() || null,
      requestHeaders: requestHeaders().filter((h) => h.name.trim().length > 0),
      responseHeaders: responseHeaders().filter((h) => h.name.trim().length > 0),
      locations: locations().filter((l) => l.pathPrefix.trim().length > 0),
      destinations: destinations().filter((d) => d.forwardHost.trim().length > 0),
      loadBalancingPolicy: loadBalancingPolicy() || null,
      healthCheckEnabled: healthCheckEnabled(),
      healthCheckPath: healthCheckPath() || null,
      healthCheckIntervalSeconds: Number(healthCheckInterval()),
    };

    try {
      await props.onSubmit(input, { autoCert: autoCert(), dnsCredentialId: dnsCredentialId() || null });
      toast.push(props.initial ? 'Host updated.' : 'Host created.', 'success');
    } catch (e) {
      setError(
        e instanceof ApiError
          ? e.errors && e.errors.length > 0
            ? e.errors
            : [e.message]
          : ['An unexpected error occurred.'],
      );
      setBusy(false);
    }
  }

  return (
    <>
      <form class="max-w-2xl space-y-5 rounded-lg border border-slate-200 bg-white p-6 shadow-sm" onSubmit={submit}>
        <Show when={error()}>
          <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            <ul class="list-disc space-y-1 pl-5">
              <For each={error()!}>{(message) => <li>{message}</li>}</For>
            </ul>
          </div>
        </Show>

        <Field label="Name">
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} placeholder="My app" required />
        </Field>

        <Field label="Domain Names" hint="Comma-separated. Wildcards allowed, e.g. *.example.com.">
          <input class={inputClass} value={domains()} onInput={(e) => setDomains(e.currentTarget.value)} placeholder="app.example.com" required />
        </Field>

        <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="Scheme">
            <select class={inputClass} value={scheme()} onChange={(e) => setScheme(e.currentTarget.value as 'http' | 'https')}>
              <option value="http">http</option>
              <option value="https">https</option>
            </select>
          </Field>
          <Field label="Forward Hostname / IP">
            <input class={inputClass} value={forwardHost()} onInput={(e) => setForwardHost(e.currentTarget.value)} placeholder="10.0.0.25" required />
          </Field>
          <Field label="Forward Port">
            <input class={inputClass} type="number" min={1} max={65535} value={forwardPort()} onInput={(e) => setForwardPort(Number(e.currentTarget.value))} required />
          </Field>
        </div>

        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <FieldControl label="Certificate (for HTTPS)" hint="Served for this host's domains via SNI.">
            <div class="mt-1 flex gap-2">
              <select class="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500" onChange={(e) => setCertificateId(e.currentTarget.value)}>
                <option value="">— none —</option>
                <Show when={certificates()} fallback={<option disabled>Loading certificates…</option>}>
                  <For each={certificates()!}>
                    {(certificate) => (
                      <option
                        value={certificate.id}
                        selected={certificate.id === certificateId()}
                        disabled={certificate.status !== 'Issued' && certificate.id !== certificateId()}
                      >
                        {certificate.name} ({certificate.domains.join(', ')}){certificate.status !== 'Issued' ? ` — ${certificate.status}` : ''}
                      </option>
                    )}
                  </For>
                </Show>
              </select>
              <button
                type="button"
                class="shrink-0 rounded-md border border-slate-300 px-3 py-2 text-xs font-medium text-blue-600 hover:bg-blue-50"
                onClick={() => setShowCertModal(true)}
              >
                + New
              </button>
            </div>
          </FieldControl>
          <FieldControl label="Access List" hint="Allow/deny rules enforced before proxying.">
            <div class="mt-1 flex gap-2">
              <select class="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500" onChange={(e) => setAccessListId(e.currentTarget.value)}>
                <option value="">— none —</option>
                <Show when={accessLists()} fallback={<option disabled>Loading access lists…</option>}>
                  <For each={accessLists()!}>
                    {(list) => <option value={list.id} selected={list.id === accessListId()}>{list.name}</option>}
                  </For>
                </Show>
              </select>
              <button
                type="button"
                class="shrink-0 rounded-md border border-slate-300 px-3 py-2 text-xs font-medium text-blue-600 hover:bg-blue-50"
                onClick={() => setShowAccessListModal(true)}
              >
                + New
              </button>
            </div>
          </FieldControl>
        </div>

        <Show when={props.isCreate}>
          <div class="space-y-3 rounded-lg border border-slate-200 p-4">
            <Toggle
              label="Request a certificate for these domains after creating the host"
              checked={autoCert()}
              onChange={setAutoCert}
            />
            <Show when={autoCert()}>
              <p class="text-xs text-slate-500">
                {hasWildcard()
                  ? 'Wildcard domains require DNS-01 — choose the DNS credential used to publish TXT records.'
                  : 'Uses HTTP-01 (port 80) — the proxy answers the ACME challenge automatically. Takes up to a minute.'}
              </p>
              <Show when={hasWildcard()}>
                <label class="block">
                  <span class="text-sm font-medium text-slate-700">DNS credential</span>
                  <select class={inputClass} value={dnsCredentialId()} onChange={(e) => setDnsCredentialId(e.currentTarget.value)} required>
                    <option value="">Select credential…</option>
                    <Show when={dnsCredentials()} fallback={<option disabled>Loading credentials…</option>}>
                      <For each={dnsCredentials()!}>
                        {(credential) => <option value={credential.id}>{credential.name} ({credential.provider})</option>}
                      </For>
                    </Show>
                  </select>
                </label>
              </Show>
            </Show>
          </div>
        </Show>

        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Toggle label="Enabled" checked={enabled()} onChange={setEnabled} />
          <Toggle label="WebSockets" checked={webSockets()} onChange={setWebSockets} />
          <Toggle label="Block Common Exploits" checked={blockExploits()} onChange={setBlockExploits} />
          <Toggle label="Force HTTPS (requires a certificate)" checked={forceHttps()} onChange={setForceHttps} />
          <Toggle label="HTTP/2" checked={http2()} onChange={setHttp2} />
        </div>

        <HeaderEditor title="Custom Request Headers" headers={requestHeaders} setHeaders={setRequestHeaders} />
        <HeaderEditor title="Custom Response Headers" headers={responseHeaders} setHeaders={setResponseHeaders} />
        <LocationEditor locations={locations} setLocations={setLocations} />
        <DestinationEditor
          destinations={destinations}
          setDestinations={setDestinations}
          loadBalancingPolicy={loadBalancingPolicy}
          setLoadBalancingPolicy={setLoadBalancingPolicy}
          healthCheckEnabled={healthCheckEnabled}
          setHealthCheckEnabled={setHealthCheckEnabled}
          healthCheckPath={healthCheckPath}
          setHealthCheckPath={setHealthCheckPath}
          healthCheckInterval={healthCheckInterval}
          setHealthCheckInterval={setHealthCheckInterval}
        />

        <div class="flex items-center gap-3 pt-2">
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
          >
            {busy() ? 'Saving…' : props.submitLabel}
          </button>
        </div>
      </form>

      <Modal open={showCertModal()} title="Request a new certificate" onClose={() => setShowCertModal(false)}>
        <CertificateCreateModal
          suggestedDomains={domains()}
          onDone={(certificate) => {
            setShowCertModal(false);
            setCertificateId(certificate.id);
            toast.push(`Certificate "${certificate.name}" requested.`, 'success');
          }}
        />
      </Modal>

      <Modal open={showAccessListModal()} title="New access list" onClose={() => setShowAccessListModal(false)}>
        <AccessListCreateModal
          onDone={(list) => {
            setShowAccessListModal(false);
            setAccessListId(list.id);
            toast.push(`Access list "${list.name}" created.`, 'success');
          }}
        />
      </Modal>
    </>
  );
}

function CertificateCreateModal(props: {
  suggestedDomains: string;
  onDone: (certificate: CertificateDto) => void;
}) {
  const [name, setName] = createSignal('');
  const [domains, setDomains] = createSignal(props.suggestedDomains);
  const [challengeType, setChallengeType] = createSignal<'Http01' | 'Dns01'>('Http01');
  const [dnsCredentialId, setDnsCredentialId] = createSignal('');
  const dnsCredentials = createMemo(() => loadDnsCredentials());
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
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
      // Surface the new certificate in this form's dropdown and on the
      // certificates page without requiring a page refresh.
      revalidate('host-form-certs');
      revalidate('certificates');
      props.onDone(certificate);
    } catch (e) {
      setError(
        e instanceof ApiError
          ? e.errors && e.errors.length > 0
            ? e.errors
            : [e.message]
          : ['An unexpected error occurred.'],
      );
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
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} placeholder="My app cert" required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Domains</span>
          <input class={inputClass} value={domains()} onInput={(e) => setDomains(e.currentTarget.value)} placeholder="app.example.com, *.example.com" required />
          <span class="mt-1 block text-xs text-slate-500">Pre-filled from the host's domains — adjust if needed.</span>
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
              <For each={dnsCredentials()}>
                {(credential) => <option value={credential.id}>{credential.name} ({credential.provider})</option>}
              </For>
            </select>
            <span class="mt-1 block text-xs text-slate-500">Add one on the Settings → Certificates page if none exist.</span>
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

interface RuleRow {
  action: 'Allow' | 'Deny';
  pattern: string;
}

function AccessListCreateModal(props: { onDone: (list: AccessList) => void }) {
  const [name, setName] = createSignal('');
  const [satisfyAny, setSatisfyAny] = createSignal(true);
  const [rules, setRules] = createSignal<RuleRow[]>([{ action: 'Allow', pattern: '*' }]);
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  function updateRule(index: number, patch: Partial<RuleRow>) {
    setRules((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const list = await api.post<AccessList>('/access-lists', {
        name: name(),
        satisfyAny: satisfyAny(),
        rules: rules().filter((r) => r.pattern.trim().length > 0),
      });
      // Surface the new list in this form's dropdown and on the access lists
      // page without requiring a page refresh.
      revalidate('host-form-access-lists');
      revalidate('access-lists');
      props.onDone(list);
    } catch (e) {
      setError(
        e instanceof ApiError
          ? e.errors && e.errors.length > 0
            ? e.errors
            : [e.message]
          : ['An unexpected error occurred.'],
      );
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
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} required />
        </label>
        <label class="flex items-end gap-2 pb-2 text-sm text-slate-700">
          <input type="checkbox" class="h-4 w-4 rounded border-slate-300" checked={satisfyAny()} onChange={(e) => setSatisfyAny(e.currentTarget.checked)} />
          Satisfy Any (allow if any rule matches)
        </label>
      </div>
      <div>
        <div class="mb-2 flex items-center justify-between">
          <span class="text-sm font-medium text-slate-700">Rules</span>
          <button
            type="button"
            class="text-xs font-medium text-blue-600 hover:text-blue-700"
            onClick={() => setRules((rows) => [...rows, { action: 'Allow', pattern: '' }])}
          >
            + Add rule
          </button>
        </div>
        <div class="space-y-2">
          <For each={rules()}>
            {(rule, index) => (
              <div class="flex items-center gap-2">
                <select
                  class="w-28 rounded-md border border-slate-300 px-2 py-2 text-sm"
                  value={rule.action}
                  onChange={(e) => updateRule(index(), { action: e.currentTarget.value as 'Allow' | 'Deny' })}
                >
                  <option value="Allow">Allow</option>
                  <option value="Deny">Deny</option>
                </select>
                <input
                  class="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  placeholder="IP, CIDR, or *"
                  value={rule.pattern}
                  onInput={(e) => updateRule(index(), { pattern: e.currentTarget.value })}
                />
                <button
                  type="button"
                  class="text-xs font-medium text-red-600 hover:text-red-700"
                  onClick={() => setRules((rows) => rows.filter((_, i) => i !== index()))}
                >
                  Remove
                </button>
              </div>
            )}
          </For>
        </div>
      </div>
      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Creating…' : 'Create access list'}
        </button>
        <span class="text-xs text-slate-500">The list is applied to this host once saved.</span>
      </div>
    </form>
  );
}

function HeaderEditor(props: {
  title: string;
  headers: () => ProxyHeaderInput[];
  setHeaders: (v: ProxyHeaderInput[]) => void;
}) {
  function update(index: number, patch: Partial<ProxyHeaderInput>) {
    props.setHeaders(props.headers().map((h, i) => (i === index ? { ...h, ...patch } : h)));
  }

  return (
    <div class="rounded-lg border border-slate-200 p-4">
      <div class="mb-2 flex items-center justify-between">
        <span class="text-sm font-medium text-slate-700">{props.title}</span>
        <button
          type="button"
          class="text-xs font-medium text-blue-600 hover:text-blue-700"
          onClick={() => props.setHeaders([...props.headers(), { target: 'Request', action: 'Set', name: '', value: '' }])}
        >
          + Add header
        </button>
      </div>
      <Show when={props.headers().length > 0} fallback={<p class="text-xs text-slate-400">None.</p>}>
        <div class="space-y-2">
          <For each={props.headers()}>
            {(header, index) => (
              <div class="flex items-center gap-2">
                <select
                  class="w-24 rounded-md border border-slate-300 px-2 py-2 text-sm"
                  value={header.action}
                  onChange={(e) => update(index(), { action: e.currentTarget.value as ProxyHeaderInput['action'] })}
                >
                  <option value="Set">Set</option>
                  <option value="Append">Append</option>
                  <option value="Remove">Remove</option>
                </select>
                <input
                  class="w-40 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  placeholder="Header name"
                  value={header.name}
                  onInput={(e) => update(index(), { name: e.currentTarget.value })}
                />
                <input
                  class="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  placeholder="Value"
                  value={header.value}
                  onInput={(e) => update(index(), { value: e.currentTarget.value })}
                />
                <button
                  type="button"
                  class="text-xs font-medium text-red-600 hover:text-red-700"
                  onClick={() => props.setHeaders(props.headers().filter((_, i) => i !== index()))}
                >
                  Remove
                </button>
              </div>
            )}
          </For>
        </div>
      </Show>
    </div>
  );
}

function LocationEditor(props: {
  locations: () => ProxyLocationInput[];
  setLocations: (v: ProxyLocationInput[]) => void;
}) {
  function update(index: number, patch: Partial<ProxyLocationInput>) {
    props.setLocations(props.locations().map((l, i) => (i === index ? { ...l, ...patch } : l)));
  }

  return (
    <div class="rounded-lg border border-slate-200 p-4">
      <div class="mb-2 flex items-center justify-between">
        <span class="text-sm font-medium text-slate-700">Custom Locations</span>
        <button
          type="button"
          class="text-xs font-medium text-blue-600 hover:text-blue-700"
          onClick={() =>
            props.setLocations([
              ...props.locations(),
              { pathPrefix: '/', stripPrefix: true, scheme: 'http', forwardHost: '', forwardPort: 80, order: props.locations().length },
            ])
          }
        >
          + Add location
        </button>
      </div>
      <Show when={props.locations().length > 0} fallback={<p class="text-xs text-slate-400">None — the whole host is forwarded to the destination above.</p>}>
        <div class="space-y-2">
          <For each={props.locations()}>
            {(location, index) => (
              <div class="flex items-center gap-2">
                <input
                  class="w-28 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  placeholder="/api"
                  value={location.pathPrefix}
                  onInput={(e) => update(index(), { pathPrefix: e.currentTarget.value })}
                />
                <select
                  class="w-24 rounded-md border border-slate-300 px-2 py-2 text-sm"
                  value={location.scheme}
                  onChange={(e) => update(index(), { scheme: e.currentTarget.value as 'http' | 'https' })}
                >
                  <option value="http">http</option>
                  <option value="https">https</option>
                </select>
                <input
                  class="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  placeholder="Forward host"
                  value={location.forwardHost}
                  onInput={(e) => update(index(), { forwardHost: e.currentTarget.value })}
                />
                <input
                  class="w-20 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  type="number"
                  min={1}
                  max={65535}
                  value={location.forwardPort}
                  onInput={(e) => update(index(), { forwardPort: Number(e.currentTarget.value) })}
                />
                <label class="flex items-center gap-1 text-xs text-slate-600">
                  <input
                    type="checkbox"
                    class="h-4 w-4 rounded border-slate-300"
                    checked={location.stripPrefix}
                    onChange={(e) => update(index(), { stripPrefix: e.currentTarget.checked })}
                  />
                  strip
                </label>
                <button
                  type="button"
                  class="text-xs font-medium text-red-600 hover:text-red-700"
                  onClick={() => props.setLocations(props.locations().filter((_, i) => i !== index()))}
                >
                  Remove
                </button>
              </div>
            )}
          </For>
        </div>
      </Show>
    </div>
  );
}

function toHeaderInput(h: { target: string; action: string; name: string; value: string }): ProxyHeaderInput {
  return { target: h.target as ProxyHeaderInput['target'], action: h.action as ProxyHeaderInput['action'], name: h.name, value: h.value };
}

function toLocationInput(l: {
  pathPrefix: string;
  stripPrefix: boolean;
  scheme: string;
  forwardHost: string;
  forwardPort: number;
  order: number;
}): ProxyLocationInput {
  return {
    pathPrefix: l.pathPrefix,
    stripPrefix: l.stripPrefix,
    scheme: l.scheme as 'http' | 'https',
    forwardHost: l.forwardHost,
    forwardPort: l.forwardPort,
    order: l.order,
  };
}

function DestinationEditor(props: {
  destinations: () => ProxyDestinationInput[];
  setDestinations: (v: ProxyDestinationInput[]) => void;
  loadBalancingPolicy: () => string;
  setLoadBalancingPolicy: (v: string) => void;
  healthCheckEnabled: () => boolean;
  setHealthCheckEnabled: (v: boolean) => void;
  healthCheckPath: () => string;
  setHealthCheckPath: (v: string) => void;
  healthCheckInterval: () => number;
  setHealthCheckInterval: (v: number) => void;
}) {
  function update(index: number, patch: Partial<ProxyDestinationInput>) {
    props.setDestinations(props.destinations().map((d, i) => (i === index ? { ...d, ...patch } : d)));
  }

  return (
    <div class="rounded-lg border border-slate-200 p-4">
      <div class="mb-2 flex items-center justify-between">
        <span class="text-sm font-medium text-slate-700">Destinations (load balancing)</span>
        <button
          type="button"
          class="text-xs font-medium text-blue-600 hover:text-blue-700"
          onClick={() => props.setDestinations([...props.destinations(), { forwardHost: '', forwardPort: 80 }])}
        >
          + Add destination
        </button>
      </div>
      <Show when={props.destinations().length > 0} fallback={<p class="text-xs text-slate-400">None.</p>}>
        <div class="space-y-2">
          <For each={props.destinations()}>
            {(destination, index) => (
              <div class="flex items-center gap-2">
                <input
                  class="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  placeholder="Forward host"
                  value={destination.forwardHost}
                  onInput={(e) => update(index(), { forwardHost: e.currentTarget.value })}
                />
                <input
                  class="w-24 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  type="number"
                  min={1}
                  max={65535}
                  value={destination.forwardPort}
                  onInput={(e) => update(index(), { forwardPort: Number(e.currentTarget.value) })}
                />
                <button
                  type="button"
                  class="text-xs font-medium text-red-600 hover:text-red-700"
                  onClick={() => props.setDestinations(props.destinations().filter((_, i) => i !== index()))}
                >
                  Remove
                </button>
              </div>
            )}
          </For>
        </div>
      </Show>
      <div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Load-balancing policy</span>
          <select
            class="mt-1 w-full rounded-md border border-slate-300 px-2 py-2 text-sm"
            value={props.loadBalancingPolicy()}
            onChange={(e) => props.setLoadBalancingPolicy(e.currentTarget.value)}
          >
            <option value="">Default (round robin)</option>
            <option value="roundrobin">Round Robin</option>
            <option value="leastrequests">Least Requests</option>
            <option value="random">Random</option>
            <option value="poweroftwochoices">Power of Two Choices</option>
            <option value="first">First</option>
          </select>
        </label>
        <label class="flex items-end gap-2 pb-2 text-sm text-slate-700">
          <input
            type="checkbox"
            class="h-4 w-4 rounded border-slate-300"
            checked={props.healthCheckEnabled()}
            onChange={(e) => props.setHealthCheckEnabled(e.currentTarget.checked)}
          />
          Active health checks
        </label>
        <Show when={props.healthCheckEnabled()}>
          <div class="flex gap-2">
            <label class="block flex-1">
              <span class="text-sm font-medium text-slate-700">Path</span>
              <input
                class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm"
                value={props.healthCheckPath()}
                onInput={(e) => props.setHealthCheckPath(e.currentTarget.value)}
              />
            </label>
            <label class="block w-28">
              <span class="text-sm font-medium text-slate-700">Interval (s)</span>
              <input
                class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm"
                type="number"
                min={1}
                value={props.healthCheckInterval()}
                onInput={(e) => props.setHealthCheckInterval(Number(e.currentTarget.value))}
              />
            </label>
          </div>
        </Show>
      </div>
    </div>
  );
}
