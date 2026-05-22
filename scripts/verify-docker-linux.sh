#!/bin/bash
# verify-docker-linux.sh
# Script de verificación para AgentFlow en Linux

set -euo pipefail

echo "🔍 Verificando Docker Stack en Linux..."

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

success() {
    echo -e "${GREEN}✅${NC} $1"
}

error() {
    echo -e "${RED}❌${NC} $1"
}

warning() {
    echo -e "${YELLOW}⚠️${NC} $1"
}

# 1. Docker daemon
echo ""
echo "1. Docker Daemon..."
if systemctl is-active --quiet docker 2>/dev/null; then
    success "Docker daemon activo"
elif service docker status > /dev/null 2>&1; then
    success "Docker daemon activo (via service)"
elif docker info > /dev/null 2>&1; then
    success "Docker daemon activo"
else
    error "Docker daemon no está corriendo"
    echo "   Solución: sudo systemctl start docker"
    exit 1
fi

# 2. Containers
echo ""
echo "2. Containers..."
containers=("agentflow-mongo-local" "agentflow-redis-local" "agentflow-qdrant-local" "agentflow-nats-local" "agentflow-mcp-test")
all_healthy=true

for container in "${containers[@]}"; do
    echo -n "   $container... "
    if ! docker ps --format '{{.Names}}' | grep -q "^${container}$"; then
        error "No está corriendo"
        all_healthy=false
        continue
    fi
    
    status=$(docker inspect --format='{{.State.Health.Status}}' $container 2>/dev/null || echo "no-healthcheck")
    case $status in
        "healthy")
            success "Healthy"
            ;;
        "starting")
            warning "Iniciando..."
            all_healthy=false
            ;;
        "unhealthy")
            error "Unhealthy"
            all_healthy=false
            ;;
        "no-healthcheck")
            warning "Sin healthcheck (pero corriendo)"
            ;;
        *)
            error "Estado: $status"
            all_healthy=false
            ;;
    esac
done

# 3. Puertos
echo ""
echo "3. Puertos..."
check_port() {
    local port=$1
    local name=$2
    echo -n "   $name (puerto $port)... "
    
    # Try ss first, fallback to netstat, then lsof
    if command -v ss > /dev/null 2>&1; then
        if ss -tln 2>/dev/null | grep -q ":$port "; then
            success "Escuchando"
            return 0
        fi
    elif command -v netstat > /dev/null 2>&1; then
        if netstat -tln 2>/dev/null | grep -q ":$port "; then
            success "Escuchando"
            return 0
        fi
    elif command -v lsof > /dev/null 2>&1; then
        if sudo lsof -i :$port > /dev/null 2>&1; then
            success "Escuchando"
            return 0
        fi
    fi
    
    error "No escuchando"
    return 1
}

check_port 27018 "MongoDB"
check_port 6380 "Redis"
check_port 6333 "Qdrant REST"
check_port 6334 "Qdrant gRPC"
check_port 4222 "NATS"
check_port 8222 "NATS Monitor"
check_port 3501 "MCP Test"

# 4. Endpoints HTTP
echo ""
echo "4. Endpoints HTTP..."
check_endpoint() {
    local url=$1
    local name=$2
    echo -n "   $name... "
    
    if command -v curl > /dev/null 2>&1; then
        if curl -f -s "$url" > /dev/null 2>&1; then
            success "Responde"
            return 0
        fi
    elif command -v wget > /dev/null 2>&1; then
        if wget -q -O /dev/null "$url" 2>&1; then
            success "Responde"
            return 0
        fi
    fi
    
    error "No responde"
    return 1
}

check_endpoint "http://localhost:6333/readyz" "Qdrant"
check_endpoint "http://localhost:8222/healthz" "NATS Monitor"
check_endpoint "http://localhost:5000/health" "API" || warning "API no iniciada (normal si solo corriste docker compose)"
check_endpoint "http://localhost:3039" "Frontend" || warning "Frontend no iniciado (normal si solo corriste docker compose)"

# 5. Volúmenes
echo ""
echo "5. Volúmenes Docker..."
volumes=("mongo_local_data" "redis_local_data" "qdrant_local_data")
for vol in "${volumes[@]}"; do
    echo -n "   ${vol}... "
    if docker volume ls --format '{{.Name}}' | grep -q "${vol}$"; then
        success "Existe"
    else
        warning "No existe (se creará al iniciar)"
    fi
done

# 6. Recursos del sistema
echo ""
echo "6. Recursos del Sistema..."
echo -n "   Espacio en disco... "
disk_usage=$(df -h / | awk 'NR==2 {print $5}' | sed 's/%//')
if [ "$disk_usage" -lt 90 ]; then
    success "$disk_usage% usado"
else
    error "$disk_usage% usado (poco espacio!)"
fi

echo -n "   Memoria disponible... "
if command -v free > /dev/null 2>&1; then
    mem_available=$(free -m | awk 'NR==2 {print $7}')
    if [ "$mem_available" -gt 512 ]; then
        success "${mem_available}MB disponible"
    else
        warning "${mem_available}MB disponible (puede ser insuficiente)"
    fi
else
    warning "Comando 'free' no disponible"
fi

# Resumen final
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if $all_healthy; then
    success "✨ Verificación completada - Todo OK!"
    echo ""
    echo "Puedes continuar con:"
    echo "  make up-local-full    # Iniciar API + Frontend"
    echo "  o abrir: http://localhost:3039/dashboard/intents"
else
    warning "⚠️ Algunos componentes tienen problemas"
    echo ""
    echo "Para ver logs:"
    echo "  docker compose -f docker-compose.local.yml logs -f"
    echo ""
    echo "Para reiniciar:"
    echo "  docker compose -f docker-compose.local.yml restart"
    echo ""
    echo "Consulta: docs/TROUBLESHOOTING-LINUX.md"
fi
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
