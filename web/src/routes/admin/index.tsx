import { Title } from '@solidjs/meta';
import { query, type RouteDefinition } from '@solidjs/router';
import { createMemo, For, Show } from 'solid-js';
import { api } from '../../lib/api';
import { StatusBadge } from '../../components/StatusBadge';
import type { ProxyHost } from '../../lib/types';

interface DashboardData {
  hosts: ProxyHost[];
  routes: number;
  clusters: number;
}

const loadDashboard = query(async (): Promise<DashboardData> => {
  const [hosts, health] = await Promise.all([
    api.get<ProxyHost[]>('/hosts'),
    api.get<{ routes: number; clusters: number }>('/health'),
  ]);
  return { hosts, routes: health.routes, clusters: health.clusters };
}, 'dashboard');

export const route = {
  preload: () => void loadDashboard(),
} satisfies RouteDefinition;

export default function Dashboard() {
  const data = createMemo(() => loadDashboard());
  const enabledCount = () => data().hosts.filter((h) => h.enabled).length;

  return (
    <section>
      <Title>Dashboard - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">Dashboard</h1>

      <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard label="Proxy Hosts" value={data().hosts.length} detail={`${enabledCount()} enabled`} />
        <StatCard label="YARP Routes" value={data().routes} detail="loaded in memory" />
        <StatCard label="YARP Clusters" value={data().clusters} detail="loaded in memory" />
      </div>

      <div class="mt-8">
        <div class="mb-3 flex items-center justify-between">
          <h2 class="text-lg font-medium text-slate-800">Recently Accessed</h2>
          <a href="/admin/hosts" class="text-sm font-medium text-blue-600 hover:text-blue-700">
            Manage hosts →
          </a>
        </div>
        <Show
          when={data().hosts.length > 0}
          fallback={<p class="text-sm text-slate-500">No proxy hosts yet. Create your first host.</p>}
        >
          <table class="w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
            <thead>
              <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
                <th class="px-4 py-3">Name</th>
                <th class="px-4 py-3">Domains</th>
                <th class="px-4 py-3">Destination</th>
                <th class="px-4 py-3">Status</th>
              </tr>
            </thead>
            <tbody>
              <For each={data().hosts.slice(0, 5)}>
                {(host) => (
                  <tr class="border-b border-slate-100 last:border-0">
                    <td class="px-4 py-3 font-medium text-slate-800">{host.name}</td>
                    <td class="px-4 py-3 text-slate-600">{host.domainNames.join(', ')}</td>
                    <td class="px-4 py-3 text-slate-600">
                      {host.scheme}://{host.forwardHost}:{host.forwardPort}
                    </td>
                    <td class="px-4 py-3">
                      <StatusBadge enabled={host.enabled} />
                    </td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>
        </Show>
      </div>
    </section>
  );
}

function StatCard(props: { label: string; value: number; detail: string }) {
  return (
    <div class="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
      <div class="text-xs font-medium uppercase tracking-wide text-slate-500">{props.label}</div>
      <div class="mt-1 text-3xl font-semibold text-slate-800">{props.value}</div>
      <div class="mt-1 text-xs text-slate-500">{props.detail}</div>
    </div>
  );
}
