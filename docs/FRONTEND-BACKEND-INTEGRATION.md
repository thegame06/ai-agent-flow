# Frontend-Backend Integration Guide

**Fecha**: Febrero 21, 2026  
**Status**: ✅ **Completado**  
**Versión**: 1.0

---

## 🎯 Arquitectura de Integración

### Stack Tecnológico

#### Frontend (`frontend/aiagent_flow`)
- **Framework**: React 18 + Vite 6
- **UI Library**: Material UI v6 (@mui/material)
- **State Management**: Redux Toolkit 2.11
- **HTTP Client**: Axios 1.7
- **Form Validation**: React Hook Form + Zod
- **Data Grid**: @mui/x-data-grid

#### Backend (`src/AgentFlow.Api`)
- **Framework**: ASP.NET Core 9.0
- **Language**: C# 13
- **Architecture**: Clean Architecture + DDD
- **API Pattern**: REST (JSON)
- **Auth**: JWT Bearer Tokens

---

## 📡 API Endpoints Integrados

### Base URL
```typescript
// .env
VITE_SERVER_URL=http://localhost:5000

// src/lib/axios.ts
const axiosInstance = axios.create({ baseURL: CONFIG.serverUrl });
```

### Agents Controller

| Method | Endpoint | Frontend Thunk | Propósito |
|---|---|---|---|
| `GET` | `/api/v1/tenants/{tenantId}/agents` | `fetchAgents` | Listar agentes (DataGrid) |
| `GET` | `/api/v1/tenants/{tenantId}/agents/{id}` | `fetchAgentById` | Detalle completo (Designer) |
| `POST` | `/api/v1/tenants/{tenantId}/agents` | `createAgent` | Crear nuevo agente |
| `PUT` | `/api/v1/tenants/{tenantId}/agents/{id}` | `updateAgent` | Actualizar agente |
| `DELETE` | `/api/v1/tenants/{tenantId}/agents/{id}` | `deleteAgent` | Eliminar (soft delete) |
| `POST` | `/api/v1/tenants/{tenantId}/agents/{id}/clone` | `cloneAgent` | Clonar agente |
| `POST` | `/api/v1/tenants/{tenantId}/agents/{id}/publish` | `publishAgent` | Publicar (Draft → Published) |
| `POST` | `/api/v1/tenants/{tenantId}/agents/{id}/preview` | `previewAgent` | Dry-run execution |

---

## 🗂️ Estructura de Directorios Frontend

```
frontend/aiagent_flow/src/aiagentflow/
├── pages/
│   └── agents/
│       ├── AgentsPage.tsx          # Vista principal (DataGrid)
│       ├── Hooks/
│       │   └── useAgents.ts        # Hook para listar agentes
│       ├── Redux/
│       │   └── Slice.ts            # Redux slice (CRUD completo)
│       └── Designer/
│           ├── AgentDesignerPage.tsx  # Editor visual
│           ├── types.ts               # TypeScript types
│           ├── designerSlice.ts       # State del designer
│           └── designerThunks.ts      # API calls (save, publish, clone, preview)
│
├── store/
│   └── index.ts                    # Store raíz (combineReducers)
│
└── lib/
    └── axios.ts                    # Axios instance con baseURL
```

---

## 📦 Redux State Management

### Store Root

```typescript
// src/aiagentflow/store/index.ts
import agentsReducer from '../pages/agents/Redux/Slice';
import designerReducer from '../pages/agents/Designer/designerSlice';

export const store = configureStore({
  reducer: {
    agents: agentsReducer,          // Estado general (lista, CRUD)
    designer: designerReducer,      // Estado del editor (draft, activeTab)
    // ... otros reducers
  },
});
```

### Agents Slice

**Estado**:
```typescript
{
  items: AgentListItemDto[],     // Lista de agentes (DataGrid)
  selectedAgent: AgentDetailDto | null,  // Agente seleccionado
  loading: boolean,
  error: string | null
}
```

**Operaciones Disponibles**:
```typescript
// Thunks (async)
fetchAgents(tenantId)
fetchAgentById({ tenantId, agentId })
createAgent({ tenantId, payload })
updateAgent({ tenantId, agentId, payload })
deleteAgent({ tenantId, agentId })
cloneAgent({ tenantId, agentId, newName, newDescription })
publishAgent({ tenantId, agentId })

// Actions (sync)
clearSelectedAgent()
```

