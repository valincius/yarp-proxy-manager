import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import Modal from '../../components/Modal';
import type { StreamInput } from '../../lib/types';

interface StreamDto {
  id: string;
  name: string;
  enabled: boolean;
  protocol: 'Tcp' | 'Udp';
  listenPort: number;
  forwardHost: string;
  forwardPort: number;
  createdAt: string;
  updatedAt: string;
}

interface StreamStatusDto {
  listening: boolean;
  error: string | null;
  activeSessions: number;
  bytesIn: number;
  bytesOut: number;
  updatedAt: string;
}

const loadStreams = query(
  async (): Promise<{ streams: StreamDto[]; statuses: Record<string, StreamStatusDto> }> => {
    const [streams, statuses] = await Promise.all([
      api.get<StreamDto[]>('/streams'),
      api.get<Record<string, StreamStatusDto>>('/streams/status'),
    ]);
    return { streams, statuses };
  },
  'streams',
);

const inputClass =
  'mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500';

function toMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors : [error.message];
  }
  return [error instanceof Error ? error.message : 'An unexpected error occurred.'];
}

export default function Streams() {
  const data = createMemo(() => loadStreams());
  const [editing, setEditing] = createSignal<StreamDto | null>(null);
  const [showForm, setShowForm] = createSignal(false);

  async function remove(stream: StreamDto) {
    if (!confirm(`Delete stream "${stream.name}"?`)) {
      return;
    }
    await api.del(`/streams/${stream.id}`);
    revalidate('streams');
  }

  return (
    <section>
      <Title>Streams - YARP Proxy Manager</Title>
      <div class="mb-6 flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">Streams</h1>
        <button
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          onClick={() => {
            setEditing(null);
            setShowForm(true);
          }}
        >
          + New Stream
        </button>
      </div>

      <Modal
        open={showForm()}
        title={editing() ? `Edit stream "${editing()!.name}"` : 'New stream'}
        onClose={() => setShowForm(false)}
      >
        <StreamForm
          initial={editing() ?? undefined}
          onDone={() => {
            setShowForm(false);
            revalidate('streams');
          }}
        />
      </Modal>

      <Show when={data().streams.length > 0} fallback={<p class="text-sm text-slate-500">No streams yet. Streams forward raw TCP/UDP traffic.</p>}>
        <table class="w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
          <thead>
            <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3">Name</th>
              <th class="px-4 py-3">Protocol</th>
              <th class="px-4 py-3">Listen</th>
              <th class="px-4 py-3">Forward to</th>
              <th class="px-4 py-3">Status</th>
              <th class="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            <For each={data().streams}>
              {(stream) => {
                const status = () => data().statuses[stream.id];
                return (
                  <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                    <td class="px-4 py-3 font-medium text-slate-800">{stream.name}</td>
                    <td class="px-4 py-3 text-slate-600">{stream.protocol}</td>
                    <td class="px-4 py-3 text-slate-600">:{stream.listenPort}</td>
                    <td class="px-4 py-3 text-slate-600">
                      {stream.forwardHost}:{stream.forwardPort}
                    </td>
                    <td class="px-4 py-3">
                      <StreamBadge enabled={stream.enabled} status={status()} />
                    </td>
                    <td class="px-4 py-3">
                      <div class="flex items-center justify-end gap-2">
                        <button
                          class="text-xs font-medium text-blue-600 hover:text-blue-700"
                          onClick={() => {
                            setEditing(stream);
                            setShowForm(true);
                          }}
                        >
                          Edit
                        </button>
                        <button class="text-xs font-medium text-red-600 hover:text-red-700" onClick={() => void remove(stream)}>
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              }}
            </For>
          </tbody>
        </table>
      </Show>
    </section>
  );
}

function StreamBadge(props: { enabled: boolean; status: StreamStatusDto | undefined }) {
  if (!props.enabled) {
    return <span class="inline-flex rounded-full bg-slate-200 px-2 py-0.5 text-xs font-medium text-slate-600">Disabled</span>;
  }
  if (props.status?.error) {
    return (
      <span class="inline-flex rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700" title={props.status.error}>
        Error
      </span>
    );
  }
  return props.status?.listening ? (
    <span class="inline-flex rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">
      Listening · {props.status.activeSessions} sessions
    </span>
  ) : (
    <span class="inline-flex rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">Starting…</span>
  );
}

function StreamForm(props: { initial?: StreamDto; onDone: () => void }) {
  const [name, setName] = createSignal(props.initial?.name ?? '');
  const [protocol, setProtocol] = createSignal(props.initial?.protocol ?? 'Tcp');
  const [listenPort, setListenPort] = createSignal(props.initial?.listenPort ?? 0);
  const [forwardHost, setForwardHost] = createSignal(props.initial?.forwardHost ?? '');
  const [forwardPort, setForwardPort] = createSignal(props.initial?.forwardPort ?? 80);
  const [enabled, setEnabled] = createSignal(props.initial?.enabled ?? true);
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const input: StreamInput = {
      name: name(),
      enabled: enabled(),
      protocol: protocol() as 'Tcp' | 'Udp',
      listenPort: Number(listenPort()),
      forwardHost: forwardHost(),
      forwardPort: Number(forwardPort()),
    };
    try {
      if (props.initial) {
        await api.put(`/streams/${props.initial.id}`, input);
      } else {
        await api.post('/streams', input);
      }
      props.onDone();
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
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Protocol</span>
          <select class={inputClass} value={protocol()} onChange={(e) => setProtocol(e.currentTarget.value as 'Tcp' | 'Udp')}>
            <option value="Tcp">TCP</option>
            <option value="Udp">UDP</option>
          </select>
          <span class="mt-1 block text-xs text-slate-500">Raw byte forwarding — not HTTP proxying.</span>
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Listen port</span>
          <input class={inputClass} type="number" min={1} max={65535} value={listenPort()} onInput={(e) => setListenPort(Number(e.currentTarget.value))} required />
          <span class="mt-1 block text-xs text-slate-500">Must not collide with the proxy or admin ports.</span>
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Forward hostname / IP</span>
          <input class={inputClass} value={forwardHost()} onInput={(e) => setForwardHost(e.currentTarget.value)} placeholder="10.0.0.30" required />
        </label>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Forward port</span>
          <input class={inputClass} type="number" min={1} max={65535} value={forwardPort()} onInput={(e) => setForwardPort(Number(e.currentTarget.value))} required />
        </label>
        <label class="flex items-end gap-2 pb-2 text-sm text-slate-700">
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
          {busy() ? 'Saving…' : props.initial ? 'Save changes' : 'Create stream'}
        </button>
        <button type="button" class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={props.onDone}>
          Cancel
        </button>
      </div>
    </form>
  );
}
