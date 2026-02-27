#!/usr/bin/env bash
set -euo pipefail

CHANNEL_ID="${1:-${CHANNEL_ID:-}}"
if [[ -z "$CHANNEL_ID" ]]; then
  echo "Usage: $0 <channelId>"
  echo "   or: CHANNEL_ID=<channelId> make check-qr"
  exit 1
fi

QR_BASE_URL="${QR_BASE_URL:-http://localhost:3401}"
LOG_FILE="${LOG_FILE:-/home/blad/labs/ai-agent-flow/.agent/logs/qr-bridge-full.log}"

echo "[check-qr] channelId=$CHANNEL_ID"
echo "[check-qr] qrBaseUrl=$QR_BASE_URL"

AUTH_HEADER=()
if [[ -n "${BRIDGE_API_KEY:-}" ]]; then
  AUTH_HEADER=(-H "Authorization: Bearer ${BRIDGE_API_KEY}")
fi

echo
echo "== Bridge health =="
curl -fsS "${AUTH_HEADER[@]}" "$QR_BASE_URL/health" | jq .

echo
echo "== Session status =="
STATUS_RAW="$(curl -s "${AUTH_HEADER[@]}" "$QR_BASE_URL/session/status?channelId=$CHANNEL_ID" || true)"
if [[ -n "$STATUS_RAW" ]]; then
  echo "$STATUS_RAW" | jq .
fi

if echo "$STATUS_RAW" | rg -q "session not found"; then
  echo
  echo "== Session start (auto) =="
  curl -s "${AUTH_HEADER[@]}" -X POST "$QR_BASE_URL/session/start" -H 'Content-Type: application/json' -d "{\"channelId\":\"$CHANNEL_ID\"}" | jq . || true
  sleep 2
  echo
  echo "== Session status (after start) =="
  curl -s "${AUTH_HEADER[@]}" "$QR_BASE_URL/session/status?channelId=$CHANNEL_ID" | jq . || true
fi

echo
echo "== Session QR =="
curl -s "${AUTH_HEADER[@]}" "$QR_BASE_URL/session/qr?channelId=$CHANNEL_ID" | jq . || true

echo
echo "== Last QR bridge logs =="
if [[ -f "$LOG_FILE" ]]; then
  tail -n 120 "$LOG_FILE"
else
  echo "Log file not found: $LOG_FILE"
fi