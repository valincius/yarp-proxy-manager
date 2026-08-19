# Benchmarks — YARP Proxy Manager vs Nginx (and NPM)

A small, reproducible load-test harness that compares this project's YARP-based
proxy against plain (tuned) Nginx, following the methodology of
[Milan Jovanović's "YARP vs Nginx — A Quick Performance Comparison"](https://milanjovanovic.tech/blog/yarp-vs-nginx-a-quick-performance-comparison):
fixed virtual-user loads against a trivial upstream, reporting **requests per second (RPS)**
and **p90/p95 latency**. Nginx Proxy Manager (NPM) is included as an optional third
candidate — it is Nginx under the hood plus a management layer, so expect it to
track the Nginx column with some overhead.

> Numbers are **environment-specific**. Run the harness on your own hardware and
> treat the results as a comparison *methodology* plus a relative picture, not an
> absolute claim.

## What it measures

| Service | What runs | Route |
|---------|-----------|-------|
| `upstream` | nginx:alpine returning `hello` on every path | — |
| `yarp` | this project (YARP + the manager's middleware stack, built image) | `yarp.local` → upstream |
| `nginx` | plain nginx, worker/event tuning from the article | `/hello` → upstream |
| `npm` (optional) | jc21/nginx-proxy-manager | `/hello` → upstream |
| `k6` | grafana/k6 load generator | — |

All proxies run in Docker on one machine and forward to the same upstream, so the
network path is identical. Load: k6 with 10 / 50 / 100 / 200 virtual users, 30s each.

## Run it

```bash
cd benchmarks

# build the YARP image first (from the repo root)
docker compose -f ../docker/docker-compose.yml build

# yarp vs nginx (~5 minutes)
./run.ps1

# include NPM (pulls ~1GB on first run)
./run.ps1 -IncludeNpm
```

Results print to the console and are saved to `benchmarks/results.csv`.

## Sample results (2026-08-19, Docker Desktop for Windows, 30s per run)

| VUs | YARP RPS | Nginx RPS | YARP p90 (ms) | Nginx p90 (ms) | YARP p95 (ms) | Nginx p95 (ms) |
|-----|----------|-----------|---------------|----------------|---------------|----------------|
| 10  | 6,291    | 9,864     | 2.50          | 1.29           | 3.05          | 1.71           |
| 50  | 7,707    | 17,518    | 9.90          | 5.01           | 12.42         | 7.26           |
| 100 | 8,407    | 23,960    | 16.36         | 8.04           | 21.18         | 10.86          |
| 200 | 10,446   | 24,590    | 24.57         | 14.97          | 26.77         | 20.53          |

Both proxies scale with load; on this machine the tuned plain-nginx container
outperforms the manager's full stack (YARP + redirect/access-list/exploit/404
middleware + SQLite/EF + audit). The reference article, run on bare-metal Linux
with bare YARP, reported YARP **above** nginx (12.7k → 36.7k RPS vs nginx ~10k
flat). The discrepancy is expected: the article compared bare YARP, while the
`yarp` column here is the whole manager, running under Docker Desktop's VM.

## Notes & caveats

- **Not apples-to-apples in the strictest sense**: the `yarp` column runs the full
  manager — YARP plus our redirect/access-list/exploit/404 middleware and hot-reload
  machinery — while `nginx` is a bare reverse proxy. That is the fair "what you
  actually get" comparison for this project.
- The article found YARP scaling well past Nginx's flat ~10k RPS under higher load
  with better tail latency; expect a similar shape but different absolute numbers.
- For reproducible numbers: run on a quiet machine, give each proxy a warm-up
  request, and keep the upstream identical across runs.
