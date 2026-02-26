#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:3501}"
AUTH_HEADER=()
if [[ -n "${MCP_TEST_API_KEY:-}" ]]; then
  AUTH_HEADER=(-H "Authorization: Bearer ${MCP_TEST_API_KEY}")
fi

echo "[1/3] Health"
curl -fsS "${BASE_URL}/health" "${AUTH_HEADER[@]}" | jq .

echo "[2/3] Discovery tools"
curl -fsS "${BASE_URL}/tools" "${AUTH_HEADER[@]}" | jq .

echo "[3/3] Invoke echo_payload"
curl -fsS "${BASE_URL}/invoke" \
  -X POST \
  -H 'Content-Type: application/json' \
  "${AUTH_HEADER[@]}" \
  -d '{"tool":"echo_payload","tenantId":"tenant-1","executionId":"exec-local-1","inputJson":"{\"hello\":\"world\"}","metadata":{"source":"validate-local"}}' | jq .

echo "OK: MCP test server is responding with real discovery+invoke"
