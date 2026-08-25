import { Title } from '@solidjs/meta';
import type { RouteDefinition } from '@solidjs/router';
import { useLocation, useNavigate } from '@solidjs/router';
import { httpStatus } from '@solidjs/web';

// The catch-all route. httpStatus() is a no-op in the browser and takes
// effect when SSR is enabled; it runs in preload so the status code is set
// before the response head flushes.
export const route = {
  preload: () => httpStatus(404),
} satisfies RouteDefinition;

// Routes that moved into the Settings hub; keep old bookmarks working.
const MOVED_ROUTES: Record<string, string> = {
  '/admin/backup': '/admin/settings#backup',
  '/admin/api-keys': '/admin/settings#api-keys',
};

export default function NotFound() {
  const location = useLocation();
  const navigate = useNavigate();

  // One-shot side effect: redirect on mount. Reads no signals, so under
  // Solid 2's compute/effect split it must be called directly rather than
  // wrapped in createEffect.
  const target = MOVED_ROUTES[location.pathname];
  if (target) {
    navigate(target, { replace: true });
    return null;
  }

  return (
    <main>
      <Title>Not Found - Solid App</Title>
      <h1>Page Not Found</h1>
      <p>
        Visit{' '}
        <a href="https://docs.solidjs.com" target="_blank" rel="noreferrer">
          docs.solidjs.com
        </a>{' '}
        to learn how to build Solid apps.
      </p>
    </main>
  );
}
