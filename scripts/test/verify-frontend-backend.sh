#!/bin/bash

# Frontend-Backend Integration Test Script
# Verifica que el backend esté corriendo y las APIs estén disponibles

set -e

BACKEND_URL="http://localhost:5183"
TENANT_ID="tenant-1"

echo "🔍 Verificando conexión con el backend..."
echo ""

# Verificar Health Endpoint
echo "1️⃣ Verificando /health..."
HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" $BACKEND_URL/health)

if [ "$HEALTH_STATUS" -eq 200 ]; then
    echo "✅ Backend está saludable (200 OK)"
else
    echo "❌ Backend no responde correctamente (Status: $HEALTH_STATUS)"
    echo "   Ejecuta: make up-local-full"
    exit 1
fi

echo ""

# Verificar Intent Routing Rules
echo "2️⃣ Verificando /api/v1/tenants/$TENANT_ID/intent-routing/rules..."
RULES_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/rules")

if [ "$RULES_STATUS" -eq 200 ]; then
    echo "✅ Endpoint de intenciones disponible (200 OK)"
    RULES_COUNT=$(curl -s "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/rules" | jq '. | length')
    echo "   📊 Intenciones configuradas: $RULES_COUNT"
else
    echo "⚠️  Endpoint de intenciones no disponible (Status: $RULES_STATUS)"
fi

echo ""

# Verificar Conversations
echo "3️⃣ Verificando /api/v1/tenants/$TENANT_ID/intent-routing/conversations..."
CONV_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/conversations")

if [ "$CONV_STATUS" -eq 200 ]; then
    echo "✅ Endpoint de conversaciones disponible (200 OK)"
    CONV_COUNT=$(curl -s "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/conversations" | jq '. | length')
    echo "   📊 Conversaciones en inbox: $CONV_COUNT"
else
    echo "⚠️  Endpoint de conversaciones no disponible (Status: $CONV_STATUS)"
fi

echo ""

# Verificar Stats
echo "4️⃣ Verificando /api/v1/tenants/$TENANT_ID/intent-routing/stats..."
STATS_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/stats")

if [ "$STATS_STATUS" -eq 200 ]; then
    echo "✅ Endpoint de estadísticas disponible (200 OK)"
else
    echo "⚠️  Endpoint de estadísticas no disponible (Status: $STATS_STATUS)"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Verificación completa!"
echo ""
echo "📱 Frontend: http://localhost:3039"
echo "🔧 Backend: $BACKEND_URL"
echo "🏢 Tenant: $TENANT_ID"
echo ""
echo "🚀 Puedes iniciar el frontend:"
echo "   cd frontend/aiagent_flow"
echo "   npm run dev"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
