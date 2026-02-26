#!/usr/bin/env bash
set -u

# AgentFlow environment bootstrap (Ubuntu 24.04)
# Installs required tooling and reports what succeeded/failed.

LOG_DIR="${LOG_DIR:-./.agent/logs}"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/bootstrap-env-$(date +%Y%m%d-%H%M%S).log"

declare -a CHECKS=()
declare -a RESULTS=()
declare -a DETAILS=()

bold() { printf "\033[1m%s\033[0m\n" "$1"; }
info() { printf "[INFO] %s\n" "$1" | tee -a "$LOG_FILE"; }
warn() { printf "[WARN] %s\n" "$1" | tee -a "$LOG_FILE"; }
err()  { printf "[ERR ] %s\n" "$1" | tee -a "$LOG_FILE"; }

add_result() {
  CHECKS+=("$1")
  RESULTS+=("$2")
  DETAILS+=("$3")
}

run_cmd() {
  local title="$1"
  shift
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

command_exists() {
  command -v "$1" >/dev/null 2>&1
}

print_summary() {
  echo | tee -a "$LOG_FILE"
  bold "===== RESUMEN =====" | tee -a "$LOG_FILE"
  local i
  for i in "${!CHECKS[@]}"; do
    printf -- "- %-65s : %-4s %s\n" "${CHECKS[$i]}" "${RESULTS[$i]}" "${DETAILS[$i]}" | tee -a "$LOG_FILE"
  done
  echo | tee -a "$LOG_FILE"
  info "Log completo: $LOG_FILE"
}

ensure_ubuntu_24_04() {
  if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    source /etc/os-release
    if [[ "${ID:-}" != "ubuntu" ]]; then
      warn "Este script fue diseñado para Ubuntu. Detectado: ${ID:-unknown}"
      add_result "Verificar distro Ubuntu" "WARN" "detected ${ID:-unknown}"
      return 0
    fi
    if [[ "${VERSION_ID:-}" != "24.04" ]]; then
      warn "Recomendado para Ubuntu 24.04. Detectado: ${VERSION_ID:-unknown}"
      add_result "Verificar Ubuntu 24.04" "WARN" "detected ${VERSION_ID:-unknown}"
      return 0
    fi
    add_result "Verificar Ubuntu 24.04" "OK" ""
    return 0
  fi

  add_result "Verificar SO" "FAIL" "/etc/os-release no encontrado"
  return 1
}

require_sudo() {
  if sudo -n true >/dev/null 2>&1; then
    add_result "Validar sudo sin password" "OK" ""
    return 0
  fi

  info "Se requiere sudo para instalar paquetes. Puede pedir tu contraseña."
  if sudo -v >>"$LOG_FILE" 2>&1; then
    add_result "Validar sudo" "OK" ""
    return 0
  fi

  add_result "Validar sudo" "FAIL" "sin privilegios sudo"
  return 1
}

install_dotnet_9() {
  if command_exists dotnet; then
    local ver
    ver="$(dotnet --version 2>/dev/null || true)"
    if [[ "$ver" == 9.* || "$ver" == 10.* ]]; then
      add_result "dotnet SDK" "OK" "ya instalado ($ver)"
      return 0
    fi
    warn "dotnet detectado ($ver). Se intentará instalar SDK compatible (9.0/10.0)."
  fi

  run_cmd "apt-get update (pre .NET)" sudo apt-get update || return 1
  run_cmd "Instalar prerequisitos APT (.NET)" sudo apt-get install -y wget gpg apt-transport-https ca-certificates || return 1
  run_cmd "Descargar repo Microsoft" wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb || return 1
  run_cmd "Registrar repo Microsoft" sudo dpkg -i /tmp/packages-microsoft-prod.deb || return 1
  run_cmd "apt-get update (post repo Microsoft)" sudo apt-get update || return 1

  local sdk_pkg=""
  if apt-cache show dotnet-sdk-9.0 >/dev/null 2>&1; then
    sdk_pkg="dotnet-sdk-9.0"
  elif apt-cache show dotnet-sdk-10.0 >/dev/null 2>&1; then
    sdk_pkg="dotnet-sdk-10.0"
    warn "dotnet-sdk-9.0 no disponible en APT; instalando dotnet-sdk-10.0 (compatible para compilar net9.0)."
    add_result "Disponibilidad dotnet-sdk-9.0" "WARN" "no disponible; se usará 10.0"
  else
    add_result "Encontrar paquete dotnet SDK" "FAIL" "no existe dotnet-sdk-9.0 ni dotnet-sdk-10.0 en repos"
    return 1
  fi

  run_cmd "Instalar $sdk_pkg" sudo apt-get install -y "$sdk_pkg" || return 1

  if command_exists dotnet; then
    local ver2
    ver2="$(dotnet --version 2>/dev/null || true)"
    if [[ "$ver2" == 9.* || "$ver2" == 10.* ]]; then
      add_result "Verificar dotnet --version" "OK" "$ver2"
      return 0
    fi
    add_result "Verificar dotnet --version" "FAIL" "versión inesperada: $ver2"
    return 1
  fi

  add_result "Verificar dotnet" "FAIL" "comando no disponible"
  return 1
}

install_docker_optional() {
  if command_exists docker; then
    add_result "docker" "OK" "ya instalado"
    return 0
  fi

  run_cmd "apt-get update (Docker)" sudo apt-get update || return 1
  run_cmd "Instalar docker.io y docker-compose-v2" sudo apt-get install -y docker.io docker-compose-v2 || return 1
  run_cmd "Habilitar servicio Docker" sudo systemctl enable --now docker || return 1
  run_cmd "Agregar usuario al grupo docker" sudo usermod -aG docker "$USER" || return 1

  if command_exists docker; then
    add_result "Verificar docker --version" "OK" "$(docker --version 2>/dev/null || echo instalado)"
  else
    add_result "Verificar docker" "FAIL" "no disponible tras instalación"
    return 1
  fi

  return 0
}

project_checks() {
  local repo_root
  repo_root="$(cd "$(dirname "$0")/.." && pwd)"

  if [[ ! -f "$repo_root/AgentFlow.sln" ]]; then
    add_result "Verificar AgentFlow.sln" "FAIL" "no encontrado en $repo_root"
    return 1
  fi

  add_result "Verificar AgentFlow.sln" "OK" "$repo_root/AgentFlow.sln"

  if command_exists dotnet; then
    run_cmd "dotnet restore AgentFlow.sln" bash -lc "cd '$repo_root' && dotnet restore AgentFlow.sln" || true
    run_cmd "dotnet build AgentFlow.sln" bash -lc "cd '$repo_root' && dotnet build AgentFlow.sln -v minimal -nologo" || true
  else
    add_result "dotnet restore/build" "SKIP" "dotnet no disponible"
  fi

  if [[ -f "$repo_root/frontend/package.json" ]]; then
    if command_exists npm; then
      run_cmd "npm ci (frontend)" bash -lc "cd '$repo_root/frontend' && npm ci" || true
      if npm -s --prefix "$repo_root/frontend" run | grep -q " dev"; then
        add_result "frontend script dev" "OK" "npm run dev disponible"
      else
        add_result "frontend script dev" "WARN" "npm run dev no detectado"
      fi
    else
      add_result "npm" "FAIL" "no disponible"
    fi
  else
    add_result "frontend/package.json" "SKIP" "no encontrado"
  fi
}

main() {
  bold "AgentFlow bootstrap iniciado"
  info "Log: $LOG_FILE"

  ensure_ubuntu_24_04 || true
  require_sudo || {
    err "No se pudo validar sudo. Abortando instalación."
    print_summary
    exit 1
  }

  install_dotnet_9 || warn "Fallo instalando .NET 9"

  if [[ "${INSTALL_DOCKER:-1}" == "1" ]]; then
    install_docker_optional || warn "Fallo instalando Docker"
  else
    add_result "docker" "SKIP" "INSTALL_DOCKER=0"
  fi

  project_checks
  print_summary

  echo
  bold "Siguiente paso"
  echo "- Si Docker se instaló por primera vez: cierra sesión y vuelve a entrar para usarlo sin sudo."
  echo "- Luego ejecuta: dotnet --info && cd $(cd "$(dirname "$0")/.." && pwd) && dotnet test AgentFlow.sln -v minimal"
}

main "$@"
