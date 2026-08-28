# User guide

This guide covers the most common ways to use YARP Proxy Manager after the initial Docker setup. For the complete machine-readable interface, see the [REST API reference](API.md).

## Proxy an HTTP app

Create a proxy host in the UI, or use the API with an API key created from the **API Keys** page:

```bash
curl -X POST http://your-host:81/api/v1/hosts \
  -H "X-Api-Key: yarp_..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "My App",
    "domainNames": ["app.example.com"],
    "enabled": true,
    "scheme": "http",
    "forwardHost": "10.0.0.25",
    "forwardPort": 8080,
    "blockCommonExploits": true,
    "forceHttps": false,
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
```

Requests for `app.example.com` are proxied immediately. WebSockets and HTTP/2 are supported. Add a location when `/api` or another path needs a different upstream, or add multiple destinations for load balancing:

```json
{
  "locations": [
    { "pathPrefix": "/api", "stripPrefix": true, "scheme": "http", "forwardHost": "10.0.0.26", "forwardPort": 9000, "order": 10 }
  ],
  "destinations": [
    { "forwardHost": "10.0.0.11", "forwardPort": 8080 },
    { "forwardHost": "10.0.0.12", "forwardPort": 8080 }
  ],
  "loadBalancingPolicy": "roundrobin",
  "healthCheckEnabled": true,
  "healthCheckPath": "/health",
  "healthCheckIntervalSeconds": 10
}
```

## Serve HTTPS with ACME

Request a Let's Encrypt certificate using HTTP-01:

```bash
curl -X POST http://your-host:81/api/v1/certificates/issue \
  -H "X-Api-Key: yarp_..." \
  -H "Content-Type: application/json" \
  -d '{ "name": "App cert", "domains": ["app.example.com"], "challengeType": "Http01" }'
```

Attach the returned certificate ID to the proxy host and set `forceHttps` to `true`. Wildcard certificates require DNS-01; configure a Cloudflare DNS credential and use `"challengeType": "Dns01"`. Certificates renew automatically.

## Redirect a domain

```bash
curl -X POST http://your-host:81/api/v1/redirects \
  -H "X-Api-Key: yarp_..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Old domain",
    "domainNames": ["old.example.com"],
    "enabled": true,
    "statusCode": 301,
    "preservePath": true,
    "forwardScheme": "https",
    "forwardHost": "www.example.com",
    "forwardPort": 443,
    "certificateId": null
  }'
```

## Restrict access to a network

Create an access list, then select it on a proxy host:

```bash
curl -X POST http://your-host:81/api/v1/access-lists \
  -H "X-Api-Key: yarp_..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Office only",
    "satisfyAny": true,
    "rules": [
      { "action": "Allow", "pattern": "203.0.113.0/24" },
      { "action": "Allow", "pattern": "198.51.100.7" },
      { "action": "Deny", "pattern": "*" }
    ]
  }'
```

Rules support IP addresses, CIDRs, and `*`. With `satisfyAny`, one matching allow rule is enough.

## Forward TCP or UDP traffic

Streams handle raw protocols that are not HTTP. This example forwards PostgreSQL:

```bash
curl -X POST http://your-host:81/api/v1/streams \
  -H "X-Api-Key: yarp_..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Postgres",
    "enabled": true,
    "protocol": "Tcp",
    "listenPort": 5432,
    "forwardHost": "10.0.0.40",
    "forwardPort": 5432
  }'
```

Publish the listen port in `docker/docker-compose.yml`; stream ports are not published by default. UDP works the same way with `"protocol": "Udp"`.

Useful stream targets include SSH, MySQL, PostgreSQL, Redis, MongoDB, MQTT, DNS, syslog, NTP, and game servers. The manager validates port conflicts and shows sessions and byte counts in the UI.

## Docker autodiscovery

Mount the Docker engine socket, enable Docker integration from **Settings**, and opt containers in with labels:

```yaml
services:
  my-app:
    image: my-app:latest
    labels:
      proxy-manager.enable: "true"
      proxy-manager.host: "app.example.com"
      proxy-manager.port: "8080"
      proxy-manager.scheme: "http"
      proxy-manager.name: "My App"
```

Keep the manager and published containers on a shared Docker network. Hosts are created from labels and removed when the container disappears.

## Backups and operations

Use the Settings page to create JSON backups, restore validated backups, configure Docker integration, and manage API keys. Use [Observability](OBSERVABILITY.md) for diagnostics, metrics, optional OTLP tracing, and the Prometheus/Tempo/Grafana compose profile.
