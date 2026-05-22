#!/usr/bin/env bash
set -euo pipefail

PORTS=(
  "${API_PORT:-5000}:API"
  "${FRONTEND_PORT:-3039}:Frontend"
  "${QR_PORT:-3401}:QR Bridge"
  "${MCP_TEST_PORT:-3501}:MCP Test"
  "${MCP_SERVER_PORT:-3502}:MCP Server"
  "27018:MongoDB"
  "6380:Redis"
  "6333:Qdrant REST"
  "6334:Qdrant gRPC"
  "4222:NATS"
  "8222:NATS Monitor"
)

is_listening() {
  local port="$1"
  if command -v ss >/dev/null 2>&1; then
    ss -ltn "( sport = :$port )" | grep -q LISTEN
  elif command -v netstat >/dev/null 2>&1; then
    netstat -tln 2>/dev/null | grep -q ":$port "
  else
    lsof -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
  fi
}

echo "[ports] Checking local ports..."
for entry in "${PORTS[@]}"; do
  port="${entry%%:*}"
  name="${entry#*:}"
  if is_listening "$port"; then
    echo "[ports] OK    $name ($port)"
  else
    echo "[ports] FREE  $name ($port)"
  fi
done
