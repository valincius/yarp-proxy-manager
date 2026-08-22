#!/usr/bin/env node
/**
 * Nginx Proxy Manager → YARP Proxy Manager export / converter.
 *
 * Fetches the full configuration of an Nginx Proxy Manager (NPM) instance through
 * its public API (https://nginxproxymanager.com/guide/#/api) and writes it as a
 * JSON backup that YARP Proxy Manager can restore:
 *
 *   - Import through the web UI:  Admin → Backup & Restore → Restore configuration
 *   - Or programmatically:        POST /api/v1/backup/restore  (admin cookie session)
 *
 * The output file matches the YARP `BackupPayload` shape (camelCase JSON of
 * ProxyHost / RedirectHost / Stream / AccessList entities) consumed by
 * src/ProxyManager.Api/Controllers/BackupController.cs.
 *
 * Usage:
 *   node scripts/npm-export-to-yarp.mjs [options]
 *
 * Options:
 *   --npm-url <url>         NPM base URL            (default: http://192.168.2.2:81)
 *   --token <token>         NPM bearer token        (default: $env:NPM_TOKEN)
 *   --out <path>            Backup JSON output path (default: npm-export-to-yarp-<ts>.json)
 *   --report <path>         Markdown report path    (default: npm-export-to-yarp-<ts>.report.md)
 *   --no-report             Skip writing the report
 *   --import-url <url>      After exporting, restore the backup into a running
 *                           YARP Proxy Manager admin API (optional).
 *   --import-email <email>  YARP admin email        (default: $env:YARP_EMAIL)
 *   --import-password <pw>  YARP admin password     (default: $env:YARP_PASSWORD)
 *
 * Examples:
 *   $env:NPM_TOKEN = "eyJhbGciOi..."
 *   node scripts/npm-export-to-yarp.mjs
 *   node scripts/npm-export-to-yarp.mjs --token $env:NPM_TOKEN --out backup.json
 *   node scripts/npm-export-to-yarp.mjs --import-url http://127.0.0.1:5081 --import-email admin@example.com --import-password changeme
 *
 * Mapping notes (also written to the report):
 *   - Certificates are NOT transferred: the NPM API never exposes private keys and the
 *     YARP backup format deliberately excludes them. Re-issue certificates in YARP
 *     (ACME) or upload PFX/PEM after the restore; `forceHttps` follows NPM `ssl_forced`
 *     and will only work once a certificate exists for the host.
 *   - NPM fields with no YARP counterpart are skipped and listed in the report:
 *     `advanced_config`, `caching_enabled`, `hsts_*`, `pass_auth`/`auth` (basic auth).
 *   - NPM redirect `forward_scheme: "$scheme"` (preserve request scheme) cannot be
 *     expressed in YARP; it is mapped to `https` (documented in the report).
 *   - NPM timestamps ("YYYY-MM-DD HH:mm:ss") are interpreted as UTC.
 *   - NPM access-list IP rules come from the per-list detail endpoint (`allow`/`deny`);
 *     some NPM builds omit them — the script warns when it cannot see any rules.
 *   - NPM custom `locations` forward the full request path (nginx `proxy_pass` without
 *     a URI), so `stripPrefix` is set to false.
 *
 * Requires Node.js 18+ (global fetch). Zero dependencies.
 */
import { randomUUID } from 'node:crypto';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

// ---------------------------------------------------------------- CLI parsing
const args = process.argv.slice(2);
const opt = { npmUrl: 'http://192.168.2.2:81', out: null, report: null, writeReport: true };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  const next = () => args[++i];
  switch (a) {
    case '--npm-url': opt.npmUrl = next(); break;
    case '--token': opt.token = next(); break;
    case '--out': opt.out = next(); break;
    case '--report': opt.report = next(); break;
    case '--no-report': opt.writeReport = false; break;
    case '--import-url': opt.importUrl = next(); break;
    case '--import-email': opt.importEmail = next(); break;
    case '--import-password': opt.importPassword = next(); break;
    case '--help':
    case '-h':
      console.log(`Usage: node ${process.argv[1].split(/[\\/]/).pop()} [options]
  --npm-url <url>         NPM base URL            (default: http://192.168.2.2:81)
  --token <token>         NPM bearer token        (default: $env:NPM_TOKEN)
  --out <path>            Backup JSON output path (default: npm-export-to-yarp-<ts>.json)
  --report <path>         Markdown report path    (default: npm-export-to-yarp-<ts>.report.md)
  --no-report             Skip writing the report
  --import-url <url>      Restore into a running YARP Proxy Manager admin API after export
  --import-email <email>  YARP admin email        (default: $env:YARP_EMAIL)
  --import-password <pw>  YARP admin password     (default: $env:YARP_PASSWORD)`);
      process.exit(0);
    default:
      console.error(`Unknown option: ${a} (try --help)`);
      process.exit(2);
  }
}