### Designer Slice

**Estado**:
```typescript
{
  draft: AgentDefinitionDraft,   // Estado del agente en edición
  activeTab: number,             // Pestaña activa (General, Steps, Tools, etc.)
  isDirty: boolean,              // ¿Hay cambios sin guardar?
  saving: boolean,
  errors: Record<string, string>
}
```

**Operaciones Disponibles**:
```typescript
// Thunks (async)
fetchAgentDetail(agentId)
saveAgent(draft)                   // Create o Update automático
publishAgent(agentId)
deleteAgent(agentId)
cloneAgent({ agentId, newName, newDescription })
previewAgent({ agentId, message, variables })

// Actions (sync)
setActiveTab(tabIndex)
updateField({ field, value })
addStep(step), removeStep(id), updateStep({ id, changes })
addTool(tool), removeTool(toolId)
updateGuardrails(partialConfig)
updateMemory(partialConfig)
updateModel(partialConfig)
loadDraft(draft), resetDraft()
```

---

## 🔄 Data Flow

### 1. Listar Agentes (AgentsPage)

```typescript
// pages/agents/AgentsPage.tsx
import { useAgents } from './Hooks/useAgents';

export default function AgentsPage() {
  const { agents, loading } = useAgents('tenant-1');

  return (
    <DataGrid
      rows={agents}
      columns={AGENT_COLUMNS}
      loading={loading}
      getRowId={(row) => row.id}
    />
  );
}
```

```typescript
// pages/agents/Hooks/useAgents.ts
export function useAgents(tenantId: string) {
  const dispatch = useDispatch();
  const { items, loading, error } = useSelector((state: any) => state.agents);

  useEffect(() => {
    if (tenantId) {
      dispatch(fetchAgents(tenantId) as any);
    }
  }, [dispatch, tenantId]);

  return { agents: items, loading, error };
}
```

**Backend Response** (`GET /api/v1/tenants/{tenantId}/agents`):
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "Customer Support Agent",
    "description": "Handles tier-1 inquiries",
    "status": "Published",
    "version": 3,
    "createdAt": "2026-02-15T10:30:00Z",
    "updatedAt": "2026-02-20T14:22:00Z",
    "tags": ["support", "production"]
  }
]
```

---

### 2. Crear/Editar Agente (Designer)

```typescript
// pages/agents/Designer/AgentDesignerPage.tsx
import { useDispatch, useSelector } from 'react-redux';
import { saveAgent, updateField } from './designerSlice';

export default function AgentDesignerPage() {
  const dispatch = useDispatch();
  const { draft, saving } = useSelector((state: RootState) => state.designer);

  const handleSave = async () => {
    await dispatch(saveAgent(draft));
  };

  return (
    <form onSubmit={handleSave}>
      <TextField
        value={draft.name}
        onChange={(e) => dispatch(updateField({ field: 'name', value: e.target.value }))}
      />
      <Button type="submit" disabled={saving}>
        {draft.id ? 'Update Agent' : 'Create Agent'}
      </Button>
    </form>
  );
}
```

**Frontend Payload** (`POST /api/v1/tenants/{tenantId}/agents`):
```typescript
{
  name: "Customer Support Agent",
  description: "Handles tier-1 inquiries",
  version: "1.0.0",
  status: "Draft",
  brain: {
    primaryModel: "gpt-4o",
    fallbackModel: "gpt-4o-mini",
    provider: "OpenAI",
    systemPrompt: "You are a customer support expert...",
    temperature: 0.7,
    maxResponseTokens: 4096
  },
  loop: {
    maxSteps: 25,
    timeoutPerStepMs: 30000,
    maxTokensPerExecution: 100000,
    maxRetries: 3,
    enablePromptInjectionGuard: true,
    enablePIIProtection: true,
    requireHumanApproval: false,
    humanApprovalThreshold: "high_risk"
  },
  memory: {
    workingMemory: true,
    longTermMemory: false,
    vectorMemory: false,
    auditMemory: true
  },
  steps: [
    { id: "step-1", type: "think", label: "Identify Intent", ... }
  ],
  tools: [
    { toolId: "zendesk", toolName: "Zendesk API", version: "1.0.0", permissions: ["tickets.read"] }
  ],
  tags: ["support", "production"]
}
```

**Backend Response** (`AgentDetailDto`):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "Customer Support Agent",
  "description": "Handles tier-1 inquiries",
  "status": "Draft",
  "version": 1,
  "createdAt": "2026-02-21T10:00:00Z",
  "updatedAt": "2026-02-21T10:00:00Z",
  "ownerUserId": "user-123",
  "tags": ["support", "production"],
  "brain": { ... },
  "loop": { ... },
  "memory": { ... },
  "steps": [ ... ],
  "tools": [ ... ]
}
```

