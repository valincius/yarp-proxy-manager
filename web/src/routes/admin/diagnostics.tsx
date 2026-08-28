import { Title } from '@solidjs/meta';
import { createEffect, createMemo, createSignal, For, Show } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api } from '../../lib/api';
import { useToast } from '../../lib/toast';
import { useAuth } from '../../lib/auth';
import type { DiagnosticsOverview, TrafficRow, RecentRequest } from '../../lib/types';

const WINDOWS = [
  { key: '1m', label: 'Last 1 min' },
  { key: '5m', label: 'Last 5 min' },
  { key: '15m', label: 'Last 15 min' },
  { key: 'all', label: 'Since boot' },
] as const;

const REFRESH_MS = 10_000;

const loadOverview = query(
  async (): Promise<DiagnosticsOverview> => api.get('/diagnostics/overview'),
  'diagnostics-overview',
);
const loadTraffic = query(
  async (window: string): Promise<TrafficRow[]> => api.get(`/diagnostics/traffic?window=${window}`),
  'diagnostics-traffic',
);
const loadRequests = query(
  async (): Promise<RecentRequest[]> => api.get('/diagnostics/requests?limit=100'),
  'diagnostics-requests',
);

export default function Diagnostics() {
  const auth = useAuth();
  const toast = useToast();
  const [window, setWindow] = createSignal<'1m' | '5m' | '15m' | 'all'>('5m');
  const isAdmin = () => auth.session()?.roles.includes('Admin') ?? false;

  const overview = createMemo(() => loadOverview());
  const traffic = createMemo(() => loadTraffic(window()));
  const requests = createMemo(() => (isAdmin() ? loadRequests() : []));

  // Live refresh: Solid 2's createEffect takes (compute, effect) — the compute runs
  // once with a stable value and the effect schedules the revalidate interval.
  createEffect(
    () => REFRESH_MS,
    (ms) => {
      const timer = setInterval(() => {
        revalidate('diagnostics-overview');
        revalidate('diagnostics-traffic');
        revalidate('diagnostics-requests');
      }, ms);
      return () => clearInterval(timer);
    },
  );

  function refreshNow() {
    revalidate('diagnostics-overview');
    revalidate('diagnostics-traffic');
    revalidate('diagnostics-requests');
  }

  const capture = () => overview()?.captureEnabled ?? false;
  const captureSize = () => overview()?.captureSize ?? 4096;

  async function toggleCapture() {
    if (!isAdmin()) return;
    try {
      await api.put('/settings/diagnostics', { captureEnabled: !capture(), captureSize: captureSize() });
      toast.push(capture() ? 'Body capture disabled.' : 'Body capture enabled.', 'success');
      refreshNow();
    } catch (e) {
      toast.push(e instanceof Error ? e.message : 'Failed to update capture settings.', 'error');
    }
  }

  const uptime = () => {
    const started = overview()?.startedAt;
    if (!started) return '—';
    const minutes = Math.max(0, Math.floor((Date.now() - new Date(started).getTime()) / 60_000));
    if (minutes < 60) return `${minutes}m`;
    const hours = Math.floor(minutes / 60);
    return `${hours}h ${minutes % 60}m`;
  };

  return (
    <section class="space-y-8">
      <Title>Diagnostics - YARP Proxy Manager</Title>
      <div class="page-header flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">Diagnostics</h1>
        <span class="text-xs text-slate-500">
          Live traffic statistics · refreshes every {REFRESH_MS / 1000}s · <code class="rounded bg-slate-100 px-1">/metrics</code>
        </span>
      </div>

      {/* Overview cards */}
      <Show when={overview()} fallback={<p class="text-sm text-slate-500">Loading…</p>}>
        <div class="grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-6">
          <StatCard label="Uptime" value={uptime()} />
          <StatCard label="Requests (boot)" value={fmt(overview()!.totalRequests)} />
          <StatCard label="Failed" value={fmt(overview()!.totalFailed)} tone={overview()!.totalFailed > 0 ? 'text-red-600' : undefined} />
          <StatCard label="Hosts tracked" value={fmt(overview()!.trackedHosts)} />
          <StatCard label="Routes / clusters" value={`${overview()!.routes} / ${overview()!.clusters}`} />
          <StatCard
            label="Certificates"
            value={`${overview()!.certificates.total}`}
            detail={`${overview()!.certificates.failed} failed · ${overview()!.certificates.expiringSoon} <30d`}
            tone={overview()!.certificates.failed > 0 || overview()!.certificates.expiringSoon > 0 ? 'text-amber-600' : undefined}
          />
        </div>

        <div class="flex flex-wrap items-center gap-4 rounded-lg border border-slate-200 bg-white p-4 text-sm shadow-sm">
          <div class="flex items-center gap-2">
            <span class="text-xs font-medium uppercase tracking-wide text-slate-500">Capture bodies</span>
            <button
              disabled={!isAdmin()}
              class={`relative h-5 w-9 rounded-full transition-colors ${capture() ? 'bg-blue-600' : 'bg-slate-300'} disabled:opacity-50`}
              onClick={() => void toggleCapture()}
              title={isAdmin() ? 'Toggle request/response body capture' : 'Admin only'}
            >
              <span class={`absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-all ${capture() ? 'left-4.5' : 'left-0.5'}`} />
            </button>
            <span class="text-xs text-slate-500">
              {capture() ? `on (≤${captureSize()} bytes each)` : 'off'}
            </span>
          </div>
          <div class="text-xs text-slate-500">
            Traces:{' '}
            <span class={overview()!.traceEndpoint ? 'text-green-600' : 'text-slate-500'}>
              {overview()!.traceEndpoint ? overview()!.traceEndpoint : 'OTLP export disabled'}
            </span>
          </div>
          <div class="text-xs text-slate-500">
            Streams:{' '}
            {overview()!.streams.length === 0
              ? 'none configured'
              : `${overview()!.streams.filter((s) => s.listening).length}/${overview()!.streams.length} listening · ${overview()!.streams.reduce((n, s) => n + s.activeSessions, 0)} sessions`}
          </div>
          <div class="ml-auto text-xs text-slate-500">buffered samples: {fmt(overview()!.bufferedSamples)}</div>
        </div>
      </Show>

      {/* Traffic table */}
      <div>
        <div class="section-header mb-3 flex items-center justify-between">
          <h2 class="text-lg font-medium text-slate-800">Traffic by host</h2>
          <div class="flex gap-1">
            <For each={WINDOWS}>
              {(w) => (
                <button
                  class={`rounded-md px-3 py-1.5 text-xs font-medium ${
                    window() === w.key
                      ? 'bg-blue-600 text-white'
                      : 'border border-slate-300 bg-white text-slate-600 hover:bg-slate-50'
                  }`}
                  onClick={() => setWindow(w.key)}
                >
                  {w.label}
                </button>
              )}
            </For>
          </div>
        </div>
        <Show
          when={traffic() && traffic()!.length > 0}
          fallback={<p class="text-sm text-slate-500">No traffic in this window yet. Send a request to a proxy host.</p>}
        >
          <div class="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
            <table class="responsive-card-table w-full text-sm">
              <thead>
                <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
                  <th class="px-4 py-3">Host</th>
                  <th class="px-4 py-3 text-right">Req</th>
                  <th class="px-4 py-3 text-center">2xx</th>
                  <th class="px-4 py-3 text-center">3xx</th>
                  <th class="px-4 py-3 text-center">4xx</th>
                  <th class="px-4 py-3 text-center">5xx</th>
                  <th class="px-4 py-3 text-right">Avg</th>
                  <th class="px-4 py-3 text-right">p50 / p95 / p99</th>
                  <th class="px-4 py-3 text-right">Bytes in / out</th>
                  <th class="px-4 py-3 text-right">Active</th>
                  <th class="px-4 py-3">Last error</th>
                </tr>
              </thead>
              <tbody>
                <For each={traffic()}>
                  {(row) => (
                    <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                      <td data-label="Host" class="px-4 py-3">
                        <div class="font-medium text-slate-800">{row.host}</div>
                        <Show when={row.hostName && row.hostName !== row.host}>
                          <div class="text-xs text-slate-400">→ {row.hostName}</div>
                        </Show>
                      </td>
                      <td data-label="Requests" class="px-4 py-3 text-right font-medium text-slate-800">{fmt(row.requests)}</td>
                      <td data-label="2xx" class="px-4 py-3 text-center text-green-600">{fmt(row.class2xx)}</td>
                      <td data-label="3xx" class="px-4 py-3 text-center text-slate-500">{fmt(row.class3xx)}</td>
                      <td data-label="4xx" class={`px-4 py-3 text-center ${row.class4xx > 0 ? 'text-amber-600' : 'text-slate-400'}`}>
                        {fmt(row.class4xx)}
                      </td>
                      <td data-label="5xx" class={`px-4 py-3 text-center ${row.class5xx > 0 ? 'text-red-600' : 'text-slate-400'}`}>
                        {fmt(row.class5xx)}
                      </td>
                      <td data-label="Average" class="px-4 py-3 text-right text-slate-600">{row.averageMs.toFixed(1)}ms</td>
                      <td data-label="Percentiles" class="px-4 py-3 text-right text-slate-600">
                        {row.p50Ms.toFixed(1)} / {row.p95Ms.toFixed(1)} / {row.p99Ms.toFixed(1)} ms
                      </td>
                      <td data-label="Bytes" class="px-4 py-3 text-right text-slate-600">
                        {fmt(row.bytesIn)} / {fmt(row.bytesOut)}
                      </td>
                      <td data-label="Active" class="px-4 py-3 text-right text-slate-600">{row.active}</td>
                      <td data-label="Last error" class="max-w-[14rem] truncate px-4 py-3 text-xs text-red-600" title={row.lastError ?? ''}>
                        {row.lastError ?? '—'}
                      </td>
                    </tr>
                  )}
                </For>
              </tbody>
            </table>
          </div>
        </Show>
      </div>

      {/* Recent requests */}
      <div>
        <h2 class="mb-3 text-lg font-medium text-slate-800">Recent requests</h2>
        <Show when={isAdmin()} fallback={<p class="text-sm text-slate-500">Admin access required to view recent requests.</p>}>
          <Show
            when={requests() && requests()!.length > 0}
            fallback={<p class="text-sm text-slate-500">No requests recorded yet.</p>}
          >
            <div class="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
              <table class="responsive-card-table w-full text-sm">
                <thead>
                  <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
                    <th class="px-4 py-3">When</th>
                    <th class="px-4 py-3">Method</th>
                    <th class="px-4 py-3">Host</th>
                    <th class="px-4 py-3">Path</th>
                    <th class="px-4 py-3 text-center">Status</th>
                    <th class="px-4 py-3 text-right">Duration</th>
                    <th class="px-4 py-3 text-right">Bytes in / out</th>
                    <th class="px-4 py-3">Client IP</th>
                  </tr>
                </thead>
                <tbody>
                  <For each={requests()}>
                    {(req) => (
                      <RequestRow req={req} />
                    )}
                  </For>
                </tbody>
              </table>
            </div>
          </Show>
        </Show>
      </div>
    </section>
  );
}