opt.token ??= process.env.NPM_TOKEN;
opt.importEmail ??= process.env.YARP_EMAIL;
opt.importPassword ??= process.env.YARP_PASSWORD;
if (!opt.token) {
  console.error('Missing NPM token: pass --token or set $env:NPM_TOKEN.');
  process.exit(2);
}

// ------------------------------------------------------------------ utilities
const stamp = () => {
  const d = new Date();
  const p = (n, l = 2) => String(n).padStart(l, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
};
const guid = () => randomUUID();

/** NPM sends 1/0 booleans as integers. */
const toBool = (v) => v === true || v === 1 || v === '1' || v === 'true';

/** Parse NPM "YYYY-MM-DD HH:mm:ss" as UTC → ISO-8601 offset string; null when unparseable. */
const parseNpmDate = (s) => {
  if (!s) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2}):(\d{2})/.exec(String(s));
  if (!m) return null;
  const [, y, mo, d, h, mi, se] = m.map(Number);
  const dt = new Date(Date.UTC(y, mo - 1, d, h, mi, se));
  return Number.isNaN(dt.getTime()) ? null : dt.toISOString().replace(/\.\d{3}Z$/, '+00:00');
};

const jsonHeader = { 'Content-Type': 'application/json', Accept: 'application/json' };

/** GET a JSON endpoint with the bearer token; null on non-2xx (logged). */
async function npmGet(path) {
  const res = await fetch(`${opt.npmUrl}${path}`, {
    headers: { Authorization: `Bearer ${opt.token}`, ...jsonHeader },
    signal: AbortSignal.timeout(20000),
  });
  if (!res.ok) {
    console.warn(`  ! GET ${path} -> ${res.status}`);
    return null;
  }
  return res.json();
}

// ------------------------------------------------------------------- NPM data
const warnings = [];
const skippedFields = new Set();

console.log(`Exporting from ${opt.npmUrl} …`);

const [npmProxyHosts, npmStreams, npmRedirects, accessListSummaries, certificates] = await Promise.all([
  npmGet('/api/nginx/proxy-hosts'),
  npmGet('/api/nginx/streams'),
  npmGet('/api/nginx/redirection-hosts'),
  npmGet('/api/nginx/access-lists'),
  npmGet('/api/nginx/certificates'),
]).then((r) => r.map((x) => (Array.isArray(x) ? x : [])));

// Best-effort list of IPs banned by NPM's exploit-protection (fail2ban-style).
const disabledIps = await npmGet('/api/nginx/disabled-ip-addresses');

// ------------------------------------------------------------- access lists
const accessListDetails = new Map();
for (const summary of accessListSummaries) {
  const detail = await npmGet(`/api/nginx/access-lists/${summary.id}`);
  accessListDetails.set(summary.id, detail ?? summary);
}

const accessListIdMap = new Map(); // NPM access-list id → new YARP GUID
const accessLists = [];
for (const al of accessListSummaries) {
  const newId = guid();
  accessListIdMap.set(al.id, newId);

  const detail = accessListDetails.get(al.id) ?? {};
  const allow = Array.isArray(detail.allow) ? detail.allow : [];
  const deny = Array.isArray(detail.deny) ? detail.deny : [];
  if (!Array.isArray(detail.allow) && !Array.isArray(detail.deny)) {
    warnings.push(
      `Access list "${al.name}" (#${al.id}): the API returned no allow/deny rule arrays — ` +
      `imported with zero IP rules. Verify the list in the NPM UI and recreate rules manually if needed.`);
  }
  if (detail.auth || toBool(detail.pass_auth)) {
    skippedFields.add('access-list basic auth (pass_auth/auth) — not supported by YARP');
  }

  const created = parseNpmDate(al.created_on) ?? new Date().toISOString().replace(/\.\d{3}Z$/, '+00:00');
  accessLists.push({
    id: newId,
    name: al.name ?? `access-list-${al.id}`,
    satisfyAny: toBool(al.satisfy_any),
    rules: [
      ...allow.map((p) => ({ id: guid(), accessListId: newId, action: 'Allow', pattern: String(p) })),
      ...deny.map((p) => ({ id: guid(), accessListId: newId, action: 'Deny', pattern: String(p) })),
    ],
    createdAt: created,
    updatedAt: parseNpmDate(al.modified_on) ?? created,
  });
}

