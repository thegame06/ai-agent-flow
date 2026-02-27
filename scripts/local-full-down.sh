#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$ROOT_DIR/.agent/run"

stop_pid_file() {
  local file="$1"
  if [[ -f "$file" ]]; then
    local pid
    pid="$(cat "$file")"
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      echo "[full-down] Stopping PID $pid ($(basename "$file" .pid))"
      kill "$pid" || true
    fi
    rm -f "$file"
  fi
}

stop_pid_file "$RUN_DIR/api.pid"
stop_pid_file "$RUN_DIR/frontend.pid"
stop_pid_file "$RUN_DIR/qr.pid"

if command -v docker >/dev/null 2>&1; then
  echo "[full-down] Stopping infra containers (keeping volumes/data)"
  docker compose -f "$ROOT_DIR/docker-compose.local.yml" down --remove-orphans || true
fi

echo "[full-down] Done."
