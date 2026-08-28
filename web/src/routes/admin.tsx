import type { ParentProps } from 'solid-js';
import { createEffect, createSignal, Show } from 'solid-js';
import { useLocation, useNavigate } from '@solidjs/router';
import { useAuth } from '../lib/auth';
import GlobalSearch from '../components/GlobalSearch';
import ErrorBoundary from '../components/ErrorBoundary';

const activeNav = 'block rounded-md bg-slate-700 px-3 py-2 text-sm font-medium text-white';
const inactiveNav = 'block rounded-md px-3 py-2 text-sm font-medium text-slate-300 hover:bg-slate-800 hover:text-white';

// A pathless layout: paired with the admin/ directory, every admin page renders inside.
export default function AdminLayout(props: ParentProps) {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchOpen, setSearchOpen] = createSignal(false);
  const [mobileNavOpen, setMobileNavOpen] = createSignal(false);

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

  const pageLabel = (path: string) => {
    if (path.startsWith('/admin/hosts')) return 'Proxy hosts';
    if (path.startsWith('/admin/redirects')) return 'Redirection hosts';
    if (path.startsWith('/admin/access-lists')) return 'Access lists';
    if (path.startsWith('/admin/streams')) return 'Streams';
    if (path.startsWith('/admin/certificates')) return 'SSL certificates';
    if (path.startsWith('/admin/audit')) return 'Audit log';
    if (path.startsWith('/admin/diagnostics')) return 'Diagnostics';
    if (path.startsWith('/admin/settings')) return 'Settings';
    if (path.startsWith('/admin/users')) return 'Users';
    return 'Dashboard';
  };

  return (
    <Show when={auth.ready()} fallback={<main class="p-8 text-center text-slate-500">Loading…</main>}>
      <Show when={auth.session()}>
        <div class="admin-shell flex h-screen overflow-hidden bg-slate-100">
          <aside class="admin-sidebar flex w-60 shrink-0 flex-col bg-slate-900 text-slate-100">
            <div class="admin-brand flex items-center justify-between border-b border-slate-800 px-4 py-4 text-sm font-semibold tracking-wide">
              <span>YARP Proxy Manager</span>
              <button
                type="button"
                class="admin-menu-button rounded-md p-2 text-slate-300 hover:bg-slate-800 hover:text-white"
                aria-controls="admin-navigation"
                aria-expanded={mobileNavOpen() ? 'true' : 'false'}
                aria-label={mobileNavOpen() ? 'Close navigation' : 'Open navigation'}
                onClick={() => void setMobileNavOpen((open) => !open)}
              >
                <Show when={mobileNavOpen()} fallback={<span aria-hidden="true">☰</span>}>
                  <span aria-hidden="true">×</span>
                </Show>
              </button>
            </div>
            <div class={`admin-search p-3 ${mobileNavOpen() ? '' : 'mobile-menu-hidden'}`}>
              <button
                class="flex w-full items-center justify-between rounded-md bg-slate-800 px-3 py-2 text-sm text-slate-400 hover:bg-slate-700 hover:text-slate-200"
                onClick={() => setSearchOpen(true)}
              >
                <span>Search…</span>
                <kbd class="rounded border border-slate-600 px-1 text-xs">Ctrl K</kbd>
              </button>
            </div>
            <nav id="admin-navigation" class={`admin-nav flex flex-col gap-1 overflow-y-auto p-3 ${mobileNavOpen() ? '' : 'mobile-menu-hidden'}`}>
              <a href="/admin" class={isActive('/admin') && location.pathname === '/admin' ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Dashboard
              </a>
              <a href="/admin/hosts" class={isActive('/admin/hosts') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Proxy Hosts
              </a>
              <a href="/admin/redirects" class={isActive('/admin/redirects') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Redirection Hosts
              </a>
              <a href="/admin/access-lists" class={isActive('/admin/access-lists') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Access Lists
              </a>
              <a href="/admin/streams" class={isActive('/admin/streams') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Streams
              </a>
              <a href="/admin/certificates" class={isActive('/admin/certificates') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                SSL Certificates
              </a>
              <a href="/admin/audit" class={isActive('/admin/audit') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Audit Log
              </a>
              <a href="/admin/diagnostics" class={isActive('/admin/diagnostics') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Diagnostics
              </a>
              <a href="/admin/settings" class={isActive('/admin/settings') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Settings
              </a>
              <a href="/admin/users" class={isActive('/admin/users') ? activeNav : inactiveNav} onClick={() => setMobileNavOpen(false)}>
                Users
              </a>
            </nav>
            <div class={`admin-footer mt-auto border-t border-slate-800 p-4 text-xs text-slate-400 ${mobileNavOpen() ? '' : 'mobile-menu-hidden'}`}>
              <div class="mb-2 truncate">{auth.session()!.email}</div>
              <button
                class="rounded-md bg-slate-700 px-3 py-1.5 font-medium text-slate-100 hover:bg-slate-600"
                onClick={() => void auth.logout()}
              >
                Log out
              </button>
            </div>
          </aside>
          <main class="admin-main min-w-0 flex-1 overflow-y-auto p-8">
            <ErrorBoundary label={pageLabel(location.pathname)}>{props.children}</ErrorBoundary>
          </main>
        </div>
        <GlobalSearch open={searchOpen()} onClose={() => setSearchOpen(false)} />
      </Show>
    </Show>
  );
}