/** NPM uses 0 / null for "no access list". */
const mapAccessListId = (id) => {
  const v = Number(id);
  if (!v) return null;
  const mapped = accessListIdMap.get(v);
  if (!mapped) warnings.push(`Host references unknown access list #${v} — imported without an access list.`);
  return mapped ?? null;
};

// ------------------------------------------------------------------- proxy hosts
const hostIdMap = new Map(); // NPM proxy-host id → new YARP GUID
const hosts = [];
for (const h of npmProxyHosts) {
  const newId = guid();
  hostIdMap.set(h.id, newId);

  const domains = Array.isArray(h.domain_names) ? h.domain_names.map(String) : [];
  const name = domains[0] ?? `host-${h.id}`;
  const created = parseNpmDate(h.created_on) ?? new Date().toISOString().replace(/\.\d{3}Z$/, '+00:00');

  for (const [k, label] of [['advanced_config', 'host advanced_config'],
    ['caching_enabled', 'host caching_enabled'], ['hsts_enabled', 'host hsts_enabled'],
    ['hsts_subdomains', 'host hsts_subdomains']]) {
    if (h[k] !== undefined && h[k] !== '' && h[k] !== 0 && h[k] !== false) skippedFields.add(label);
  }

  hosts.push({
    id: newId,
    name,
    domainNames: domains,
    enabled: toBool(h.enabled),
    scheme: h.forward_scheme === 'https' ? 'https' : 'http',
    forwardHost: String(h.forward_host ?? ''),
    forwardPort: Number(h.forward_port) || 80,
    webSocketsEnabled: toBool(h.allow_websocket_upgrade),
    blockCommonExploits: toBool(h.block_exploits),
    forceHttps: toBool(h.ssl_forced),
    http2Support: toBool(h.http2_support),
    certificateId: null, // NPM certs are not exportable via the API
    accessListId: mapAccessListId(h.access_list_id),
    requestHeaders: [],
    responseHeaders: [],
    locations: (Array.isArray(h.locations) ? h.locations : []).map((loc, i) => {
      if (loc.advanced_config) skippedFields.add('location advanced_config');
      return {
        id: guid(),
        proxyHostId: newId,
        pathPrefix: String(loc.path ?? '/'),
        stripPrefix: false, // NPM forwards the full request path (proxy_pass without a URI)
        scheme: loc.forward_scheme === 'https' ? 'https' : 'http',
        forwardHost: String(loc.forward_host ?? ''),
        forwardPort: Number(loc.forward_port) || 80,
        order: i,
      };
    }),
    destinations: [],
    loadBalancingPolicy: null,
    healthCheckEnabled: false,
    healthCheckPath: null,
    healthCheckIntervalSeconds: 10,
    managedBy: null,
    managedSource: null,
    createdAt: created,
    updatedAt: parseNpmDate(h.modified_on) ?? created,
  });
}

// ------------------------------------------------------------------ redirects
const redirects = [];
for (const r of npmRedirects) {
  const domains = Array.isArray(r.domain_names) ? r.domain_names.map(String) : [];
  const target = String(r.forward_domain_name ?? '');
  const scheme = r.forward_scheme === '$scheme' ? 'https' : (r.forward_scheme === 'https' ? 'https' : 'http');
  if (r.forward_scheme === '$scheme') {
    warnings.push(
      `Redirect "${domains[0]}" uses forward_scheme "$scheme" (preserve request scheme) — ` +
      `YARP cannot express this; mapped to "${scheme}". Change it in the YARP UI if you need http redirects.`);
  }
  for (const [k, label] of [['advanced_config', 'redirect advanced_config'],
    ['hsts_enabled', 'redirect hsts_enabled'], ['hsts_subdomains', 'redirect hsts_subdomains']]) {
    if (r[k] !== undefined && r[k] !== '' && r[k] !== 0 && r[k] !== false) skippedFields.add(label);
  }

  const created = parseNpmDate(r.created_on) ?? new Date().toISOString().replace(/\.\d{3}Z$/, '+00:00');
  redirects.push({
    id: guid(),
    name: domains[0] ? `${domains[0]} → ${target}` : `redirect-${r.id}`,
    domainNames: domains,
    enabled: toBool(r.enabled),
    statusCode: Number(r.forward_http_code) || 301,
    preservePath: toBool(r.preserve_path),
    forwardScheme: scheme,
    forwardHost: target,
    forwardPort: scheme === 'https' ? 443 : 80, // NPM redirects have no port concept
    certificateId: null,
    createdAt: created,
    updatedAt: parseNpmDate(r.modified_on) ?? created,
  });
}

