#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$ROOT_DIR/.agent/run"
LOG_DIR="$ROOT_DIR/.agent/logs"
COMPOSE_PROJECT="${COMPOSE_PROJECT_NAME:-aiagents}"
mkdir -p "$RUN_DIR" "$LOG_DIR"

API_LOG="$LOG_DIR/api-full.log"
FRONT_LOG="$LOG_DIR/frontend-full.log"
QR_LOG="$LOG_DIR/qr-bridge-full.log"

API_PID_FILE="$RUN_DIR/api.pid"
FRONT_PID_FILE="$RUN_DIR/frontend.pid"
QR_PID_FILE="$RUN_DIR/qr.pid"
DOTNET_BIN="$(bash "$ROOT_DIR/scripts/resolve-dotnet.sh")"

host_path() {
  local path="$1"
  if [[ "$DOTNET_BIN" == *".exe" ]]; then
    if command -v wslpath >/dev/null 2>&1; then
      wslpath -w "$path"
      return
    fi
    if command -v cygpath >/dev/null 2>&1; then
      cygpath -w "$path"
      return
    fi
  fi
  echo "$path"
}

wait_for_port() {
  local port="$1"
  local label="$2"
  local pid_file="$3"
  local log_file="$4"

  for _ in {1..30}; do
    if ss -ltn "( sport = :$port )" | grep -q LISTEN; then
      return 0
    fi
    if [[ -f "$pid_file" ]] && ! kill -0 "$(cat "$pid_file")" 2>/dev/null; then
      echo "[full-up] $label exited before port $port was ready. Last log lines:"
      tail -80 "$log_file" || true
      return 1
    fi
    sleep 1
  done

  echo "[full-up] $label did not listen on port $port in time. Last log lines:"
  tail -80 "$log_file" || true
  return 1
}

if command -v docker >/dev/null 2>&1; then
  if ss -ltn "( sport = :${MCP_TEST_PORT:-3501} )" | grep -q LISTEN; then
    echo "[full-up] MCP_TEST_PORT ${MCP_TEST_PORT:-3501} is already in use."
    echo "[full-up] Tip: run with another port, e.g.: MCP_TEST_PORT=3511 make up-local-full"
    exit 1
  fi

  echo "[full-up] Starting infra (mongo_local_data + redis_local_data + mcp-test) via docker-compose.local.yml"
  MCP_TEST_PORT=${MCP_TEST_PORT:-3501} docker compose -p "$COMPOSE_PROJECT" -f "$ROOT_DIR/docker-compose.local.yml" up -d --wait
else
  echo "[full-up] docker not found; skipping container infra"
fi

echo "[full-up] Ensuring ports are free..."
for p in ${API_PORT:-5000} ${FRONTEND_PORT:-3039} ${QR_PORT:-3401}; do
  if ss -ltn "( sport = :$p )" | grep -q LISTEN; then
    echo "[full-up] Port $p is busy. Stop previous stack first: make down-local-full"
    exit 1
  fi
done

echo "[full-up] Starting API..."
API_PROJECT="$(host_path "$ROOT_DIR/src/AgentFlow.Api/AgentFlow.Api.csproj")"
(cd "$ROOT_DIR" && nohup env \
  ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development} \
  ASPNETCORE_URLS=${ASPNETCORE_URLS:-http://0.0.0.0:${API_PORT:-5000}} \
  ConnectionStrings__MongoDB=${ConnectionStrings__MongoDB:-mongodb://localhost:27018} \
  ConnectionStrings__Redis=${ConnectionStrings__Redis:-localhost:6380} \
  WhatsApp__QrBridgeApiKey=${BRIDGE_API_KEY:-dev-bridge-key} \
  "$DOTNET_BIN" run --no-build --no-launch-profile --project "$API_PROJECT" >"$API_LOG" 2>&1 & echo $! > "$API_PID_FILE")
wait_for_port "${API_PORT:-5000}" "API" "$API_PID_FILE" "$API_LOG"

echo "[full-up] Starting Frontend..."
(cd "$ROOT_DIR/frontend/aiagent_flow" && nohup npm run dev -- --strictPort --port ${FRONTEND_PORT:-3039} --host >"$FRONT_LOG" 2>&1 & echo $! > "$FRONT_PID_FILE")
wait_for_port "${FRONTEND_PORT:-3039}" "Frontend" "$FRONT_PID_FILE" "$FRONT_LOG"

echo "[full-up] Starting WhatsApp QR bridge..."
(cd "$ROOT_DIR/tools/whatsapp-qr-bridge" && npm install >/dev/null && nohup env PORT=${QR_PORT:-3401} AGENTFLOW_BASE_URL=${AGENTFLOW_BASE_URL:-http://localhost:5000} TENANT_ID=${TENANT_ID:-tenant-1} BRIDGE_API_KEY=${BRIDGE_API_KEY:-dev-bridge-key} npm start >"$QR_LOG" 2>&1 & echo $! > "$QR_PID_FILE")
wait_for_port "${QR_PORT:-3401}" "WhatsApp QR bridge" "$QR_PID_FILE" "$QR_LOG"

echo "[full-up] Done."
echo "  API log:      $API_LOG"
echo "  Front log:    $FRONT_LOG"
echo "  QR bridge log:$QR_LOG"
echo "  PIDs:"
for f in "$API_PID_FILE" "$FRONT_PID_FILE" "$QR_PID_FILE"; do
  [[ -f "$f" ]] && echo "    $(basename "$f" .pid): $(cat "$f")"
done

echo "[full-up] URLs:"
echo "  Frontend: http://localhost:${FRONTEND_PORT:-3039}"
echo "  API:      ${ASPNETCORE_URLS:-http://localhost:${API_PORT:-5000}}"
echo "  QR bridge:http://localhost:${QR_PORT:-3401}"
