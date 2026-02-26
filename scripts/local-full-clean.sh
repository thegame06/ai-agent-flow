#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$ROOT_DIR/.agent/run"

PORTS=("${FRONTEND_PORT:-3039}" "${API_PORT:-5000}" "${QR_PORT:-3401}" "${MCP_TEST_PORT:-3501}")

stop_pid_file() {
  local file="$1"
  if [[ -f "$file" ]]; then
    local pid
    pid="$(cat "$file")"
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      echo "[clean] killing pid $pid ($(basename "$file" .pid))"
      kill "$pid" || true
      sleep 1
      kill -9 "$pid" 2>/dev/null || true
    fi
    rm -f "$file"
  fi
}

kill_by_pattern() {
  local pattern="$1"
  if pgrep -f "$pattern" >/dev/null 2>&1; then
    echo "[clean] killing processes matching: $pattern"
    pkill -f "$pattern" || true
    sleep 1
    pkill -9 -f "$pattern" 2>/dev/null || true
  fi
}

kill_port_listeners() {
  for p in "${PORTS[@]}"; do
    local pids
    pids="$(lsof -tiTCP:"$p" -sTCP:LISTEN 2>/dev/null || true)"
    if [[ -n "$pids" ]]; then
      echo "[clean] port $p is busy; killing listener(s): $pids"
      for pid in $pids; do
        kill "$pid" 2>/dev/null || true
      done
      sleep 1
      for pid in $pids; do
        kill -9 "$pid" 2>/dev/null || true
      done
    fi
  done
}

echo "[clean] stopping known PID files"
stop_pid_file "$RUN_DIR/api.pid"
stop_pid_file "$RUN_DIR/frontend.pid"
stop_pid_file "$RUN_DIR/qr.pid"

echo "[clean] killing known process patterns"
kill_by_pattern "AgentFlow.Api.csproj"
kill_by_pattern "vite --strictPort --port ${FRONTEND_PORT:-3039}"
kill_by_pattern "whatsapp-qr-bridge"

echo "[clean] killing listeners on managed ports"
kill_port_listeners

if command -v docker >/dev/null 2>&1; then
  if [[ "${WIPE_DATA:-0}" == "1" ]]; then
    echo "[clean] docker compose down with volume wipe"
    MCP_TEST_PORT=${MCP_TEST_PORT:-3501} docker compose -f "$ROOT_DIR/docker-compose.test.yml" down -v --remove-orphans || true
  else
    echo "[clean] docker compose down (keeping volumes/data)"
    MCP_TEST_PORT=${MCP_TEST_PORT:-3501} docker compose -f "$ROOT_DIR/docker-compose.test.yml" down --remove-orphans || true
  fi
fi

echo "[clean] final port check"
for p in "${PORTS[@]}"; do
  if ss -ltn "( sport = :$p )" | grep -q LISTEN; then
    echo "[clean] WARNING: port $p still busy"
  else
    echo "[clean] OK: port $p free"
  fi
done

echo "[clean] done"