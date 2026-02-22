# 🔌 Integración API: Backend ↔️ Frontend

## ✅ Estado: **COMPLETADO**

Integración completa entre el backend (.NET 9) y el frontend (React + Redux) usando arquitectura multi-tenant y autenticación JWT.

---

## 🏗️ Arquitectura de Integración

```
┌──────────────────┐      HTTP/REST      ┌──────────────────┐
│                  │ ◄─────────────────► │                  │
│  React Frontend  │    axios + Redux    │  .NET Backend    │
│  (Port 8081)     │      JWT Auth       │  (Port 5183)     │
│                  │      CORS OK        │                  │
└──────────────────┘                     └──────────────────┘
```

---

## 📡 Configuración de Endpoints

### Backend: `AgentFlow.Api`
- **URL Base**: `http://localhost:5183`
- **Autenticación**: JWT Bearer Token
- **Formato**: JSON (camelCase)
- **CORS**: Habilitado para `localhost:8081`
- **Swagger**: `http://localhost:5183/swagger`

### Frontend: `aiagent_flow`
- **URL Base**: `http://localhost:8081`
- **API Client**: Axios con interceptors
- **State Management**: Redux Toolkit + createAsyncThunk
- **Tenant ID**: `tenant-1` (hardcoded en thunks, TODO: obtener de auth)

---

## 🗂️ Mapeo de Endpoints

### 1️⃣ **Agents** (`/api/v1/tenants/{tenantId}/agents`)

| Método | Endpoint | Frontend | Backend Controller |
|--------|----------|----------|-------------------|
| GET    | `/agents` | `fetchAgents()` | `AgentsController.GetAgents()` |
| GET    | `/agents/{id}` | `fetchAgentDetail()` | `AgentsController.GetAgent()` |
| POST   | `/agents` | `saveAgent()` (create) | `AgentsController.CreateAgent()` |
| PUT    | `/agents/{id}` | `saveAgent()` (update) | `AgentsController.UpdateAgent()` |
| DELETE | `/agents/{id}` | `deleteAgent()` | `AgentsController.DeleteAgent()` |
| POST   | `/agents/{id}/clone` | `cloneAgent()` | `AgentsController.CloneAgent()` |
| POST   | `/agents/{id}/publish` | `publishAgent()` | `AgentsController.PublishAgent()` |

**Redux Slice**: `frontend/aiagent_flow/src/aiagentflow/pages/agents/Redux/Slice.ts`  
**Thunks Designer**: `frontend/aiagent_flow/src/aiagentflow/pages/agents/Designer/designerThunks.ts`

---

### 2️⃣ **Executions** (`/api/v1/tenants/{tenantId}`)

| Método | Endpoint | Frontend | Backend Controller |
|--------|----------|----------|-------------------|
| GET    | `/executions` | `fetchExecutions()` | `AgentExecutionsController.GetAllExecutionsAsync()` |
| GET    | `/agents/{agentId}/executions` | - | `AgentExecutionsController.GetHistoryAsync()` |
| POST   | `/agents/{agentId}/trigger` | - | `AgentExecutionsController.TriggerAsync()` |

**Redux Slice**: `frontend/aiagent_flow/src/aiagentflow/pages/executions/Redux/Slice.ts`

---

### 3️⃣ **Checkpoints (HITL)** (`/api/v1/tenants/{tenantId}/checkpoints`)

| Método | Endpoint | Frontend | Backend Controller |
|--------|----------|----------|-------------------|
| GET    | `/checkpoints` | `fetchCheckpoints()` | `CheckpointsController.GetPending()` |
| POST   | `/checkpoints/{executionId}/decide` | `decideCheckpoint()` | `CheckpointsController.Decide()` |

**Redux Slice**: `frontend/aiagent_flow/src/aiagentflow/pages/checkpoints/checkpointSlice.ts`

---

### 4️⃣ **Tools / Extensions** (`/api/v1/extensions`)

