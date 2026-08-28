# Development

## Run locally

Start the backend on the development ports (`5080`/`5443` for proxy traffic and `5081` for the admin API):

```bash
dotnet run --project src/ProxyManager.Api
```

Start the frontend in another terminal. Vite runs on `http://localhost:3000` and proxies `/api` to the backend:

```bash
cd web
pnpm install
pnpm dev
```

## Tests and builds

```bash
dotnet test ProxyManager.slnx

cd web
pnpm typecheck
pnpm test -- --run
pnpm test:visual
pnpm build
```

The visual suite uses Playwright to cover the desktop dashboard, collapsed mobile navigation, the open mobile menu, and mobile proxy-host cards. Baselines live under `web/tests/visual/__snapshots__/`.

## Project structure

```text
src/
├── ProxyManager.Domain/          Entities and enums
├── ProxyManager.Application/    Use cases, DTOs, and validation
├── ProxyManager.Infrastructure/ EF Core, migrations, audit, DNS client
├── ProxyManager.Proxy/           YARP projection and proxy middleware
├── ProxyManager.Certificates/    ACME, renewal, and SNI certificate selection
├── ProxyManager.Streams/         TCP/UDP forwarding
└── ProxyManager.Api/             ASP.NET host, endpoints, Identity, static UI
web/                              SolidJS admin UI and Vite build
tests/ProxyManager.Tests/         xUnit tests
docs/                             User, API, observability, and development docs
scripts/                          Migration and diagnostics helpers
benchmarks/                       k6 load-test harness
```