// -------------------------------------------------------------------- streams
const streamsOut = [];
for (const s of npmStreams) {
  const protocol = String(s.tcp_udp ?? 'tcp').toLowerCase() === 'udp' ? 1 : 0; // 0=Tcp, 1=Udp (System.Text.Json numeric enum)
  const created = parseNpmDate(s.created_on) ?? new Date().toISOString().replace(/\.\d{3}Z$/, '+00:00');
  const incoming = Number(s.incoming_port) || 0;
  const fwd = String(s.forwarding_host ?? '');
  const fwdPort = Number(s.forwarding_port) || 0;
  streamsOut.push({
    id: guid(),
    name: `${protocol === 1 ? 'udp' : 'tcp'} ${incoming} → ${fwd}:${fwdPort}`,
    enabled: toBool(s.enabled),
    protocol,
    listenPort: incoming,
    forwardHost: fwd,
    forwardPort: fwdPort,
    createdAt: created,
    updatedAt: parseNpmDate(s.modified_on) ?? created,
  });
}

// --------------------------------------------------------------------- output
const exportedAt = new Date().toISOString().replace(/\.\d{3}Z$/, '+00:00');
const payload = {
  exportedAt,
  hosts,
  redirects,
  streams: streamsOut,
  accessLists,
};

const ts = stamp();
const outPath = resolve(opt.out ?? `npm-export-to-yarp-${ts}.json`);
const reportPath = opt.report ?? `npm-export-to-yarp-${ts}.report.md`;

mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, JSON.stringify(payload, null, 2) + '\n', 'utf8');
console.log(`\nBackup written to ${outPath}`);
console.log(
  `  hosts: ${hosts.length}  redirects: ${redirects.length}  streams: ${streamsOut.length}  access lists: ${accessLists.length}`);

if (opt.writeReport) {
  const lines = [];
  lines.push('# NPM → YARP Proxy Manager export report');
  lines.push('');
  lines.push(`- Source: NPM API at ${opt.npmUrl}`);
  lines.push(`- Exported at: ${exportedAt}`);
  lines.push(`- Backup file: \`${outPath}\``);
  lines.push('');
  lines.push('## Import');
  lines.push('');
  lines.push('1. Open the YARP Proxy Manager admin UI and log in.');
  lines.push('2. Go to **Admin → Backup & Restore → Restore configuration** and pick the backup file.');
  lines.push('3. Restoring **replaces** the current YARP configuration (hosts, redirects, streams, access lists).');
  lines.push('');
  lines.push('## Counts');
  lines.push('');
  lines.push('| Entity | Count |');
  lines.push('| --- | --- |');
  lines.push(`| Proxy hosts | ${hosts.length} |`);
  lines.push(`| Redirection hosts | ${redirects.length} |`);
  lines.push(`| Streams | ${streamsOut.length} |`);
  lines.push(`| Access lists | ${accessLists.length} |`);
  lines.push('');
  lines.push('## ID mapping (NPM → YARP)');
  lines.push('');
  if (hosts.length) {
    lines.push('| NPM host id | YARP host id | Domains |');
    lines.push('| --- | --- | --- |');
    for (const h of npmProxyHosts) {
      const y = hosts.find((x) => x.id === hostIdMap.get(h.id));
      lines.push(`| ${h.id} | \`${y ? y.id : '?'}\` | ${(h.domain_names ?? []).join(', ')} |`);
    }
  }
  if (accessLists.length) {
    lines.push('');
    lines.push('| NPM access-list id | YARP access-list id | Name | Rules (allow/deny) |');
    lines.push('| --- | --- | --- | --- |');
    for (const al of accessListSummaries) {
      const y = accessLists.find((x) => x.id === accessListIdMap.get(al.id));
      const d = accessListDetails.get(al.id) ?? {};
      lines.push(`| ${al.id} | \`${y ? y.id : '?'}\` | ${al.name} | ${JSON.stringify(d.allow ?? [])} / ${JSON.stringify(d.deny ?? [])} |`);
    }
  }
  lines.push('');
  lines.push('## Certificates — NOT migrated (must be re-created in YARP)');
  lines.push('');
  lines.push('The NPM API does not expose certificate private keys and the YARP backup format does not include them.');
  lines.push('Re-issue certificates in YARP (**SSL Certificates**) for every host that needs HTTPS, then toggle '
    + '**Force HTTPS** where the NPM host had `ssl_forced` enabled.');
  if (certificates.length) {
    lines.push('');
    lines.push('| NPM cert id | Domains | Provider | Expires |');
    lines.push('| --- | --- | --- | --- |');
    for (const c of certificates) {
      lines.push(`| ${c.id} | ${(c.domain_names ?? []).join(', ')} | ${c.provider} | ${c.expires_on ?? '?'} |`);
    }
  }
  lines.push('');
  lines.push('## Skipped / transformed fields');
  lines.push('');
  if (skippedFields.size) {
    for (const f of [...skippedFields].sort()) lines.push(`- \`${f}\` — not represented in YARP; skipped.`);
  } else {
    lines.push('- none');
  }
  lines.push('- NPM timestamps (`created_on`/`modified_on`) interpreted as **UTC**.');
  lines.push('- NPM redirects use `forward_scheme "$scheme"` → mapped to `https` (YARP has no scheme-preserving redirect).');
  lines.push('- YARP redirect `Location` headers always include the port (`https://host:443/…`).');
  lines.push('- NPM custom locations proxy the full request path → `stripPrefix: false`.');
  lines.push('- NPM has no host/redirect/stream names → names are derived from domains/ports.');
  lines.push('');
  if (warnings.length) {
    lines.push('## Warnings');
    lines.push('');
    for (const w of warnings) lines.push(`- ${w}`);
    lines.push('');
  }
  if (disabledIps && Array.isArray(disabledIps)) {
    lines.push('## NPM exploit-block banned IPs (not imported)');
    lines.push('');
    lines.push(`\`${disabledIps.join(', ')}\``);
    lines.push('');
  }
  writeFileSync(reportPath, lines.join('\n'), 'utf8');
  console.log(`Report written to ${reportPath}`);
}

