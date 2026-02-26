#!/usr/bin/env bash
set -euo pipefail

API_BASE="${API_BASE:-http://localhost:5000}"
TENANT_ID="${TENANT_ID:-tenant-1}"
SESSION_ID="${SESSION_ID:-}"
LIMIT="${LIMIT:-20}"

if [[ -z "$SESSION_ID" ]]; then
  echo "Usage: SESSION_ID=<session-id> [API_BASE=http://localhost:5000] [TENANT_ID=tenant-1] $0"
  exit 1
fi

URL="$API_BASE/api/v1/tenants/$TENANT_ID/channel-sessions/$SESSION_ID/messages?limit=$LIMIT"

echo "[capture] GET $URL"
RESP="$(curl -fsS "$URL")"

echo "$RESP" | jq . > /tmp/e2e-messages.json

EXEC_ID="$(jq -r 'map(.agentExecutionId) | map(select(.!=null and .!="")) | .[0] // ""' /tmp/e2e-messages.json)"
IN_ID="$(jq -r 'map(.channelMessageIdIn) | map(select(.!=null and .!="")) | .[0] // ""' /tmp/e2e-messages.json)"
OUT_ID="$(jq -r 'map(.channelMessageIdOut) | map(select(.!=null and .!="")) | .[0] // ""' /tmp/e2e-messages.json)"

LATENCY="$(jq -r 'if length >= 2 then ((.[-1].createdAt | fromdateiso8601) - (.[0].createdAt | fromdateiso8601))*1000|floor else 0 end' /tmp/e2e-messages.json)"

cat <<EOF

=== E2E EVIDENCE ===
executionId: ${EXEC_ID:-N/A}
channelMessageIdIn: ${IN_ID:-N/A}
channelMessageIdOut: ${OUT_ID:-N/A}
latencyMs: ${LATENCY:-0}

EOF

echo "[capture] Raw saved at /tmp/e2e-messages.json"