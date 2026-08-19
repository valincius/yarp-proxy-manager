# YARP Proxy Manager

A self-hosted, database-backed reverse proxy manager in the spirit of Nginx Proxy Manager — built on **YARP** (ASP.NET Core / Kestrel) with a **SolidJS 2** admin UI. One Docker container owns ports 80/443 (proxy) and 81 (admin); proxy configuration is stored in SQLite and hot-reloaded with no restarts.

> ⚠️ **WIP — not production-ready.** This project is under active development. It is **not** hardened, audited, or performance-tuned, and should **not be trusted as secure** or used to serve production traffic yet. Default credentials, incomplete edge cases, and unvetted defaults are all possible. Use it on an isolated network only.

## What it does

- **Proxy hosts** — map hostnames to HTTP(S) destinations, with WebSockets, custom request/response headers, custom locations (path-prefix routing), HTTP/2, Force-HTTPS, and per-host SNI certificate selection.
- **Load balancing** — multiple destinations per host with round-robin / least-requests / random / power-of-two-choices / first policies and active health checks.
- **Redirection hosts** — 301/302 redirects with optional path/query preservation.
- **Access lists** — allow/deny rules (IP, CIDR, `*`) with satisfy-any/satisfy-all semantics, enforced before proxying.
- **Exploit blocking** — NPM-style common-exploit filtering.
- **Certificates** — per-host ACME certificates (Let's Encrypt, HTTP-01 or Cloudflare DNS-01), manual PFX/PEM upload, automatic renewal, Kestrel SNI selection.
- **Streams** — raw **TCP/UDP** port forwarding for non-HTTP protocols (see [What is the stream proxy for?](#what-is-the-stream-proxy-for)).
- **Ops** — Prometheus metrics, JSON backup/restore, immutable audit log, user management, OIDC SSO login (optional), and a **public REST API** (see [docs/API.md](docs/API.md)).

## Quick start

```bash
cd docker && docker compose up -d
```

The container owns port 80 (reverse proxy) and 81 (admin UI/API); the data volume at `./data` holds the SQLite database, certificates, Data Protection keys and logs. The default admin account is `admin@example.com` / `changeme` — **set `Admin__Password` (or `ADMIN_PASSWORD`) before first boot** and change it immediately.

Example flow:

1. Open `http://your-host:81`, log in, go to **Proxy Hosts → + New Host**.
2. Enter `app.example.com` as the domain and point it at `10.0.0.25:8080`.
3. Requests to `http://app.example.com` (port 80) are proxied to the destination instantly — no restart.
4. Add a certificate for the domain (**SSL Certificates**) to serve HTTPS with automatic renewal.

## What is the stream proxy for?

YARP proxies **HTTP**, which covers web apps, APIs and WebSockets. The **streams** feature handles everything else — raw byte forwarding of TCP or UDP:

- **Databases** (MySQL/PostgreSQL/Redis/MongoDB) that you don't want to expose directly to the internet.
- **SSH** — forward port 22 to an internal box.
- **Custom TCP protocols** (game servers, legacy line protocols, MQTT without TLS).
- **UDP services** (DNS forwarding, syslog, NTP, game voice).

Configure a stream with a listen port, protocol and destination; the manager validates port conflicts against the proxy ports and reports per-stream status (sessions, bytes in/out) in the UI.

## Docker autodiscovery (traefik-style labels)

Run the manager with the Docker engine socket mounted (`/var/run/docker.sock`) and enable **Docker integration** on the Settings page. Containers opt in with labels; the manager creates a proxy host for each and disposes it again when the container disappears:

```yaml
services:
  my-app:
    image: my-app:latest
    labels:
      proxy-manager.enable: "true"
      proxy-manager.host: "app.example.com"
      proxy-manager.port: "8080"          # container port
      proxy-manager.scheme: "http"        # optional: http | https
      proxy-manager.name: "My App"        # optional display name
```

Put the manager and the published containers on a shared network so the proxy can reach the container IPs. See `benchmarks/` for the load-test harness and `docs/API.md` for the settings endpoints.

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
docs/                             Implementation plan, REST API reference
```

## Status

- **Phase 0 — Scaffold (done):** solution layout, pinned dependencies, YARP-on-net10 smoke test, frontend moved to `web/` with Tailwind v4, CI workflow.
- **Phase 1 — Core proxy MVP (done):** entities, EF Core + SQLite, dynamic config projection into YARP (hot reload, no restarts), Identity auth, hosts API, Solid 2 admin UI (login + dashboard + hosts CRUD), Docker packaging. Verified end-to-end: create a host in the UI → traffic flows through the proxy port instantly.
- **Phase 2 — Certificates (done):** per-host ACME certificates via Certes (HTTP-01 + Cloudflare DNS-01), manual PFX/PEM upload, Kestrel SNI selection on 443, automatic renewal worker, ForceHTTPS redirects, certificates + DNS credentials + ACME settings UI.
- **Phase 3 — NPM parity (done):** redirect hosts (301/302 with path preservation), access lists (allow/deny IP/CIDR with satisfy-any semantics), NPM-style exploit blocking, custom request/response headers and custom locations in the host form, an immutable audit log of config changes, and user management (create/disable/reset-password, admin-only).
- **Phase 4 — Streams (done):** TCP/UDP forwarding subsystem with per-stream listeners, runtime status (sessions/bytes), port-conflict validation against the proxy ports, and a streams UI.
- **Phase 5 — Ops (done):** load balancing (multiple destinations, round-robin/least-requests/random policies, active health checks), Prometheus metrics on the admin port, optional OIDC SSO login, and JSON backup/restore.
- **Stage 1 — UX + public API (done):** modal-based editing across all pages, inline certificate/access-list creation from the host form, error boundaries + toasts, global search (Ctrl+K), a Settings page, a read-view for certificates, a public REST API with API-key authentication ([docs/API.md](docs/API.md)), and API-key management UI.
- **Stage 2 — Deep features (done):** custom 404 page management (built-in / empty / uploaded HTML with `{{host}}`-style templating), helper text across forms, Docker container-label autodiscovery (traefik-style, verified end-to-end in Docker), and a k6-based benchmark harness comparing YARP vs tuned nginx vs optional NPM ([benchmarks/](benchmarks/README.md)). See [docs/PLAN.md](docs/PLAN.md).

## REST API

The admin UI runs on the same JSON API exposed under `/api/v1` on the admin port. Programmatic access uses API keys (`X-Api-Key` header) that can manage every proxy entity — hosts, redirects, access lists, streams, certificates — but not users, backups or API keys themselves. Full reference with examples: **[docs/API.md](docs/API.md)**.
