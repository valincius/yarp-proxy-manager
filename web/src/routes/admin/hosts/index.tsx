import { Title } from '@solidjs/meta';
import { query, revalidate } from '@solidjs/router';
import { createMemo, For, Show } from 'solid-js';
import { api } from '../../../lib/api';
import { StatusBadge } from '../../../components/StatusBadge';
import { DeleteButton, EditButton, PowerButton } from '../../../components/ActionButtons';
import type { ProxyHost } from '../../../lib/types';

const loadHosts = query(async (): Promise<ProxyHost[]> => api.get('/hosts'), 'hosts-list');

export default function Hosts() {
  const hosts = createMemo(() => loadHosts());

  async function remove(host: ProxyHost) {
    if (!confirm(`Delete proxy host "${host.name}"? This cannot be undone.`)) {
      return;
    }
    await api.del(`/hosts/${host.id}`);
    revalidate('hosts-list');
    revalidate('dashboard');
  }

  async function toggle(host: ProxyHost) {
    await api.patch(`/hosts/${host.id}/enable`, { enabled: !host.enabled });
    revalidate('hosts-list');
    revalidate('dashboard');
  }

  return (
    <section>
      <Title>Proxy Hosts - YARP Proxy Manager</Title>
      <div class="mb-6 flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">Proxy Hosts</h1>
        <a
          href="/admin/hosts/new"
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
        >
          + New Host
        </a>
      </div>

      <Show when={hosts().length > 0} fallback={<p class="text-sm text-slate-500">No proxy hosts yet.</p>}>
        <table class="w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
          <thead>
            <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3">Name</th>
              <th class="px-4 py-3">Domains</th>
              <th class="px-4 py-3">Destination</th>
              <th class="px-4 py-3">Status</th>
              <th class="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            <For each={hosts()}>
              {(host) => (
                <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                  <td class="px-4 py-3 font-medium text-slate-800">{host.name}</td>
                  <td class="px-4 py-3 text-slate-600">{host.domainNames.join(', ')}</td>
                  <td class="px-4 py-3 text-slate-600">
                    {host.scheme}://{host.forwardHost}:{host.forwardPort}
                  </td>
                  <td class="px-4 py-3">
                    <StatusBadge enabled={host.enabled} />
                  </td>
                  <td class="px-4 py-3">
                    <div class="flex items-center justify-end gap-1">
                      <PowerButton enabled={host.enabled} onClick={() => void toggle(host)} />
                      <EditButton href={`/admin/hosts/${host.id}`} />
                      <DeleteButton onClick={() => void remove(host)} />
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
