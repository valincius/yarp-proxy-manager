# YARP Proxy Manager — admin web UI

The admin UI for [YARP Proxy Manager](../README.md): a SolidJS 2 SPA (Tailwind CSS v4, Vite) that talks to the ASP.NET admin API under `/api/v1` on the admin port.

## Commands

```bash
pnpm install     # first time
pnpm dev         # Vite dev server on http://localhost:3000 (/api proxied to http://localhost:5081)
pnpm typecheck   # tsc --noEmit
pnpm test        # vitest (component tests in jsdom)
pnpm build       # static build → dist/client (what the backend serves)
```

## Structure

- `src/App.tsx` — app root: auth provider + router. `src/Document.tsx` — the document shell (prerendered into `dist/client/index.html`).
- `src/routes/` — file-system routes (`admin/*` pages, `login`, `[...404]`); `src/routes/admin.tsx` is the pathless layout wrapping the admin pages.
- `src/components/` — shared UI (`HostForm`, `Modal`, `GlobalSearch`, `StatusBadge`, …).
- `src/lib/` — API client (`api.ts`), auth/session context (`auth.tsx`), toast store (`toast.tsx`), shared types.
- `*.test.tsx` / `*.test.ts` — vitest component tests next to the code they cover (see `host-form.test.tsx` for the fetch-mock pattern).

## Notes

- The API requires an antiforgery cookie round-trip for mutating requests from browser sessions; the client fetches the `XSRF-TOKEN` after any auth change and echoes it as `X-XSRF-TOKEN`. API-key calls skip antiforgery.
- `query()` (from `@solidjs/router`) caches server data per key; pages call `revalidate` after mutations so lists stay fresh without a full reload.
