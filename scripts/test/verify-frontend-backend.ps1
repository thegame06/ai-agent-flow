# Frontend-Backend Integration Test Script (PowerShell)
# Verifica que el backend esté corriendo y las APIs estén disponibles

$BACKEND_URL = "http://localhost:5183"
$TENANT_ID = "tenant-1"

Write-Host "🔍 Verificando conexión con el backend..." -ForegroundColor Cyan
Write-Host ""

# Verificar Health Endpoint
Write-Host "1️⃣ Verificando /health..." -ForegroundColor Yellow

try {
    $healthResponse = Invoke-WebRequest -Uri "$BACKEND_URL/health" -Method Get -ErrorAction Stop
    if ($healthResponse.StatusCode -eq 200) {
        Write-Host "✅ Backend está saludable (200 OK)" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ Backend no responde correctamente" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Ejecuta: make up-local-full" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Verificar Intent Routing Rules
Write-Host "2️⃣ Verificando /api/v1/tenants/$TENANT_ID/intent-routing/rules..." -ForegroundColor Yellow

try {
    $rulesResponse = Invoke-WebRequest -Uri "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/rules" -Method Get -ErrorAction Stop
    if ($rulesResponse.StatusCode -eq 200) {
        Write-Host "✅ Endpoint de intenciones disponible (200 OK)" -ForegroundColor Green
        $rules = $rulesResponse.Content | ConvertFrom-Json
        Write-Host "   📊 Intenciones configuradas: $($rules.Count)" -ForegroundColor Cyan
    }
} catch {
    Write-Host "⚠️  Endpoint de intenciones no disponible" -ForegroundColor Yellow
    Write-Host "   Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Yellow
}

Write-Host ""

# Verificar Conversations
Write-Host "3️⃣ Verificando /api/v1/tenants/$TENANT_ID/intent-routing/conversations..." -ForegroundColor Yellow

try {
    $convResponse = Invoke-WebRequest -Uri "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/conversations" -Method Get -ErrorAction Stop
    if ($convResponse.StatusCode -eq 200) {
        Write-Host "✅ Endpoint de conversaciones disponible (200 OK)" -ForegroundColor Green
        $conversations = $convResponse.Content | ConvertFrom-Json
        Write-Host "   📊 Conversaciones en inbox: $($conversations.Count)" -ForegroundColor Cyan
    }
} catch {
    Write-Host "⚠️  Endpoint de conversaciones no disponible" -ForegroundColor Yellow
}

Write-Host ""

# Verificar Stats
Write-Host "4️⃣ Verificando /api/v1/tenants/$TENANT_ID/intent-routing/stats..." -ForegroundColor Yellow

try {
    $statsResponse = Invoke-WebRequest -Uri "$BACKEND_URL/api/v1/tenants/$TENANT_ID/intent-routing/stats" -Method Get -ErrorAction Stop
    if ($statsResponse.StatusCode -eq 200) {
        Write-Host "✅ Endpoint de estadísticas disponible (200 OK)" -ForegroundColor Green
    }
} catch {
    Write-Host "⚠️  Endpoint de estadísticas no disponible" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "✅ Verificación completa!" -ForegroundColor Green
Write-Host ""
Write-Host "📱 Frontend: http://localhost:3039" -ForegroundColor Cyan
Write-Host "🔧 Backend: $BACKEND_URL" -ForegroundColor Cyan
Write-Host "🏢 Tenant: $TENANT_ID" -ForegroundColor Cyan
Write-Host ""
Write-Host "🚀 Puedes iniciar el frontend:" -ForegroundColor Yellow
Write-Host "   cd frontend\aiagent_flow" -ForegroundColor White
Write-Host "   npm run dev" -ForegroundColor White
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
