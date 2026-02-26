#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "[global-4-5] 1) Validate no-mock guard"
bash "$ROOT_DIR/scripts/quality/no-mock-runtime.sh"

echo "[global-4-5] 2) Start MCP local test server (background)"
cd "$ROOT_DIR/tools/mcp-test-server"
npm install >/dev/null
PORT=${MCP_TEST_PORT:-3501} MCP_TEST_API_KEY=${MCP_TEST_API_KEY:-} npm start > /tmp/mcp-test-server.log 2>&1 &
MCP_PID=$!
trap 'kill $MCP_PID 2>/dev/null || true' EXIT
sleep 2

echo "[global-4-5] 3) Validate MCP endpoints"
BASE_URL="http://localhost:${MCP_TEST_PORT:-3501}" ./validate-local.sh

echo "[global-4-5] 4) Execute integration contracts for MAF + MCP"
cd "$ROOT_DIR"
dotnet test tests/AgentFlow.Tests.Integration/AgentFlow.Tests.Integration.csproj -v minimal --filter "FullyQualifiedName~MafAndMcpContractsTests"

echo "[global-4-5] OK: Global #4 (MCP real) + #5 (MAF/A2A contract) validated locally"
