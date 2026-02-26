#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$ROOT_DIR/.agent/run"
LOG_DIR="$ROOT_DIR/.agent/logs"
mkdir -p "$RUN_DIR" "$LOG_DIR"

API_LOG="$LOG_DIR/api-full.log"
FRONT_LOG="$LOG_DIR/frontend-full.log"
QR_LOG="$LOG_DIR/qr-bridge-full.log"

API_PID_FILE="$RUN_DIR/api.pid"
FRONT_PID_FILE="$RUN_DIR/frontend.pid"
QR_PID_FILE="$RUN_DIR/qr.pid"

if command -v docker >/dev/null 2>&1; then
  if ss -ltn "( sport = :${MCP_TEST_PORT:-3501} )" | grep -q LISTEN; then
    echo "[full-up] MCP_TEST_PORT ${MCP_TEST_PORT:-3501} is already in use."
    echo "[full-up] Tip: run with another port, e.g.: MCP_TEST_PORT=3511 make up-local-full"
    exit 1
  fi

  echo "[full-up] Starting infra (mongo/redis/mcp-test) via docker-compose.test.yml"
  MCP_TEST_PORT=${MCP_TEST_PORT:-3501} docker compose -f "$ROOT_DIR/docker-compose.test.yml" up -d --wait
else
  echo "[full-up] docker not found; skipping container infra"
fi

echo "[full-up] Starting API..."
(cd "$ROOT_DIR" && nohup dotnet run --project src/AgentFlow.Api/AgentFlow.Api.csproj >"$API_LOG" 2>&1 & echo $! > "$API_PID_FILE")

echo "[full-up] Starting Frontend..."
(cd "$ROOT_DIR/frontend/aiagent_flow" && nohup npm run dev >"$FRONT_LOG" 2>&1 & echo $! > "$FRONT_PID_FILE")

echo "[full-up] Starting WhatsApp QR bridge..."
(cd "$ROOT_DIR/tools/whatsapp-qr-bridge" && npm install >/dev/null && nohup env PORT=${QR_PORT:-3401} AGENTFLOW_BASE_URL=${AGENTFLOW_BASE_URL:-http://localhost:5000} TENANT_ID=${TENANT_ID:-tenant-1} npm start >"$QR_LOG" 2>&1 & echo $! > "$QR_PID_FILE")

echo "[full-up] Done."
echo "  API log:      $API_LOG"
echo "  Front log:    $FRONT_LOG"
echo "  QR bridge log:$QR_LOG"
echo "  PIDs:"
for f in "$API_PID_FILE" "$FRONT_PID_FILE" "$QR_PID_FILE"; do
  [[ -f "$f" ]] && echo "    $(basename "$f" .pid): $(cat "$f")"
done

echo "[full-up] URLs:"
echo "  Frontend: http://localhost:3039 (or Vite output)"
echo "  API:      http://localhost:5000"
echo "  QR bridge:http://localhost:${QR_PORT:-3401}"
