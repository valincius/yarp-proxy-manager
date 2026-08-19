import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query } from '@solidjs/router';
import { api } from '../../lib/api';
import type { AuditLogDto } from '../../lib/types';

const loadAudit = query(async (): Promise<AuditLogDto[]> => api.get('/audit?limit=200'), 'audit');

export default function Audit() {
  const audit = createMemo(() => loadAudit());

  return (
    <section>
      <Title>Audit Log - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">Audit Log</h1>

      <Show when={audit().length > 0} fallback={<p class="text-sm text-slate-500">No audit entries yet.</p>}>
        <table class="w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
          <thead>
            <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3">Timestamp</th>
              <th class="px-4 py-3">Entity</th>
              <th class="px-4 py-3">Action</th>
              <th class="px-4 py-3">Details</th>
            </tr>
          </thead>
          <tbody>
            <For each={audit()}>
              {(entry) => (
                <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                  <td class="whitespace-nowrap px-4 py-3 text-slate-500">
                    {new Date(entry.timestamp).toLocaleString()}
                  </td>
                  <td class="px-4 py-3 font-medium text-slate-800">{entry.entityType}</td>
                  <td class="px-4 py-3">
                    <span
                      class={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        entry.action === 'Added'
                          ? 'bg-green-100 text-green-700'
                          : entry.action === 'Deleted'
                            ? 'bg-red-100 text-red-700'
                            : 'bg-amber-100 text-amber-700'
                      }`}
                    >
                      {entry.action}
                    </span>
                  </td>
                  <td class="max-w-md truncate px-4 py-3 text-xs text-slate-500" title={entry.details}>
                    {entry.details}
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
