# Implementation Plan — Stage 1 & Stage 2

> **Status: all items implemented and verified (2026-08-19).** See the commit log
> and README for where each piece landed. Verification: `dotnet test` 85/85,
> web typecheck + build + tests 6/6, app boot + API smoke test, in-Docker E2E
> for autodiscovery, live benchmark run.

Work is tracked against the current goal. Each item lists the affected files and the
approach. This document is the plan; implementation follows it item by item with
verification (web `tsc`/`build`, `dotnet build`, `dotnet test`) at the end of each stage.

## Stage 1 — UI/UX & public API

| # | Item | Approach | Files |
|---|------|----------|-------|
| S1.1 | Fix host edit crash (`[PENDING_ASYNC_UNTRACKED_READ]` in `HostForm`) | The edit page passes a *pending* async `query()` value into `HostForm`, whose `createSignal` initializers read it in an untracked scope. Wrap the form in `<Loading fallback={…}>` (Solid 2's boundary for pending async reads) so the pending value suspends instead of being read untracked; the form only mounts once resolved. | `web/src/routes/admin/hosts/[id].tsx` |
| S1.2 | Redirects / Access Lists / Streams inline editing → modals | Extract a reusable `Modal` component; convert the "form above the table" pattern on all three pages to open in a centered modal. | `web/src/components/Modal.tsx` (new), `admin/redirects.tsx`, `admin/access-lists.tsx`, `admin/streams.tsx` |
| S1.3 | Inline create of certs & access lists from the host form | Add "+ New" buttons next to the certificate/access-list selects in `HostForm` that open a modal. The cert modal pre-populates the domains field from the host form's domain input. On success, select the newly created entity. | `web/src/components/HostForm.tsx`, `web/src/components/Modal.tsx` |
| S1.4 | Error boundaries + error toasts | Add a global toast store (`lib/toast.tsx`) with a context + viewport. Add `createErrorBoundary`/`<Errored>` wrappers around page content and data-heavy components; surface errors as toasts. | `web/src/lib/toast.tsx` (new), `web/src/App.tsx`, `web/src/routes/admin.tsx` |
| S1.5 | Public REST API + API-key auth + docs | Reuse the existing `api/v1/*` controllers. Add an API-key store (hashed keys, `ApiKey` entity + management UI), a `X-Api-Key` authentication handler that runs alongside cookie auth, and ensure all proxy-entity endpoints (hosts, redirects, access-lists, streams, certificates, dns-credentials, acme-settings) are reachable with a key. Antiforgery applies to cookie sessions only, not API keys. Write agentic-accessible REST docs. | Backend: `ApiKey` entity/migration, `ApiKeyAuthHandler`, `ApiKeysController`, `Program.cs` auth wiring. Web: `admin/api-keys.tsx`. Docs: `docs/API.md` + README |
| S1.6 | README update (WIP warning + examples + stream proxy explanation) | Add a prominent WIP/security disclaimer, quick-start example, stream proxy use cases, API summary, links to docs. | `README.md`, `docs/API.md` |
| S1.7 | Certificates page layout + settings page | New `/admin/settings` page holding non-proxy configuration. Move the ACME account settings section there (certificates section). Keep issue/upload/credentials + list on the certificates page. | `web/src/routes/admin/settings.tsx` (new), `admin/certificates.tsx`, `web/src/routes/admin.tsx` (nav) |
| S1.8 | Certificates: modals with read view | Replace the inline editable sections (issue/upload/credentials/settings) with modal flows, and add a read-only detail view for a certificate (domains, provider, validity, renewal info). | `web/src/routes/admin/certificates.tsx`, `web/src/components/Modal.tsx` |
| S1.9 | Redirects "(path)" display | `(path)` was indicating `preservePath`. Replace with an explicit badge/column ("preserves path") so the URL column shows only the redirect target. | `web/src/routes/admin/redirects.tsx` |
| S1.10 | Access lists as expandable table | Convert the cards grid to a table; each row expands (collapsed by default) to show rules. | `web/src/routes/admin/access-lists.tsx` |
| S1.11 | Global search (names, domains, IPs) | Global search entry (header/sidebar button) opening a centered modal; searches hosts/redirects/streams/access-list patterns client-side over the loaded collections, grouped by type, with links. | `web/src/components/GlobalSearch.tsx` (new), `web/src/routes/admin.tsx` |
| S1.12 | Sidebar fits viewport | Change layout from `min-h-screen` (stretches with content) to fixed `h-screen` with `overflow-y-auto` on the content column. | `web/src/routes/admin.tsx` |
| S1.13 | "Recent Proxy Hosts" → "Recently Accessed" | Rename the dashboard section heading. | `web/src/routes/admin/index.tsx` |

## Stage 2 — Deep features

| # | Item | Approach |
|---|------|----------|
| S2.1 | 404 page management | Settings option (embedded default / empty / uploaded HTML with `{{placeholder}}` string replacement). Store the template in the DB or data dir; proxy port serves it for unmatched hosts on the proxy pipeline. UI on the settings page. **Done** — `Setting` key/value store, `NotFoundPageMiddleware` on the proxy port, `/settings/not-found` endpoints, Settings UI with live preview. |
| S2.2 | Helper text | Add `hint` text to form fields that lack it (redirects, streams, access lists, users, certs, settings). **Done** — hints added across hosts, redirects, streams, access lists, certificates, API keys, settings, users. |
| S2.3 | Docker container-tag integration | A watcher that connects to the Docker socket/API, reads container labels (e.g. `proxy-manager.enable=true`, `host=…`, `port=…`, `scheme=…`), creates/disposes proxy hosts automatically (traefik-style). Config toggle + status UI. **Done** — `DockerHostSyncService` + 15s `DockerSyncWorker`, managed-host lifecycle (`ManagedBy`/`ManagedSource`), `/settings/docker` endpoints, Settings UI with status + "Sync now", verified end-to-end in Docker (discovery → live proxy → disposal). |
| S2.4 | Benchmarking vs nginx/npm | Add `benchmarks/` harness (docker-compose with nginx + YARP + npm, bombardier/wrk scripts) and a results/methodology doc modeled on the referenced YARP-vs-nginx article. **Done** — k6-based docker harness, `run.ps1` runner, results + methodology in `benchmarks/README.md`; sample run recorded. |

## Verification

- `cd web && pnpm typecheck && pnpm build`
- `dotnet build ProxyManager.slnx`
- `dotnet test ProxyManager.slnx`
- Boot the app; exercise the changed UI flows.
