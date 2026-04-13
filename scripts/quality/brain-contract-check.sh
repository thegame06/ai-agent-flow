#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

dotnet test "$ROOT_DIR/tests/AgentFlow.Tests.Integration/AgentFlow.Tests.Integration.csproj" \
  --filter "FullyQualifiedName~BrainContractGoldenTests|FullyQualifiedName~MafAndMcpContractsTests" \
  -v minimal
