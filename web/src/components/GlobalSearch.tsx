import { createEffect, createMemo, createSignal, For, Show } from 'solid-js';
import { query } from '@solidjs/router';
import { api } from '../lib/api';
import Modal from './Modal';
import type { AccessList, ProxyHost, RedirectHost } from '../lib/types';

// Reuse the same cache keys as the entity pages so results appear instantly.
// (Streams gets its own key: the page's 'streams' entry caches { streams, statuses }.)
const loadHosts = query(async (): Promise<ProxyHost[]> => api.get('/hosts'), 'hosts-list');
const loadRedirects = query(async (): Promise<RedirectHost[]> => api.get('/redirects'), 'redirects');
const loadStreams = query(
  async (): Promise<
    { id: string; name: string; protocol: string; forwardHost: string; forwardPort: number; listenPort: number }[]
  > => api.get('/streams'),
  'search-streams',
);
const loadAccessLists = query(async (): Promise<AccessList[]> => api.get('/access-lists'), 'access-lists');

interface SearchResult {
  type: 'Proxy host' | 'Redirect' | 'Stream' | 'Access list';
  id: string;
  title: string;
  subtitle: string;
  href: string;
}

export default function GlobalSearch(props: { open: boolean; onClose: () => void }) {
  const [term, setTerm] = createSignal('');

  // Reset the query each time the modal opens.
  createEffect(
    () => props.open,
    (open) => {
      if (open) setTerm('');
    },
  );

  const hosts = createMemo<ProxyHost[]>(() => loadHosts());
  const redirects = createMemo<RedirectHost[]>(() => loadRedirects());
  const streams = createMemo<
    { id: string; name: string; protocol: string; forwardHost: string; forwardPort: number; listenPort: number }[]
  >(() => loadStreams());
  const accessLists = createMemo<AccessList[]>(() => loadAccessLists());

  const results = createMemo<SearchResult[]>(() => {
    const needle = term().trim().toLowerCase();
    if (needle.length === 0) return [];

    const matches = (value: string) => value.toLowerCase().includes(needle);
    const found: SearchResult[] = [];

    // Snapshot all collections up front: in this TS/solid-js setup, reading
    // async memo accessors again after the first loop confuses inference, so
    // materialize them as plain arrays before any iteration.
    const hostList = hosts();
    const redirectList = redirects();
    const streamList = streams();
    const accessListArray = accessLists();

    for (const host of hostList) {
      if (
        matches(host.name) ||
        host.domainNames.some(matches) ||
        matches(host.forwardHost) ||
        host.forwardHost.split('.').some((part) => matches(part))
      ) {
        found.push({
          type: 'Proxy host',
          id: host.id,
          title: host.name,
          subtitle: `${host.domainNames.join(', ')} → ${host.scheme}://${host.forwardHost}:${host.forwardPort}`,
          href: `/admin/hosts/${host.id}`,
        });
      }
    }
    for (const redirect of redirectList) {
      if (matches(redirect.name) || redirect.domainNames.some(matches) || matches(redirect.forwardHost)) {
        found.push({
          type: 'Redirect',
          id: redirect.id,
          title: redirect.name,
          subtitle: `${redirect.domainNames.join(', ')} → ${redirect.forwardScheme}://${redirect.forwardHost}:${redirect.forwardPort}`,
          href: `/admin/redirects`,
        });
      }
    }
    for (const stream of streamList) {
      if (matches(stream.name) || matches(stream.forwardHost) || String(stream.listenPort).includes(needle)) {
        found.push({
          type: 'Stream',
          id: stream.id,
          title: stream.name,
          subtitle: `${stream.protocol ?? 'TCP'} :${stream.listenPort} → ${stream.forwardHost}:${stream.forwardPort}`,
          href: `/admin/streams`,
        });
      }
    }
    for (const list of accessListArray) {
      if (matches(list.name) || list.rules.some((rule) => matches(rule.pattern))) {
        found.push({
          type: 'Access list',
          id: list.id,
          title: list.name,
          subtitle: `${list.rules.length} rule${list.rules.length === 1 ? '' : 's'} · ${
            list.satisfyAny ? 'Satisfy Any' : 'Satisfy All'
          }`,
          href: `/admin/access-lists`,
        });
      }
    }
    return found.slice(0, 50);
  });

  return (
    <Modal open={props.open} title="Search" onClose={props.onClose} size="max-w-xl">
      <div class="space-y-4">
        <input
          autofocus
          type="search"
          class="w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
          placeholder="Search names, domains, IPs, ports…"
          value={term()}
          onInput={(e) => setTerm(e.currentTarget.value)}
        />
        <Show
          when={results().length > 0}
          fallback={
            <p class="text-sm text-slate-500">
              {term().trim().length === 0 ? 'Type to search across hosts, redirects, streams and access lists.' : 'No matches.'}
            </p>
          }
        >
          <ul class="max-h-96 divide-y divide-slate-100 overflow-y-auto rounded-md border border-slate-200">
            <For each={results()}>
              {(result) => (
                <li>
                  <a href={result.href} class="block px-3 py-2 hover:bg-slate-50" onClick={props.onClose}>
                    <div class="flex items-center justify-between gap-2">
                      <span class="font-medium text-slate-800">{result.title}</span>
                      <span class="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        {result.type}
                      </span>
                    </div>
                    <p class="mt-0.5 truncate text-xs text-slate-500">{result.subtitle}</p>
                  </a>
                </li>
              )}
            </For>
          </ul>
        </Show>
      </div>
    </Modal>
  );
}
