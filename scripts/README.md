# NPM → YARP export

[`npm-export-to-yarp.mjs`](./npm-export-to-yarp.mjs) fetches the full configuration of an
**Nginx Proxy Manager** instance through its public API and writes it as a JSON backup that
**YARP Proxy Manager** can restore (hosts, redirects, streams, access lists).

## Requirements

- Node.js 18+ (uses the built-in `fetch`; no dependencies)
- An NPM token — create one in NPM: *Account → Access Tokens* (or use an existing one)

## Usage

```bash
# Windows PowerShell
$env:NPM_TOKEN = "eyJhbGciOi..."
node scripts/npm-export-to-yarp.mjs --npm-url http://192.168.2.2:81
```

This writes two files into the current directory:

| File | Contents |
| --- | --- |
| `npm-export-to-yarp.json` | Backup in YARP `BackupPayload` format — this is the file to import |
| `npm-export-to-yarp.report.md` | Mapping table (NPM id → YARP id), counts, skipped fields, warnings |

Options (see the script header for the full list):

| Option | Purpose |
| --- | --- |
| `--npm-url <url>` | NPM base URL (default `http://192.168.2.2:81`) |
| `--token <token>` | NPM bearer token (or `$env:NPM_TOKEN`) |
| `--out <path>` / `--report <path>` | Override output paths |
| `--no-report` | Skip the report |
| `--import-url <url>` | After exporting, restore into a running YARP Proxy Manager admin API |
| `--import-email` / `--import-password` | YARP admin credentials for `--import-url` (or `$env:YARP_EMAIL` / `$env:YARP_PASSWORD`) |

## Importing into YARP Proxy Manager

1. Log in to the YARP admin UI.
2. **Admin → Backup & Restore → Restore configuration** and select `npm-export-to-yarp.json`.
3. Restoring **replaces** the current YARP configuration (hosts, redirects, streams, access lists).

Or fully automated:

```bash
node scripts/npm-export-to-yarp.mjs `
  --npm-url http://192.168.2.2:81 `
  --import-url http://your-yarp-host:81 `
  --import-email admin@example.com `
  --import-password 'your-password'
```

## What transfers and what doesn't

Transfers: proxy hosts (scheme, destination, websockets, exploit blocking, HTTP/2,
Force-HTTPS flag, access-list binding, custom locations), redirects (301/302, path
preservation), streams (TCP/UDP, listen/forward ports), access lists (satisfy-any,
allow/deny IP rules when the API exposes them).

Does **not** transfer (YARP-side equivalents are listed in the report):

- **Certificates / private keys** — the NPM API never exposes them and the YARP backup
  format deliberately excludes them. Re-issue certificates in YARP (ACME) or upload
  PFX/PEM after the restore; hosts keep the `ssl_forced`/Force-HTTPS flag.
- NPM `advanced_config`, caching, HSTS, access-list basic auth (`pass_auth`/`auth`).
- NPM redirect `forward_scheme "$scheme"` is mapped to `https` (YARP has no
  scheme-preserving redirect).

## Verified end-to-end

The generated file was restored into an isolated local YARP Proxy Manager instance
(`POST /api/v1/backup/restore` with an admin session): 9 hosts, 1 redirect, 1 access
list (with host bindings), 0 streams, and YARP hot-reloaded 9 routes / 9 clusters.