function RequestRow(props: { req: RecentRequest }) {
  const [open, setOpen] = createSignal(false);
  const req = () => props.req;

  return (
    <>
      <tr
        class="cursor-pointer border-b border-slate-100 last:border-0 hover:bg-slate-50"
        onClick={() => setOpen((o) => !o)}
      >
        <td data-label="When" class="whitespace-nowrap px-4 py-3 text-xs text-slate-500">{formatDate(req().timestamp)}</td>
        <td data-label="Method" class="px-4 py-3">
          <code class="rounded bg-slate-100 px-1.5 py-0.5 text-xs font-medium text-slate-700">{req().method}</code>
        </td>
        <td data-label="Host" class="px-4 py-3 text-slate-700">{req().host}</td>
        <td data-label="Path" class="max-w-[20rem] truncate px-4 py-3 text-slate-600" title={req().path}>{req().path}</td>
        <td data-label="Status" class="px-4 py-3 text-center">
          <StatusPill code={req().statusCode} />
        </td>
        <td data-label="Duration" class="px-4 py-3 text-right text-slate-600">{req().durationMs}ms</td>
        <td data-label="Bytes" class="px-4 py-3 text-right text-slate-600">
          {fmt(req().bytesIn)} / {fmt(req().bytesOut)}
        </td>
        <td data-label="Client IP" class="px-4 py-3 text-xs text-slate-500">{req().clientIp ?? '—'}</td>
      </tr>
      <Show when={open()}>
        <tr class="border-b border-slate-100 bg-slate-50">
          <td colspan={8} class="px-4 py-3">
            <div class="space-y-2 text-xs">
              <Show when={req().error}>
                <p class="font-medium text-red-600">Error: {req().error}</p>
              </Show>
              <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
                <Show when={req().requestBody !== null} fallback={<p class="text-slate-400">No request body captured (enable body capture).</p>}>
                  <div>
                    <p class="mb-1 font-medium uppercase tracking-wide text-slate-500">Request body</p>
                    <pre class="max-h-40 overflow-auto whitespace-pre-wrap break-all rounded bg-white p-2 text-slate-700">{req().requestBody}</pre>
                  </div>
                </Show>
                <Show when={req().responseBody !== null} fallback={<p class="text-slate-400">No response body captured (enable body capture).</p>}>
                  <div>
                    <p class="mb-1 font-medium uppercase tracking-wide text-slate-500">Response body</p>
                    <pre class="max-h-40 overflow-auto whitespace-pre-wrap break-all rounded bg-white p-2 text-slate-700">{req().responseBody}</pre>
                  </div>
                </Show>
              </div>
            </div>
          </td>
        </tr>
      </Show>
    </>
  );
}

function StatusPill(props: { code: number }) {
  const tone =
    props.code >= 500 ? 'bg-red-100 text-red-700'
      : props.code >= 400 ? 'bg-amber-100 text-amber-700'
      : props.code >= 300 ? 'bg-sky-100 text-sky-700'
      : props.code >= 200 ? 'bg-green-100 text-green-700'
      : 'bg-slate-100 text-slate-600';
  return <span class={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${tone}`}>{props.code}</span>;
}

function StatCard(props: { label: string; value: string; detail?: string; tone?: string }) {
  return (
    <div class="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
      <div class="text-xs font-medium uppercase tracking-wide text-slate-500">{props.label}</div>
      <div class={`mt-1 text-2xl font-semibold text-slate-800 ${props.tone ?? ''}`}>{props.value}</div>
      <Show when={props.detail}>
        <div class="mt-1 text-xs text-slate-500">{props.detail}</div>
      </Show>
    </div>
  );
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function fmt(n: number): string {
  return n.toLocaleString();
}
