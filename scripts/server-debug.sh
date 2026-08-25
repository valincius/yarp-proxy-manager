#!/usr/bin/env bash
# Server-side diagnostics for YARP Proxy Manager. Run this ON the server, from the
# directory that contains docker-compose.yml. Prints everything needed to debug
# "no traffic proxied / no metrics / cert renewal failing" in one shot.
#
# Usage:
#   bash scripts/server-debug.sh [admin-email] [admin-password]
#   (email defaults to admin@example.com; pass the password explicitly or set YARP_PASSWORD)
set -u

EMAIL="${1:-admin@example.com}"
PASS="${2:-${YARP_PASSWORD:-}}"
if [ -z "$PASS" ]; then
  read -r -s -p "Admin password: " PASS
  echo
fi
ADMIN_PORT=81
COMPOSE="docker-compose.yml"

echo "===== 1. Container status ====="
docker compose -f "$COMPOSE" ps 2>&1 || docker compose ps 2>&1

echo ""
echo "===== 2. Is anything else holding 80/81/443? (NPM must be OFF) ====="
ss -tlnp 2>/dev/null | grep -E ':(80|81|443)\b' || netstat -tlnp 2>/dev/null | grep -E ':(80|81|443)\b' || echo "no listener found on 80/81/443 (or ss/netstat unavailable)"

echo ""
echo "===== 3. Container logs (last 60 lines) ====="
docker logs --tail 60 yarp-proxy-manager 2>&1 | tail -60

echo ""
echo "===== 4. Rolling log file (last 40 lines) ====="
ls -t data/logs/ 2>/dev/null | head -3
LOG=$(ls -t data/logs/*.log 2>/dev/null | head -1)
if [ -n "${LOG:-}" ]; then tail -40 "$LOG"; else echo "(no rolling logs yet — container may not have started cleanly)"; fi

echo ""
echo "===== 5. Health endpoints ====="
echo -n "healthz:    "; curl -fsS -m 5 "http://127.0.0.1:${ADMIN_PORT}/healthz" || echo "FAILED"
echo -n "api health: "; curl -fsS -m 5 "http://127.0.0.1:${ADMIN_PORT}/api/v1/health" || echo "FAILED (unauthenticated? it needs a session)"

echo ""
echo "===== 6. Admin login + config state ====="
XSRF=$(curl -fsS -m 5 -c /tmp/yarp-cookies -b /tmp/yarp-cookies "http://127.0.0.1:${ADMIN_PORT}/api/v1/auth/antiforgery" | sed -E 's/.*"token":"([^"]+)".*/\1/')
LOGIN=$(curl -fsS -m 5 -b /tmp/yarp-cookies -c /tmp/yarp-cookies -X POST \
  -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $XSRF" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}" \
  "http://127.0.0.1:${ADMIN_PORT}/api/v1/auth/login")
echo "login response: ${LOGIN:0:120}"
XSRF2=$(curl -fsS -m 5 -b /tmp/yarp-cookies -c /tmp/yarp-cookies "http://127.0.0.1:${ADMIN_PORT}/api/v1/auth/antiforgery" | sed -E 's/.*"token":"([^"]+)".*/\1/')
AUTH="Cookie: yarp_manager=$(grep yarp_manager /tmp/yarp-cookies | awk '{print $NF}')"

echo ""
echo "--- hosts (name | domains | enabled | scheme://host:port | forceHttps) ---"
curl -fsS -m 5 -H "$AUTH" "http://127.0.0.1:${ADMIN_PORT}/api/v1/hosts" \
  | python3 -c "
import sys, json
for h in json.load(sys.stdin):
    print(' | '.join([h['name'], ','.join(h['domainNames']), str(h['enabled']), f\"{h['scheme']}://{h['forwardHost']}:{h['forwardPort']}\", str(h['forceHttps'])]))
" 2>/dev/null || echo "(python3 not available — install it or check the hosts page in the UI)"

echo ""
echo "--- redirects ---"
curl -fsS -m 5 -H "$AUTH" "http://127.0.0.1:${ADMIN_PORT}/api/v1/redirects" 2>/dev/null | head -c 600; echo ""

echo ""
echo "--- certificates (name | status | lastRenewalError) ---"
curl -fsS -m 5 -H "$AUTH" "http://127.0.0.1:${ADMIN_PORT}/api/v1/certificates" \
  | python3 -c "
import sys, json
for c in json.load(sys.stdin):
    print(' | '.join([c['name'], c['status'], str(c.get('lastRenewalError'))]))
" 2>/dev/null || echo "(python3 not available)"

echo ""
echo "--- dns-credentials (needed for DNS-01 renewal) ---"
curl -fsS -m 5 -H "$AUTH" "http://127.0.0.1:${ADMIN_PORT}/api/v1/dns-credentials" 2>/dev/null | head -c 400; echo ""

echo ""
echo "--- acme-settings (staging vs production CA) ---"
curl -fsS -m 5 -H "$AUTH" "http://127.0.0.1:${ADMIN_PORT}/api/v1/acme-settings" 2>/dev/null; echo ""

echo ""
echo "===== 7. Diagnostics ====="
curl -fsS -m 5 -H "$AUTH" "http://127.0.0.1:${ADMIN_PORT}/api/v1/diagnostics/overview" 2>/dev/null | head -c 800; echo ""
echo "--- traffic ---"
curl -fsS -m 5 -H "$AUTH" "http://127.0.0.1:${ADMIN_PORT}/api/v1/diagnostics/traffic?window=5m" 2>/dev/null | head -c 800; echo ""

echo ""
echo "===== 8. Metrics sample (traffic_*) ====="
curl -fsS -m 5 "http://127.0.0.1:${ADMIN_PORT}/metrics" 2>/dev/null | grep -E '^(traffic_|yarp_)' | head -20 || echo "(no traffic_* metrics — either no traffic yet or the meter is not registered)"

echo ""
echo "===== 9. Data volume contents ====="
ls -la data/ 2>/dev/null | head -20

echo ""
echo "===== 10. Upstream reachability from inside the container ====="
# Probes an example upstream; edit the address to match a host reachable from the container.
docker exec yarp-proxy-manager sh -c 'wget -q -T 3 -O - http://192.168.2.2:6000/ 2>&1 | head -c 200' 2>&1 || echo "(cannot reach 192.168.2.2:6000 from the container — check upstream bindings/networks)"
