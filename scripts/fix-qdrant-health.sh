#!/bin/bash
# fix-qdrant-health.sh - Fix rápido para Qdrant health check

echo "🔧 Aplicando fix para Qdrant health check..."

# Detener todo
echo "1. Deteniendo containers..."
docker compose -f docker-compose.local.yml down

# Verificar que docker-compose.local.yml tiene el fix
if grep -q "timeout 2 bash -c" docker-compose.local.yml; then
    echo "✅ Health check ya está actualizado"
else
    echo "❌ Health check no está actualizado"
    echo "   Necesitas aplicar el fix manualmente"
    exit 1
fi

# Iniciar solo Qdrant primero
echo ""
echo "2. Iniciando Qdrant solo..."
docker compose -f docker-compose.local.yml up -d qdrant-local

# Esperar y verificar
echo ""
echo "3. Esperando 30 segundos..."
sleep 30

# Ver estado
echo ""
echo "4. Verificando estado..."
docker ps -a --filter name=qdrant

# Ver logs
echo ""
echo "5. Últimos logs de Qdrant:"
docker logs --tail 20 agentflow-qdrant-local

# Verificar health
echo ""
echo "6. Estado del health check:"
docker inspect agentflow-qdrant-local --format='{{.State.Health.Status}}'

# Si está healthy, iniciar el resto
health=$(docker inspect agentflow-qdrant-local --format='{{.State.Health.Status}}')
if [[ "$health" == "healthy" ]]; then
    echo ""
    echo "✅ Qdrant está healthy! Iniciando el resto..."
    docker compose -f docker-compose.local.yml up -d
    echo ""
    echo "✅ Stack completo iniciado!"
else
    echo ""
    echo "❌ Qdrant aún no está healthy: $health"
    echo ""
    echo "Opciones:"
    echo "1. Esperar más tiempo y revisar logs:"
    echo "   docker logs -f agentflow-qdrant-local"
    echo ""
    echo "2. Desactivar health check completamente (editar docker-compose.local.yml)"
    echo ""
    echo "3. Usar puerto directo sin health check:"
    echo "   Comenta la sección 'healthcheck:' de qdrant-local"
fi
