#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="${ROOT_DIR}/docker-compose.test.yml"
RESULTS_DIR="${ROOT_DIR}/.agent/test-results"

export ASPNETCORE_ENVIRONMENT="Development"
export ConnectionStrings__MongoDB="mongodb://localhost:27018"
export MongoDB__DatabaseName="${MONGODB_TEST_DB:-agentflow_test}"
export ConnectionStrings__Redis="localhost:6380"
export MCP_TEST_API_KEY="${MCP_TEST_API_KEY:-}"

mkdir -p "$RESULTS_DIR"

up() {
  echo "[test-env] Starting ephemeral infra..."
  docker compose -f "$COMPOSE_FILE" up -d --wait
  echo "[test-env] Infra ready: mongo(27018), redis(6380), mcp(3501)"
}

down() {
  echo "[test-env] Stopping ephemeral infra..."
  docker compose -f "$COMPOSE_FILE" down -v --remove-orphans
  echo "[test-env] Infra removed"
}

run_backend_tests() {
  echo "[tests] Running backend unit tests..."
  dotnet test "$ROOT_DIR/tests/AgentFlow.Tests.Unit/AgentFlow.Tests.Unit.csproj" \
    -v minimal \
    --logger "trx;LogFileName=unit-tests.trx" \
    --results-directory "$RESULTS_DIR"

  echo "[tests] Running backend integration tests..."
  dotnet test "$ROOT_DIR/tests/AgentFlow.Tests.Integration/AgentFlow.Tests.Integration.csproj" \
    -v minimal \
    --logger "trx;LogFileName=integration-tests.trx" \
    --results-directory "$RESULTS_DIR"
}

run_frontend_checks() {
  echo "[tests] Running frontend checks..."

  for app in "$ROOT_DIR/frontend/aiagent_flow" "$ROOT_DIR/frontend/designer"; do
    if [[ -f "$app/package.json" ]]; then
      echo "[tests] -> $(basename "$app"): npm ci"
      (cd "$app" && npm ci)

      if (cd "$app" && npm run | grep -q " lint"); then
        echo "[tests] -> $(basename "$app"): npm run lint"
        (cd "$app" && npm run lint)
      fi

      if (cd "$app" && npm run | grep -q " build"); then
        echo "[tests] -> $(basename "$app"): npm run build"
        (cd "$app" && npm run build)
      fi

      if (cd "$app" && npm run | grep -q " test"); then
        echo "[tests] -> $(basename "$app"): npm test -- --watch=false"
        (cd "$app" && npm test -- --watch=false)
      else
        echo "[tests] -> $(basename "$app"): no test script (skipped)"
      fi
    fi
  done
}

print_summary() {
  echo
  echo "=== TEST SUMMARY ==="
  echo "Results directory: $RESULTS_DIR"
  ls -1 "$RESULTS_DIR"/*.trx 2>/dev/null || echo "No .trx files generated"
  echo "Infra endpoints used:"
  echo "- MongoDB: $ConnectionStrings__MongoDB / DB=$MongoDB__DatabaseName"
  echo "- Redis: $ConnectionStrings__Redis"
  echo "- MCP test server: http://localhost:3501"
}

cmd="${1:-run}"

case "$cmd" in
  up)
    up
    ;;
  down)
    down
    ;;
  run)
    trap down EXIT
    up
    run_backend_tests
    run_frontend_checks
    print_summary
    ;;
  *)
    echo "Usage: $0 {run|up|down}" >&2
    exit 1
    ;;
esac
