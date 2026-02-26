#!/usr/bin/env bash
set -u

LOG_DIR="${LOG_DIR:-./.agent/logs}"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/verify-solution-$(date +%Y%m%d-%H%M%S).log"

declare -a CHECKS=()
declare -a RESULTS=()
declare -a DETAILS=()

info() { printf "[INFO] %s\n" "$1" | tee -a "$LOG_FILE"; }
warn() { printf "[WARN] %s\n" "$1" | tee -a "$LOG_FILE"; }
add_result() { CHECKS+=("$1"); RESULTS+=("$2"); DETAILS+=("$3"); }

run_cmd() {
  local title="$1"; shift
  info "$title"
  if "$@" >>"$LOG_FILE" 2>&1; then
    add_result "$title" "OK" ""
    return 0
  else
    local code=$?
    add_result "$title" "FAIL" "exit code $code"
    return $code
  fi
}

print_summary() {
  echo | tee -a "$LOG_FILE"
  echo "===== RESUMEN VERIFY =====" | tee -a "$LOG_FILE"
  local i
  for i in "${!CHECKS[@]}"; do
    printf -- "- %-45s : %-4s %s\n" "${CHECKS[$i]}" "${RESULTS[$i]}" "${DETAILS[$i]}" | tee -a "$LOG_FILE"
  done
  echo | tee -a "$LOG_FILE"
  info "Log completo: $LOG_FILE"
}

main() {
  local repo_root
  repo_root="$(cd "$(dirname "$0")/.." && pwd)"

  if [[ ! -f "$repo_root/AgentFlow.sln" ]]; then
    add_result "Verificar AgentFlow.sln" "FAIL" "No encontrado en $repo_root"
    print_summary
    exit 1
  fi
  add_result "Verificar AgentFlow.sln" "OK" "$repo_root/AgentFlow.sln"

  if ! command -v dotnet >/dev/null 2>&1; then
    add_result "Verificar dotnet" "FAIL" "dotnet no instalado"
    print_summary
    exit 1
  fi

  local sdk
  sdk="$(dotnet --version 2>/dev/null || true)"
  add_result "dotnet --version" "OK" "$sdk"

  run_cmd "dotnet restore" bash -lc "cd '$repo_root' && dotnet restore AgentFlow.sln" || true
  run_cmd "dotnet build" bash -lc "cd '$repo_root' && dotnet build AgentFlow.sln -v minimal -nologo" || true
  run_cmd "dotnet test" bash -lc "cd '$repo_root' && dotnet test AgentFlow.sln -v minimal" || true

  if [[ -d "$repo_root/frontend" ]]; then
    if [[ -f "$repo_root/frontend/package.json" ]]; then
      if command -v npm >/dev/null 2>&1; then
        run_cmd "npm ci (frontend)" bash -lc "cd '$repo_root/frontend' && npm ci" || true
      else
        add_result "npm" "WARN" "npm no disponible"
      fi
    else
      add_result "frontend/package.json" "SKIP" "no encontrado"
    fi
  else
    add_result "frontend" "SKIP" "directorio no encontrado"
  fi

  print_summary
}

main "$@"