| Método | Endpoint | Frontend | Backend Controller |
|--------|----------|----------|-------------------|
| GET    | `/extensions/tools` | `fetchTools()` | `ExtensionsController.GetTools()` |
| GET    | `/extensions/catalog` | - | `ExtensionsController.GetCatalogAsync()` |
| POST   | `/extensions/tools/{name}/invoke` | - | `ExtensionsController.InvokeToolAsync()` |

**Redux Slice**: `frontend/aiagent_flow/src/aiagentflow/pages/tools/Redux/Slice.ts`

---

### 5️⃣ **Models** (`/api/v1/model-routing`)

| Método | Endpoint | Frontend | Backend Controller |
|--------|----------|----------|-------------------|
| GET    | `/model-routing/models` | `fetchModels()` | `ModelRoutingController.GetAvailableModels()` |
| GET    | `/model-routing/models/healthy` | `fetchHealthyModels()` | `ModelRoutingController.GetHealthyModels()` |

**Redux Slice**: `frontend/aiagent_flow/src/aiagentflow/pages/models/Redux/Slice.ts`

---

### 6️⃣ **Policies** (`/api/v1/tenants/{tenantId}/policies`)

| Método | Endpoint | Frontend | Backend Controller |
|--------|----------|----------|-------------------|
| GET    | `/policies` | - | `PoliciesController.GetPolicies()` |

**Redux Slice**: ❌ No implementado (usar axios directo si es necesario)

---

### 7️⃣ **Audit** (`/api/v1/tenants/{tenantId}/audit`)

| Método | Endpoint | Frontend | Backend Controller |
|--------|----------|----------|-------------------|
| GET    | `/audit` | - | `AuditController.GetAuditLogs()` |

**Redux Slice**: ❌ No implementado (usar axios directo si es necesario)

---

## 🔐 Autenticación

### Estado Actual: **Mock / Bypass**
- `auth.skip: false` en `global-config.ts` pero sin validación real
- Todos los endpoints requieren `[Authorize]` en el backend
- **Tenant ID hardcoded**: `tenant-1` en todos los thunks

### TODO: Implementar Auth Real
1. ✅ Backend tiene `AuthController` con JWT
2. ❌ Frontend no está enviando tokens JWT
3. ❌ No hay refresh token logic
4. ❌ TenantContextAccessor necesita extraer tenantId del token

**Archivos críticos**:
- Backend: [src/AgentFlow.Api/Controllers/AuthController.cs](c:\labs\aiagents\src\AgentFlow.Api\Controllers\AuthController.cs)
- Frontend: `src/lib/axios.ts` (interceptors para agregar Bearer token)
- Security: [src/AgentFlow.Security/](c:\labs\aiagents\src\AgentFlow.Security)

---

## 📦 DTOs (Data Transfer Objects)

### Backend → Frontend

**AgentListItemDto** (lista de agentes):
```csharp
{
  "id": "string",
  "name": "string",
  "description": "string",
  "status": "Draft|Published|Archived",
  "version": 1,
  "createdAt": "2026-02-22T...",
  "updatedAt": "2026-02-22T...",
  "tags": ["string"]
}
```

**AgentDetailDto** (detalle completo para Designer):
```csharp
{
  "id": "string",
  "name": "Customer Support Agent",
  "brain": {
    "primaryModel": "gpt-4o-mini",
    "fallbackModel": "gpt-4o",
    "systemPrompt": "...",
    "temperature": 0.7,
    "maxResponseTokens": 4096
  },
  "loop": {
    "maxSteps": 25,
    "timeoutPerStepMs": 30000,
    "maxRetries": 3,
    "requireHumanApproval": false
  },
  "memory": {
    "workingMemory": true,
    "longTermMemory": false,
    "vectorMemory": true
  },
  "steps": [...],
  "tools": [...]
}
```

