import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';

export default function Backup() {
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
    <section>
      <Title>Backup & Restore - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">Backup & Restore</h1>

      <div class="max-w-xl space-y-6">
        <div class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
          <h2 class="mb-2 text-lg font-medium text-slate-800">Export configuration</h2>
          <p class="mb-4 text-sm text-slate-500">
            Downloads hosts, redirects, streams and access lists as JSON. Certificate private keys are not
            included — back up the container's <code class="rounded bg-slate-100 px-1">/data</code> volume for those.
          </p>
          <button
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
            onClick={() => void exportBackup()}
          >
            {busy() ? 'Working…' : 'Download backup'}
          </button>
        </div>

        <div class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
          <h2 class="mb-2 text-lg font-medium text-slate-800">Restore configuration</h2>
          <p class="mb-4 text-sm text-slate-500">Choose a backup file to replace the current configuration.</p>
          <input type="file" accept="application/json" disabled={busy()} onChange={(e) => void restoreBackup(e)} />
        </div>

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
