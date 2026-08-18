# YARP Proxy Manager

A self-hosted, database-backed reverse proxy manager in the spirit of Nginx Proxy Manager — built on **YARP** (ASP.NET Core / Kestrel) with a **SolidJS 2** admin UI. One Docker container owns ports 80/443 (proxy) and 81 (admin); proxy configuration is stored in SQLite and hot-reloaded with no restarts.

## Status

- **Phase 0 — Scaffold (done):** solution layout, pinned dependencies, YARP-on-net10 smoke test, frontend moved to `web/` with Tailwind v4, CI workflow.
- **Phase 1 — Core proxy MVP (in progress):** entities, EF Core + SQLite, config projection into YARP, Identity auth, hosts API + UI.

## Layout

```
src/
├── ProxyManager.Domain/          Entities, enums — no dependencies
├── ProxyManager.Application/     Use-cases, DTOs, validation (FluentValidation)
├── ProxyManager.Infrastructure/  EF Core DbContext, migrations, audit interceptor, Cloudflare DNS client
├── ProxyManager.Proxy/           YARP config projection, middleware (redirects, access lists, exploits)
├── ProxyManager.Certificates/    ACME (Certes) service, SNI certificate selector, renewal worker
├── ProxyManager.Streams/         TCP/UDP forwarder subsystem (YARP is HTTP-only)
└── ProxyManager.Api/             ASP.NET host: Kestrel endpoints, controllers, Identity, static UI
web/                              SolidJS 2 + Tailwind v4 admin UI (Vite build → dist/client)
tests/ProxyManager.Tests/         xUnit smoke/integration/unit tests
```

## Development

Backend (dev ports 5080/5443/5081 — no admin rights needed):

```bash
dotnet run --project src/ProxyManager.Api
```

Frontend (Vite on http://localhost:3000, `/api` proxied to the backend):

```bash
cd web && pnpm install && pnpm dev
```

Tests / CI:

```bash
dotnet test ProxyManager.slnx
cd web && pnpm test -- --run && pnpm build
```

## Deployment

Docker (Phase 1 adds the Dockerfile + compose): container owns 80/443/81, data volume at `/data` (SQLite, certificates, Data Protection keys, logs).
