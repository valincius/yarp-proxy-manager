# REST API

YARP Proxy Manager exposes a JSON REST API on the **admin port** (81 in Docker, 5081 in dev) under `/api/v1`. It is the same API the admin UI uses — there is no separate API surface.

- **OpenAPI:** interactive schema at `GET /openapi/v1.json` (served on the admin port).
- **Authentication:** cookie session (browser) **or** an API key (programmatic). API keys are sent in the `X-Api-Key` header (or `Authorization: Bearer <key>`).
- **Antiforgery:** browser cookie sessions must send the `X-XSRF-TOKEN` header on mutating requests. API-key requests skip antiforgery.
- **Errors:** non-2xx responses are `application/problem+json` (`{ "title": ..., "status": ..., "errors": [...] }`).
- **Scope:** API keys can manage *proxy entities* — hosts, redirects, access lists, streams, certificates, DNS credentials, ACME settings. They **cannot** access users, backups, or API-key management (those require an admin cookie session). Keys are not logged.

## Managing API keys

Keys are created and deleted from the admin UI (**API Keys** page) or via the API itself using an admin cookie session:

| Method | Path | Description |
|--------|------|-------------|
| `GET`    | `/api/v1/api-keys`         | List keys (prefix, name, created/last-used — never the secret). |
| `POST`   | `/api/v1/api-keys`         | `{ "name": "ci" }` → `{ "key": {...}, "plaintext": "yarp_…" }`. The plaintext is returned **once**. |
| `DELETE` | `/api/v1/api-keys/{id}`    | Revoke a key. |

## Proxy entities

| Entity | Routes |
|--------|--------|
| Proxy hosts | `GET/POST /hosts`, `GET/PUT/DELETE /hosts/{id}`, `PATCH /hosts/{id}/enable` |
| Redirection hosts | `GET/POST /redirects`, `GET/PUT/DELETE /redirects/{id}`, `PATCH /redirects/{id}/enable` |
| Access lists | `GET/POST /access-lists`, `GET/PUT/DELETE /access-lists/{id}` |
| Streams (TCP/UDP) | `GET/POST /streams`, `GET/PUT/DELETE /streams/{id}`, `GET /streams/status` |
| Certificates | `GET /certificates`, `POST /certificates/issue`, `POST /certificates/upload`, `POST /certificates/{id}/renew`, `DELETE /certificates/{id}` |

**Certificate deduplication.** One certificate row is kept per normalized domain set. `POST /certificates/issue` is idempotent: if an `Issued`, unexpired certificate already covers exactly the requested domains, it is returned instead of creating a duplicate row or starting a new ACME order. After any successful issue/upload/renewal, other rows covering the same domains are automatically deleted and any proxy hosts / redirection hosts referencing them are re-pointed to the surviving certificate (so force-HTTPS keeps working). On startup the same sweep collapses duplicates left by earlier versions.
| DNS credentials | `GET/POST /dns-credentials`, `DELETE /dns-credentials/{id}` |
| ACME settings | `GET/PUT /acme-settings` |
| Settings (404 page) | `GET/PUT /settings/not-found` — mode `Default`/`Empty`/`Custom`, `template` with `{{host}}`, `{{path}}`, `{{method}}`, `{{now}}` placeholders *(admin cookie only)* |
| Settings (Docker) | `GET/PUT /settings/docker`, `POST /settings/docker/sync` *(admin cookie only)* |
| Health | `GET /health` (routes/clusters in memory) |
| Audit log | `GET /audit?limit=100&entityType=…` |
| Auth | `POST /auth/login`, `POST /auth/logout`, `GET /auth/session`, `GET /auth/antiforgery` |
| Backup/restore | `GET /backup`, `POST /backup/restore` *(admin cookie only)* |
| Users | `GET/POST /users`, `PATCH /users/{id}/enable`, `DELETE /users/{id}` *(admin cookie only)* |
| API keys | see above *(admin cookie only)* |

## Quick start

```bash
# 1. Create a key (cookie session). curl with a saved cookie jar:
curl -c jar -b jar -X POST http://localhost:5081/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@example.com","password":"changeme"}'

curl -c jar -b jar -X POST http://localhost:5081/api/v1/api-keys \
  -H 'Content-Type: application/json' \
  -d '{"name":"ci"}'
# → { "key": { "id": "...", "name": "ci", "prefix": "yarp_AbC…" }, "plaintext": "yarp_AbCdEf..." }

# 2. Use the key for everything else:
curl http://localhost:5081/api/v1/hosts -H 'X-Api-Key: yarp_AbCdEf...'

curl -X POST http://localhost:5081/api/v1/hosts \
  -H 'X-Api-Key: yarp_AbCdEf...' \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "My App",
    "domainNames": ["app.example.com"],
    "enabled": true,
    "scheme": "http",
    "forwardHost": "10.0.0.25",
    "forwardPort": 8080,
    "webSocketsEnabled": true,
    "blockCommonExploits": true,
    "forceHttps": false,
    "http2Support": true,
    "certificateId": null,
    "accessListId": null,
    "requestHeaders": [],
    "responseHeaders": [],
    "locations": [],
    "destinations": [],
    "loadBalancingPolicy": null,
    "healthCheckEnabled": false,
    "healthCheckPath": null,
    "healthCheckIntervalSeconds": 10
  }'

curl http://localhost:5081/api/v1/redirects -H 'X-Api-Key: yarp_AbCdEf...'
curl http://localhost:5081/api/v1/streams/status -H 'X-Api-Key: yarp_AbCdEf...'
```

The API key can also be sent as `Authorization: Bearer yarp_…`.

## Notes for agents / automation

- The API is **idempotent-friendly**: `PUT` replaces an entity wholesale; `DELETE` returns 204.
- Configuration changes take effect immediately (hot reload) — no restart needed.
- Streams: `PATCH /streams/{id}/enable` does not exist; toggle via `PUT` with the full body or delete/recreate.
- Rate limiting is not applied; treat keys as secrets and rotate them by creating a new key and deleting the old one.
