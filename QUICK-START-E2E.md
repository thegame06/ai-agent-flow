# 🚀 Quick Start: Frontend-Backend E2E Testing

## ⚡ Inicio Rápido (5 minutos)

### 1. Iniciar Backend
```powershell
cd c:\labs\aiagents
make up-local-full
```

Espera a ver:
```
✅ MongoDB running
✅ Qdrant running
✅ AgentFlow API listening on http://localhost:5183
```

---

### 2. Verificar Backend
```powershell
# Verificar salud
Invoke-WebRequest http://localhost:5183/health

# O usar el script automático
.\scripts\test\verify-frontend-backend.ps1
```

Deberías ver:
```
✅ Backend está saludable (200 OK)
✅ Endpoint de intenciones disponible (200 OK)
📊 Intenciones configuradas: 30+
```

---

### 3. Iniciar Frontend
```powershell
cd frontend\aiagent_flow
npm run dev
```

Espera a ver:
```
VITE v6.0.6  ready in 1234 ms
➜  Local:   http://localhost:3039/
➜  Network: use --host to expose
```

---

### 4. Abrir Navegador

**Intents Management**: http://localhost:3039/dashboard/intents

![Intents Page](https://via.placeholder.com/800x400?text=IntentsPage)

Deberías ver:
- ✅ Lista de intenciones cargadas del backend
- ✅ Botón "Create Intent" funcional
- ✅ Botones de editar/eliminar/toggle en cada intención

**Playground**: http://localhost:3039/dashboard/intents/playground

![Playground Page](https://via.placeholder.com/800x400?text=PlaygroundPage)

Deberías ver:
- ✅ Campo de texto para escribir mensajes
- ✅ Botón "Classify Intent" funcional
- ✅ Resultados mostrando intent clasificado, score, y candidatos

**Inbox**: http://localhost:3039/dashboard/inbox

![Inbox Page](https://via.placeholder.com/800x400?text=InboxPage)

Deberías ver:
- ✅ Cards de estadísticas (Total, Awaiting, Classified, etc.)
- ✅ Tabla de conversaciones
- ✅ Filtros por estado y confianza

---

## 🧪 Tests Rápidos

### Test 1: Crear Intención
1. Ir a http://localhost:3039/dashboard/intents
2. Click en "Create Intent"
3. Llenar:
   - Key: `test_greeting`
   - Name: `Greeting Test`
   - Description: `User says hello`
   - Category: `Customer Service`
   - Examples: `["Hello", "Hi there", "Good morning"]`
4. Click "Save"
5. ✅ Debería aparecer en la lista

### Test 2: Clasificar Mensaje
1. Ir a http://localhost:3039/dashboard/intents/playground
2. Escribir: `"I want to apply for a personal loan"`
3. Click "Classify Intent"
4. ✅ Debería mostrar:
   - Intent: `loan_application`
   - Confidence: `High`
   - Score: `> 0.80`
   - Lista de candidatos alternativos

### Test 3: Ver Conversaciones
1. Ir a http://localhost:3039/dashboard/inbox
2. ✅ Debería mostrar estadísticas y tabla
3. Filtrar por "State: Classified"
4. ✅ Solo conversaciones clasificadas deben aparecer

---

## 🐛 Troubleshooting

### ❌ Backend no responde
```powershell
# Verificar que Docker esté corriendo
docker ps

# Re-iniciar servicios
make down-local-full
make up-local-full
```

### ❌ Frontend muestra "Error al cargar"
1. Abrir DevTools (F12) → Console
2. Buscar errores de red (Status 500, 404, CORS)
3. Verificar que backend esté en http://localhost:5183
4. Revisar `.env.local`:
   ```env
   VITE_SERVER_URL=http://localhost:5183
   ```

### ❌ "Cannot find module 'date-fns'"
```powershell
cd frontend\aiagent_flow
npm install
npm run dev
```

### ❌ CORS Error
Verificar en `src/AgentFlow.Api/Program.cs`:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3039")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

---

## 📊 Endpoints Disponibles

### Intents
- `GET /api/v1/tenants/tenant-1/intent-routing/rules` - Lista
- `POST /api/v1/tenants/tenant-1/intent-routing/rules` - Crear
- `PUT /api/v1/tenants/tenant-1/intent-routing/rules/{id}` - Actualizar
- `DELETE /api/v1/tenants/tenant-1/intent-routing/rules/{id}` - Eliminar
- `POST /api/v1/tenants/tenant-1/intent-routing/rules/{id}/enable` - Toggle

### Classification
- `POST /api/v1/tenants/tenant-1/intent-routing/classify` - Clasificar mensaje

### Inbox
- `GET /api/v1/tenants/tenant-1/intent-routing/conversations` - Lista
- `GET /api/v1/tenants/tenant-1/intent-routing/stats` - Estadísticas
- `POST /api/v1/tenants/tenant-1/intent-routing/conversations/{id}/reassign` - Reasignar
- `POST /api/v1/tenants/tenant-1/intent-routing/conversations/{id}/resolve` - Resolver

### Health
- `GET /health` - Salud del backend

---

## 🎯 Expected Results

### Intents Page
- ✅ 30+ intenciones base cargadas
- ✅ Categorías: Sales, Customer Service, Support, Technical, etc.
- ✅ Todos los botones funcionales

### Playground Page
- ✅ Clasificación en tiempo real
- ✅ Scores y candidatos mostrados
- ✅ Explanation con factores de scoring

### Inbox Page
- ✅ Estadísticas actualizadas
- ✅ Conversaciones filtradas correctamente
- ✅ Acciones de reassign/resolve funcionan

---

## 📚 Documentación Completa

- **Guía Detallada**: `frontend/aiagent_flow/FRONTEND-BACKEND-INTEGRATION-COMPLETE.md`
- **Resumen Ejecutivo**: `FRONTEND-INTEGRATION-SUMMARY.md`
- **Scripts de Verificación**: `scripts/test/verify-frontend-backend.ps1`

---

## 🎉 ¡Listo!

El sistema está completamente funcional y listo para testing E2E.

**Happy Testing! 🚀**

---

*AgentFlow Platform - Frontend Expert Mode*
