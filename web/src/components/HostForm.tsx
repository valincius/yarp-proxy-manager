import { createSignal, createMemo, For, Show, type ParentProps } from 'solid-js';
import { query } from '@solidjs/router';
import { api, ApiError } from '../lib/api';
import type {
  AccessList,
  CertificateDto,
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

interface HostFormProps {
  initial?: ProxyHost;
  submitLabel: string;
  onSubmit: (input: ProxyHostInput) => Promise<void>;
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
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

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
      await props.onSubmit(input);
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
        <Field label="Certificate (for HTTPS)" hint="Served for this host's domains via SNI.">
          <select class={inputClass} value={certificateId()} onChange={(e) => setCertificateId(e.currentTarget.value)}>
            <option value="">— none —</option>
            <For each={certificates()}>
              {(certificate) => (
                <option value={certificate.id} disabled={certificate.status !== 'Issued'}>
                  {certificate.name} ({certificate.domains.join(', ')})
                </option>
              )}
            </For>
          </select>
        </Field>
        <Field label="Access List" hint="Allow/deny rules enforced before proxying.">
          <select class={inputClass} value={accessListId()} onChange={(e) => setAccessListId(e.currentTarget.value)}>
            <option value="">— none —</option>
            <For each={accessLists()}>
              {(list) => <option value={list.id}>{list.name}</option>}
            </For>
          </select>
        </Field>
      </div>

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
