import type { ParentProps } from 'solid-js';
import { createEffect, createSignal, Show } from 'solid-js';
import { useLocation, useNavigate } from '@solidjs/router';
import { useAuth } from '../lib/auth';
import GlobalSearch from '../components/GlobalSearch';

const activeNav = 'block rounded-md bg-slate-700 px-3 py-2 text-sm font-medium text-white';
const inactiveNav = 'block rounded-md px-3 py-2 text-sm font-medium text-slate-300 hover:bg-slate-800 hover:text-white';

// A pathless layout: paired with the admin/ directory, every admin page renders inside.
export default function AdminLayout(props: ParentProps) {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchOpen, setSearchOpen] = createSignal(false);

  // In Solid 2's compute/effect split the effect fn receives (next, prev) —
  // next is the compute result itself, so destructure the [ready, session]
  // tuple instead of treating the params as (ready, session).
  createEffect(() => [ auth.ready(), auth.session() ], ([ready, session]) => {
    if (ready && !session) {
      navigate('/login', { replace: true });
    }
  });

  // Ctrl/Cmd+K opens global search.
  createEffect(
    () => [auth.ready(), auth.session()] as const,
    ([ready, session]) => {
      if (!ready || !session) return;
      const onKey = (event: KeyboardEvent) => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
          event.preventDefault();
          setSearchOpen(true);
        }
      };
      window.addEventListener('keydown', onKey);
      return () => window.removeEventListener('keydown', onKey);
    },
  );

  const isActive = (path: string) =>
    location.pathname === path || location.pathname.startsWith(path + '/');

  return (
    <Show when={auth.ready()} fallback={<main class="p-8 text-center text-slate-500">Loading…</main>}>
      <Show when={auth.session()}>
        <div class="flex h-screen overflow-hidden bg-slate-100">
          <aside class="flex w-60 shrink-0 flex-col bg-slate-900 text-slate-100">
            <div class="border-b border-slate-800 px-4 py-4 text-sm font-semibold tracking-wide">
              YARP Proxy Manager
            </div>
            <div class="p-3">
              <button
                class="flex w-full items-center justify-between rounded-md bg-slate-800 px-3 py-2 text-sm text-slate-400 hover:bg-slate-700 hover:text-slate-200"
                onClick={() => setSearchOpen(true)}
              >
                <span>Search…</span>
                <kbd class="rounded border border-slate-600 px-1 text-xs">Ctrl K</kbd>
              </button>
            </div>
            <nav class="flex flex-col gap-1 overflow-y-auto p-3">
              <a href="/admin" class={isActive('/admin') && location.pathname === '/admin' ? activeNav : inactiveNav}>
                Dashboard
              </a>
              <a href="/admin/hosts" class={isActive('/admin/hosts') ? activeNav : inactiveNav}>
                Proxy Hosts
              </a>
              <a href="/admin/redirects" class={isActive('/admin/redirects') ? activeNav : inactiveNav}>
                Redirection Hosts
              </a>
              <a href="/admin/access-lists" class={isActive('/admin/access-lists') ? activeNav : inactiveNav}>
                Access Lists
              </a>
              <a href="/admin/streams" class={isActive('/admin/streams') ? activeNav : inactiveNav}>
                Streams
              </a>
              <a href="/admin/certificates" class={isActive('/admin/certificates') ? activeNav : inactiveNav}>
                SSL Certificates
              </a>
              <a href="/admin/audit" class={isActive('/admin/audit') ? activeNav : inactiveNav}>
                Audit Log
              </a>
              <a href="/admin/diagnostics" class={isActive('/admin/diagnostics') ? activeNav : inactiveNav}>
                Diagnostics
              </a>
              <a href="/admin/settings" class={isActive('/admin/settings') ? activeNav : inactiveNav}>
                Settings
              </a>
              <a href="/admin/users" class={isActive('/admin/users') ? activeNav : inactiveNav}>
                Users
              </a>
            </nav>
            <div class="mt-auto border-t border-slate-800 p-4 text-xs text-slate-400">
              <div class="mb-2 truncate">{auth.session()!.email}</div>
              <button
                class="rounded-md bg-slate-700 px-3 py-1.5 font-medium text-slate-100 hover:bg-slate-600"
                onClick={() => void auth.logout()}
              >
                Log out
              </button>
            </div>
          </aside>
          <main class="flex-1 overflow-y-auto p-8">{props.children}</main>
        </div>
        <GlobalSearch open={searchOpen()} onClose={() => setSearchOpen(false)} />
      </Show>
    </Show>
  );
}