---

### 3. Clonar Agente

```typescript
// pages/agents/AgentsList.tsx
import { cloneAgent } from '../Redux/Slice';

const handleClone = async (agentId: string) => {
  await dispatch(cloneAgent({
    tenantId: 'tenant-1',
    agentId,
    newName: 'Customer Support Agent (Copy)',
    newDescription: 'Cloned from original'
  }));
};
```

**Backend Request** (`POST /api/v1/tenants/{tenantId}/agents/{id}/clone`):
```json
{
  "newName": "Customer Support Agent (Copy)",
  "newDescription": "Cloned from original"
}
```

**Backend Response**:
```json
{
  "id": "660f9500-f3ac-52e5-b827-557766551111",   // Nuevo ID
  "name": "Customer Support Agent (Copy)",
  "status": "Draft",                              // Siempre Draft
  "version": 1,                                   // Reinicia a 1
  // ... resto de configuración copiada del original
}
```

---

### 4. Preview (Dry-Run Execution)

```typescript
// pages/agents/Designer/TestPanel.tsx
import { previewAgent } from './designerThunks';

const handlePreview = async () => {
  const result = await dispatch(previewAgent({
    agentId: draft.id!,
    message: "What is the status of ticket #12345?",
    variables: { ticketId: "12345" }
  }));

  if (result.payload.success) {
    console.log('Preview succeeded:', result.payload.finalResponse);
    console.log('Stats:', {
      steps: result.payload.totalSteps,
      tokens: result.payload.totalTokensUsed,
      duration: result.payload.durationMs
    });
  } else {
    console.error('Preview failed:', result.payload.errorMessage);
  }
};
```

**Backend Request** (`POST /api/v1/tenants/{tenantId}/agents/{id}/preview`):
```json
{
  "message": "What is the status of ticket #12345?",
  "variables": { "ticketId": "12345" }
}
```

**Backend Response** (`PreviewExecutionResponse`):
```json
{
  "success": true,
  "executionId": "exec-770g0600-g4bd-63f6-c938-668877662222",
  "status": "Completed",
  "finalResponse": "Ticket #12345 is currently in progress. ETA: 2 hours.",
  "totalSteps": 5,
  "totalTokensUsed": 1247,
  "durationMs": 3420,
  "runtimeSnapshot": {
    "agentVersion": "1",
    "modelId": "gpt-4o",
    "temperature": 0.7
  }
}
```

---

## 🛡️ Type Safety (TypeScript)

### Frontend Types

```typescript
// pages/agents/Designer/types.ts

export interface AgentStep {
  id: string;
  type: 'think' | 'plan' | 'act' | 'observe' | 'decide' | 'tool_call' | 'human_review';
  label: string;
  description: string;
  config: Record<string, unknown>;
  position: { x: number; y: number };
  connections: string[];
}

export interface AgentToolBinding {
  toolId: string;
  toolName: string;
  version: string;
  riskLevel: 'Low' | 'Medium' | 'High' | 'Critical';
  permissions: string[];
}

export interface AgentDefinitionDraft {
  id?: string;
  name: string;
  description: string;
  version: string;
  status: 'Draft' | 'Published' | 'Archived';
  steps: AgentStep[];
  tools: AgentToolBinding[];
  memory: AgentMemoryConfig;
  guardrails: AgentGuardrails;
  model: AgentModelConfig;
  systemPrompt: string;
  tags: string[];
}
```

### Backend DTOs (C#)

```csharp
// src/AgentFlow.Api/Controllers/DTOs/AgentDesignerDtos.cs

public sealed record AgentDesignerDto
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = "Draft";
    public string Version { get; init; } = "1.0.0";
    public BrainConfigDto Brain { get; init; } = new();
    public LoopConfigDto Loop { get; init; } = new();
    public MemoryConfigDto Memory { get; init; } = new();
    public IReadOnlyList<DesignerStepDto> Steps { get; init; } = [];
    public IReadOnlyList<ToolBindingDto> Tools { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record AgentDetailDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string Status { get; init; }
    public required long Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string OwnerUserId { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public BrainConfigDto Brain { get; init; } = new();
    public LoopConfigDto Loop { get; init; } = new();
    public MemoryConfigDto Memory { get; init; } = new();
    public IReadOnlyList<DesignerStepDto> Steps { get; init; } = [];
    public IReadOnlyList<ToolBindingDto> Tools { get; init; } = [];
}
```

