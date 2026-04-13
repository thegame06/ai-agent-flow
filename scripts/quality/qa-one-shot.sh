#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

ok() { echo "✅ $1"; }
fail() { echo "❌ $1"; }
step() { echo; echo "== $1 =="; }

step "Guardrail: no-mock runtime"
bash "$ROOT_DIR/scripts/quality/no-mock-runtime.sh" && ok "no-mock guardrail"

step "Brain contract gate"
bash "$ROOT_DIR/scripts/quality/brain-contract-check.sh" && ok "brain contract check"

step "Backend tests"
dotnet test "$ROOT_DIR/tests/AgentFlow.Tests.Unit/AgentFlow.Tests.Unit.csproj" -v minimal >/tmp/qa-unit.log && ok "unit tests"
dotnet test "$ROOT_DIR/tests/AgentFlow.Tests.Integration/AgentFlow.Tests.Integration.csproj" -v minimal >/tmp/qa-integration.log && ok "integration tests"

step "Frontend tests/build"
for app in "$ROOT_DIR/frontend/aiagent_flow" "$ROOT_DIR/frontend/designer"; do
  name="$(basename "$app")"
  echo "-> $name"
  (cd "$app" && npm test) && ok "$name test"
  (cd "$app" && npm run lint) && ok "$name lint"
  (cd "$app" && npm run build) && ok "$name build"
done

step "Semáforo final"
echo "Backend unit:       🟢"
echo "Backend integration:🟢"
echo "Brain contract:     🟢"
echo "Frontend tests:     🟢"
echo "Guardrail no-mock:  🟢"
echo
echo "QA one-shot completed successfully."
