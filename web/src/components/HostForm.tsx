import { createSignal, createMemo, For, Show, type ParentProps } from 'solid-js';
import { query } from '@solidjs/router';
import { api, ApiError } from '../lib/api';
import type { CertificateDto, ProxyHost, ProxyHostInput } from '../lib/types';

const loadCertificates = query(
  async (): Promise<CertificateDto[]> => api.get('/certificates'),
  'host-form-certs',
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
  const certificates = createMemo(() => loadCertificates());
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
      accessListId: null,
      requestHeaders: [],
      responseHeaders: [],
      locations: [],
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

      <Field label="Certificate (for HTTPS)" hint="The certificate served for this host's domains via SNI.">
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

      <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Toggle label="Enabled" checked={enabled()} onChange={setEnabled} />
        <Toggle label="WebSockets" checked={webSockets()} onChange={setWebSockets} />
        <Toggle label="Block Common Exploits" checked={blockExploits()} onChange={setBlockExploits} />
        <Toggle label="Force HTTPS (requires a certificate)" checked={forceHttps()} onChange={setForceHttps} />
        <Toggle label="HTTP/2" checked={http2()} onChange={setHttp2} />
      </div>

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