**Archivo de DTOs**: [src/AgentFlow.Api/Controllers/DTOs/AgentDesignerDtos.cs](c:\labs\aiagents\src\AgentFlow.Api\Controllers\DTOs\AgentDesignerDtos.cs)

---

## 🧪 Testing de Integración

### 1. **Backend**
```powershell
cd c:\labs\aiagents\src\AgentFlow.Api
dotnet run
```
- ✅ Swagger: http://localhost:5183/swagger
- ✅ Health: http://localhost:5183/health

### 2. **Frontend**
```powershell
cd c:\labs\aiagents\frontend\aiagent_flow
npm run dev
```
- ✅ App: http://localhost:8081
- ✅ Vite HMR: Habilitado

### 3. **Test Manual**
1. Abrir http://localhost:8081
2. Navegar a **Agents** → Ver lista de agentes (seed data)
3. Abrir **Agent Designer** → Editar "Customer Support Agent"
4. Verificar que carguen los datos del backend
5. Cambiar configuración → Guardar → Verificar en MongoDB

---

## 🚨 Issues Conocidos

| # | Issue | Ubicación | Prioridad | Fix |
|---|-------|-----------|-----------|-----|
| 1 | Tenant ID hardcoded `tenant-1` | `designerThunks.ts:7` | 🔴 Alta | Extraer de JWT token |
| 2 | Sin Bearer token en requests | `axios.ts` | 🔴 Alta | Agregar interceptor |
| 3 | Auth.skip no funciona | `global-config.ts` | 🟡 Media | Implementar guard en routes |
| 4 | CORS solo permite localhost | `Program.cs:45` | 🟢 Baja | Agregar env.ALLOWED_ORIGINS |
| 5 | No hay Redux slice para Audit | `pages/audit/` | 🟢 Baja | Crear slice o usar axios directo |

---

## 🎯 Próximos Pasos

### Críticos (2-4 horas)
1. **Autenticación Real**:
   - Implementar login en frontend
   - Agregar Bearer token a axios interceptor
   - Extraer tenantId del JWT en backend
   
2. **Testing E2E**:
   - Crear suite de tests con Playwright
   - Validar flujo completo: Login → Create Agent → Execute → View Results

### Mejoras (1-2 días)
3. **RTK Query Migration**:
   - Reemplazar createAsyncThunk por RTK Query
   - Cacheo automático de datos
   - Optimistic updates

4. **Error Handling**:
   - Toast notifications para errores de API
   - Retry logic para requests fallidos
   - Offline detection

---

## 📚 Documentación de Referencia

| Recurso | Link |
|---------|------|
| Backend API Spec | [DESIGNER-BACKEND-API.md](c:\labs\aiagents\docs\DESIGNER-BACKEND-API.md) |
| Frontend Integration | [FRONTEND-BACKEND-INTEGRATION.md](c:\labs\aiagents\docs\FRONTEND-BACKEND-INTEGRATION.md) |
| Security Model | [AgentFlow.Security](c:\labs\aiagents\src\AgentFlow.Security) |
| Demo Instructions | [DEMO-INSTRUCTIONS.md](c:\labs\aiagents\DEMO-INSTRUCTIONS.md) |

---

## ✅ Checklist de Integración

- [x] `.env.development` creado con `VITE_SERVER_URL=http://localhost:5183`
- [x] CORS configurado en backend para puerto `8081`
- [x] Todos los endpoints documentados en `axios.ts`
- [x] Redux slices creados para: Agents, Executions, Tools, Models, Checkpoints
- [x] DTOs alineados entre frontend y backend
- [x] SeedData implementado (2 agentes demo)
- [x] Frontend build sin errores
- [x] Backend build sin errores
- [ ] **TODO**: Implementar autenticación JWT real
- [ ] **TODO**: Testing E2E automatizado
- [ ] **TODO**: Deployment a staging

---

**Última actualización**: 2026-02-22  
**Estado**: ✅ Integración Core Completa | ⚠️ Pendiente Auth + Tests