// ------------------------------------------------------------- optional import
if (opt.importUrl) {
  if (!opt.importEmail || !opt.importPassword) {
    console.error('--import-url requires --import-email and --import-password (or $env:YARP_EMAIL / $env:YARP_PASSWORD).');
    process.exit(2);
  }
  await importToYarp(payload);
}

async function importToYarp(payload) {
  const base = opt.importUrl.replace(/\/+$/, '');
  const jar = new Map();
  const storeCookies = (res) => {
    for (const c of res.headers.getSetCookie?.() ?? []) {
      const [pair] = c.split(';');
      const idx = pair.indexOf('=');
      if (idx > 0) jar.set(pair.slice(0, idx).trim(), pair.slice(idx + 1).trim());
    }
  };
  const cookieHeader = () => [...jar.entries()].map(([k, v]) => `${k}=${v}`).join('; ');

  // 1. Antiforgery cookie + token.
  let res = await fetch(`${base}/api/v1/auth/antiforgery`, { signal: AbortSignal.timeout(20000) });
  if (!res.ok) throw new Error(`GET /api/v1/auth/antiforgery -> ${res.status}`);
  storeCookies(res);
  let { token } = await res.json();

  // 2. Login with the admin credentials.
  res = await fetch(`${base}/api/v1/auth/login`, {
    method: 'POST',
    headers: { ...jsonHeader, Cookie: cookieHeader(), 'X-XSRF-TOKEN': token },
    body: JSON.stringify({ email: opt.importEmail, password: opt.importPassword }),
    signal: AbortSignal.timeout(20000),
  });
  storeCookies(res);
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`POST /api/v1/auth/login -> ${res.status}: ${body.slice(0, 300)}`);
  }

  // 3. ASP.NET Core rotates the antiforgery cookie on login — re-fetch so the
  //    header token matches the current cookie for the restore request.
  res = await fetch(`${base}/api/v1/auth/antiforgery`, {
    headers: { Cookie: cookieHeader() },
    signal: AbortSignal.timeout(20000),
  });
  storeCookies(res);
  const refreshed = await res.json();
  if (refreshed?.token) token = refreshed.token;

  // 4. Restore.
  res = await fetch(`${base}/api/v1/backup/restore`, {
    method: 'POST',
    headers: { ...jsonHeader, Cookie: cookieHeader(), 'X-XSRF-TOKEN': token },
    body: JSON.stringify(payload),
    signal: AbortSignal.timeout(60000),
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`POST /api/v1/backup/restore -> ${res.status}: ${body.slice(0, 300)}`);
  }
  console.log(`Restored into ${base} (hosts/redirects/streams/access lists replaced).`);
}
