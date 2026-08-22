# Observability

YARP Proxy Manager ships with in-app diagnostics plus optional Prometheus / Tempo /
Grafana integration, so you can watch live traffic before (and while) it replaces your
existing reverse proxy — and prove to yourself that it is behaving before releasing it
to others.

## In-app diagnostics

Open **Admin → Diagnostics** (any authenticated user sees overview + traffic; the
recent-requests view and body-capture toggle are admin-only). The page shows:

- **Overview cards** — uptime, total requests/failures since boot, tracked hostnames,
  in-memory YARP routes/clusters, proxy-host count, certificate health (failed /
  expiring < 30 days), stream listener status, capture settings and the OTLP trace
  endpoint (if configured).
- **Traffic by host** — per-hostname table over a selectable window (1m / 5m / 15m /
  since boot): request count, 2xx/3xx/4xx/5xx breakdown, average and p50/p95/p99
  latency (approximated from logarithmic buckets), bytes in/out, active requests, last
  error. Rows are annotated with the matching proxy host when the hostname maps to one
  (wildcard `*.example.com` entries resolve any subdomain).
- **Recent requests** — the last 100 proxied requests (newest first) with method, host,
  path, status, duration, bytes, client IP. Each row expands to show the captured
  request/response bodies **when body capture is enabled**.

The page refreshes every 10 seconds.

### Body capture

Request/response body capture is **off by default**. When enabled (Settings page or the
Diagnostics page toggle), the proxy stores the last request/response body per captured
request, truncated to a configurable size (default 4096 bytes). `Authorization` and
`Cookie` headers are never stored. Capture is in-memory only — it is lost on restart.

## API

| Endpoint | Auth | Description |
| --- | --- | --- |
| `GET /api/v1/diagnostics/overview` | session or API key | system counters, streams, certs, capture/trace config |
| `GET /api/v1/diagnostics/traffic?window=5m` | session or API key | per-host traffic (`all`, `1m`, `5m`, `15m`) |
| `GET /api/v1/diagnostics/requests?limit=100` | admin session | recent requests incl. captured bodies |
| `GET /api/v1/settings/diagnostics` | admin | capture settings |
| `PUT /api/v1/settings/diagnostics` | admin | update capture settings |

## Prometheus metrics

`GET /metrics` on the **admin port** (81 in Docker) exposes:

- **`traffic_*`** (from the `ProxyManager.Traffic` meter) — per-hostname proxy traffic:
  `traffic_requests_total{host,status_class}`, `traffic_duration_seconds{host}`,
  `traffic_bytes_total{host,direction}`, `traffic_active{host}`, `traffic_hosts`,
  `traffic_failed_total{host,reason}`, plus `traffic.stream.*` gauges per stream id.
- **`http_server_*` / `kestrel_*` / `http_client_*`** — ASP.NET Core, Kestrel and
  HttpClient instrumentations (inbound/outbound request durations, connection stats).
- **`aspnetcore_*`** — Identity/auth metrics.

Example queries:

```
# requests per second by host
rate(traffic_requests_total[5m])
# 95th-percentile latency by host
histogram_quantile(0.95, sum by (le, host) (rate(traffic_duration_seconds_bucket[5m])))
# error ratio
sum by (host) (rate(traffic_failed_total[5m])) / sum by (host) (rate(traffic_requests_total[5m]))
```

## Distributed traces (OTLP)

Traces are **disabled by default**. Set the OTLP endpoint (HTTP) and the proxy will
export a trace per proxied request — the inbound ASP.NET activity and the outbound
HttpClient activity form one hop, and YARP propagates the trace context to the upstream:

```bash
# environment variable (both are equivalent)
OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo:4318
# or
Diagnostics__Tracing__Endpoint=http://tempo:4318
```

The exporter uses OTLP/HTTP with protobuf and posts to `/v1/traces` (a bare
`http://host:4318` endpoint is normalized to `http://host:4318/v1/traces` automatically).

The Diagnostics page shows whether the endpoint is configured. Any OTLP-HTTP backend
works (Tempo, Jaeger, Grafana Cloud, an OpenTelemetry Collector). The compose profile
below wires everything for you.

## Docker observability profile

```bash
cd docker
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d
```

This adds:

| Service | Port | Purpose |
| --- | --- | --- |
| Prometheus | 9090 | scrapes `proxy-manager:81/metrics` |
| Tempo | 3200 / 4318 | trace backend, OTLP HTTP receiver |
| Grafana | 3000 | dashboards (Prometheus + Tempo datasources pre-provisioned) |

and sets `OTEL_EXPORTER_OTLP_ENDPOINT` on the proxy-manager container. Grafana runs
with anonymous admin for local use — restrict `GF_AUTH_ANONYMOUS_*` before exposing it.

## Retention & scope

- All traffic statistics are **in-memory and session-scoped** (like stream status): they
  reset on restart and are never persisted. The recent-requests buffer holds at most
  50,000 samples. For durable history, scrape `/metrics` with Prometheus.
- Metrics are labeled per hostname (not per path or destination) to keep cardinality
  bounded.
- Client IP is the connection IP; when the proxy sits behind a load balancer the
  `X-Forwarded-For` value is not trusted, so the UI shows the direct peer.

## Validating before release

1. Enable the diagnostics page and body capture on a staging host.
2. Generate traffic (see `benchmarks/` for k6 scenarios) and confirm the traffic table,
   recent requests and `/metrics` reflect it.
3. Stand up the observability profile, confirm spans appear in Tempo, and set up a
   Grafana panel for error ratio + latency per host.
4. Watch `traffic_failed_total{reason="server_error"}` and the diagnostics `Last error`
   column during the soak; any upstream or proxy fault is visible immediately.
5. Confirm `/healthz` (container liveness) and `/api/v1/health` (routes/clusters
   reloaded) as deployment health signals.