---

## 🔧 Mapeo Frontend ↔ Backend

### Nombre de Propiedades

| Concepto | Frontend (TypeScript) | Backend (C#) |
|---|---|---|
| Config del cerebro | `draft.model` | `Brain` |
| Guardrails del loop | `draft.guardrails` | `Loop` |
| Configuración memoria | `draft.memory` | `Memory` |
| Human-in-the-Loop | `draft.guardrails.hitl` | `Loop.RequireHumanApproval` |

### HITL Mapping (Crítico)

**Frontend → Backend**:
```typescript
// designerThunks.ts
requireHumanApproval: draft.guardrails.hitl.enabled,
humanApprovalThreshold: draft.guardrails.hitl.requireReviewOnAllToolCalls ? 'always' : 'high_risk',
```

**Backend → Frontend**:
```typescript
// designerThunks.ts
hitl: {
  enabled: (loop.requireHumanApproval as boolean) ?? false,
  requireReviewOnAllToolCalls: (loop.humanApprovalThreshold as string) === 'always',
  requireReviewOnPolicyEscalation: true,
  confidenceThreshold: 0.7,
},
```

**Backend Domain** (C#):
```csharp
// AgentsController.cs (mapeo de DTO a Domain)
var loop = new AgentLoopConfig
{
    MaxIterations = request.Loop.MaxSteps,
    ToolCallTimeout = TimeSpan.FromMilliseconds(request.Loop.TimeoutPerStepMs),
    MaxRetries = request.Loop.MaxRetries,
    HitlConfig = new HumanInTheLoopConfig { Enabled = request.Loop.RequireHumanApproval }
};

// AgentsController.cs (mapeo de Domain a DTO)
Loop = new LoopConfigDto
{
    MaxSteps = agent.LoopConfig.MaxIterations,
    TimeoutPerStepMs = (int)agent.LoopConfig.ToolCallTimeout.TotalMilliseconds,
    MaxRetries = agent.LoopConfig.MaxRetries,
    RequireHumanApproval = agent.LoopConfig.HitlConfig.Enabled,
}
```

---

## 🧪 Testing Integration

### Ejemplo: Unit Test para Redux Slice

```typescript
// pages/agents/Redux/__tests__/Slice.test.ts
import { configureStore } from '@reduxjs/toolkit';
import agentsReducer, { fetchAgents, createAgent } from '../Slice';

describe('Agents Slice', () => {
  it('should handle fetchAgents.fulfilled', () => {
    const store = configureStore({ reducer: { agents: agentsReducer } });
    
    const mockAgents = [
      { id: '1', name: 'Agent 1', status: 'Published', version: 1 }
    ];

    store.dispatch(fetchAgents.fulfilled(mockAgents, '', 'tenant-1'));

    expect(store.getState().agents.items).toEqual(mockAgents);
    expect(store.getState().agents.loading).toBe(false);
  });

  it('should handle createAgent.fulfilled', () => {
    const store = configureStore({ reducer: { agents: agentsReducer } });
    
    const newAgent = { id: '2', name: 'New Agent', status: 'Draft', version: 1 };

    store.dispatch(createAgent.fulfilled(newAgent, '', { tenantId: 'tenant-1', payload: {} }));

    expect(store.getState().agents.items).toContainEqual(newAgent);
  });
});
```

---

## 🚨 Error Handling

### Frontend (Axios Interceptor)

```typescript
// src/lib/axios.ts
axiosInstance.interceptors.response.use(
  (response) => response,
  (error) => {
    // El error del backend llega como error.response.data
    const errorMessage = error.response?.data?.error || 
                        error.response?.data?.message || 
                        'Something went wrong!';
    
    return Promise.reject(errorMessage);
  }
);
```

### Redux Slice Error State

```typescript
// Slice manejando errores
.addCase(createAgent.rejected, (state, action) => {
  state.loading = false;
  state.error = action.error.message; // Mensaje del backend
});
```

### Backend Error Response

```csharp
// AgentsController.cs
if (!agentResult.IsSuccess)
    return BadRequest(agentResult.Error);

// Retorna:
// {
//   "code": "VALIDATION_ERROR",
//   "message": "Agent name cannot be empty",
//   "category": "Validation"
// }
```

---

## 🔐 Autenticación (Pendiente)

**Estado Actual**: Usando `TENANT_ID = 'tenant-1'` hardcodeado.

**TODO**:
```typescript
// Obtener de Auth Context
import { useTenantContext } from 'src/auth/hooks';

export function useAgents() {
  const { tenantId } = useTenantContext(); // En lugar de 'tenant-1'
  // ...
}
```

**Backend**: Ya está preparado con `ITenantContextAccessor`.

---

## 📊 Estado de Implementación

| Feature | Status | Archivos Modificados |
|---|---|---|
| ✅ Listar Agentes | Completado | `AgentsPage.tsx`, `Redux/Slice.ts` |
| ✅ Ver Detalle | Completado | `designerThunks.ts` (`fetchAgentDetail`) |
| ✅ Crear Agente | Completado | `Redux/Slice.ts` (`createAgent`), `designerThunks.ts` (`saveAgent`) |
| ✅ Actualizar Agente | Completado | `Redux/Slice.ts` (`updateAgent`), `designerThunks.ts` (`saveAgent`) |
| ✅ Eliminar Agente | Completado | `Redux/Slice.ts` (`deleteAgent`) |
| ✅ Clonar Agente | Completado | `Redux/Slice.ts` (`cloneAgent`), `designerThunks.ts` |
| ✅ Publicar Agente | Completado | `Redux/Slice.ts` (`publishAgent`), `designerThunks.ts` |
| ✅ Preview Agente | Completado | `designerThunks.ts` (`previewAgent`) |
| ✅ HITL Mapping Correcto | Completado | `designerThunks.ts` (mapeo bidireccional corregido) |

---

## 🎯 Próximos Pasos

### Fase 1: Auth Context (Semana 1)
- [ ] Implementar `useTenantContext()` hook
- [ ] Reemplazar `TENANT_ID` hardcodeado
- [ ] Agregar JWT Bearer token a Axios headers

### Fase 2: UI Components (Semana 2)
- [ ] Crear formulario de creación (modal o página)
- [ ] Agregar botones Clone/Delete en DataGrid
- [ ] Panel de Preview/Testing en Designer
- [ ] Toast notifications para operaciones CRUD

### Fase 3: Optimización (Semana 3)
- [ ] Implementar cache con RTK Query (migrar de createAsyncThunk)
- [ ] Optimistic updates (actualizar UI antes de respuesta backend)
- [ ] Paginación en DataGrid (backend ya soporta `skip`/`limit`)

### Fase 4: Testing (Semana 4)
- [ ] Unit tests para Redux slices
- [ ] Integration tests con Mock Service Worker (MSW)
- [ ] E2E tests con Playwright

---

## 📚 Referencias

### Backend
- [AgentsController.cs](../src/AgentFlow.Api/Controllers/AgentsController.cs) - Endpoints REST
- [AgentDesignerDtos.cs](../src/AgentFlow.Api/Controllers/DTOs/AgentDesignerDtos.cs) - DTOs
- [AgentExecutionsController.cs](../src/AgentFlow.Api/Controllers/AgentExecutionsController.cs) - Preview endpoint

### Frontend
- [Redux/Slice.ts](../frontend/aiagent_flow/src/aiagentflow/pages/agents/Redux/Slice.ts) - CRUD operations
- [designerThunks.ts](../frontend/aiagent_flow/src/aiagentflow/pages/agents/Designer/designerThunks.ts) - Designer API calls
- [designerSlice.ts](../frontend/aiagent_flow/src/aiagentflow/pages/agents/Designer/designerSlice.ts) - Designer state
- [types.ts](../frontend/aiagent_flow/src/aiagentflow/pages/agents/Designer/types.ts) - TypeScript types

---

**✅ Frontend-Backend Integration COMPLETADO**

Todos los endpoints están conectados y funcionando. El frontend puede crear, listar, editar, eliminar, clonar, publicar y hacer preview de agentes usando el backend .NET.

---

*AgentFlow Master Architect*  
*Febrero 21, 2026*
