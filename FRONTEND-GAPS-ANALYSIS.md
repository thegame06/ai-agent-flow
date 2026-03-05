# Frontend Gaps Analysis - AgentFlow

**Fecha:** 2026-03-02  
**Objetivo:** Identificar y priorizar funcionalidades del backend sin UI en el frontend

---

## Matriz: Backend vs Frontend

| Dominio | Endpoints Backend | Página Frontend | Estado | Prioridad |
|---------|-------------------|-----------------|--------|-----------|
| **Agents** | CRUD, publish, clone, handoff | ✅ AgentsPage, AgentDetailPage, AgentDesignerPage | Completo | - |
| **Executions** | List, detail, trigger, handoff | ✅ ExecutionsPage, ExecutionDetailPage | Completo | - |
| **Channels** | CRUD, activate, deactivate, QR, status, assign-agent | ✅ ChannelsPage | Completo | - |
| **Channel Sessions** | List, detail, close, messages | ✅ (en ChannelsPage) | Completo | - |
| **Checkpoints (HITL)** | List, detail, decide | ✅ CheckpointsPage | Completo | - |
| **Conversation Threads** | CRUD, messages, history, archive | ❌ **FALTA** | 🔴 Pendiente | **P0** |
| **Policies** | CRUD, publish, clone-version | ✅ PoliciesPage | Completo | - |
| **Audit** | List, filters | ✅ AuditPage | Completo | - |
| **Tools/Extensions** | List, invoke, catalog | ✅ ToolsPage | Completo | - |
| **Model Routing** | CRUD models, providers, test, bind | ✅ ModelsPage | Completo | - |
| **Auth Profiles** | CRUD, test | ✅ AuthProfilesPage | Completo | - |
| **MCP Servers** | List servers, tools, invoke | ✅ McpPage | Completo | - |
| **Tenant Settings** | GET, PUT | ✅ SettingsPage | Completo | - |
| **Feature Flags** | List, check, enable, update | ❌ **FALTA** | 🟡 Pendiente | P2 |
| **Evaluations** | List, summary, pending-review | ❌ **FALTA** | 🔴 Pendiente | **P0** |
| **Segment Routing** | Preview, get, update, disable | ❌ **FALTA** | 🟡 Pendiente | P1 |
| **DSL Validator** | Validate, parse, compare, lifecycle | ❌ **FALTA** | 🟢 Opcional | P3 |
| **Test Runner** | Suite, case | ❌ **FALTA** | 🟢 Opcional | P3 |
| **Auth (JWT)** | Sign-in, sign-up, me | ✅ auth/jwt/* | Completo | - |

---

## Gaps Críticos (P0)

### 1. Conversation Threads
**Endpoints disponibles:**
- `POST /api/v1/tenants/{tenantId}/threads` - Crear thread
- `GET /api/v1/tenants/{tenantId}/threads` - Listar threads
- `GET /api/v1/tenants/{tenantId}/threads/{threadId}` - Detalle
- `GET /api/v1/tenants/{tenantId}/threads/{threadId}/history` - Historial de mensajes
- `POST /api/v1/tenants/{tenantId}/threads/{threadId}/messages` - Enviar mensaje
- `POST /api/v1/tenants/{tenantId}/threads/{threadId}/archive` - Archivar
- `DELETE /api/v1/tenants/{tenantId}/threads/{threadId}` - Eliminar

**Por qué es crítico:**
- Los usuarios necesitan ver historiales de conversación
- Es esencial para debugging y auditoría
- ChatPage existe pero no hay lista de threads previos

**Implementación requerida:**
- `ThreadsPage.tsx` - Lista de threads con filtros (por agente, estado, fecha)
- Integración con ChatPage existente
- Vista de historial con mensajes

---

### 2. Evaluations Dashboard
**Endpoints disponibles:**
- `GET /api/v1/tenants/{tenantId}/evaluations/executions/{executionId}` - Evaluación por ejecución
- `GET /api/v1/tenants/{tenantId}/evaluations/agents/{agentKey}` - Evaluaciones por agente
- `GET /api/v1/tenants/{tenantId}/evaluations/agents/{agentKey}/summary` - Resumen comparativo
- `GET /api/v1/tenants/{tenantId}/evaluations/pending-review` - Pendientes de revisión

**Por qué es crítico:**
- Es parte central de la propuesta de valor empresarial
- Permite comparación champion/challenger
- Sin esto, no se puede medir calidad de agentes

**Implementación requerida:**
- `EvaluationsPage.tsx` - Dashboard con métricas de calidad
- Vistas comparativas champion vs challenger
- Gráficos de tendencias de calidad

---

## Gaps Importantes (P1)

### 3. Segment Routing
**Endpoints disponibles:**
- `GET /api/v1/tenants/{tenantId}/segment-routing/agents/{agentId}` - Configuración actual
- `PUT /api/v1/tenants/{tenantId}/segment-routing/agents/{agentId}` - Actualizar segmentación
- `POST /api/v1/tenants/{tenantId}/segment-routing/agents/{agentId}/preview` - Preview de routing
- `POST /api/v1/tenants/{tenantId}/segment-routing/agents/{agentId}/disable` - Desactivar routing

**Implementación requerida:**
- `SegmentRoutingPage.tsx` - UI para configurar segmentación por agente
- Preview de decisiones de routing

---

## Gaps Secundarios (P2)

### 4. Feature Flags
**Endpoints disponibles:**
- `GET /api/v1/tenants/{tenantId}/feature-flags` - Listar flags
- `POST /api/v1/tenants/{tenantId}/feature-flags/{flagKey}/check` - Verificar estado
- `POST /api/v1/tenants/{tenantId}/feature-flags/enabled` - Listar enabled
- `PUT /api/v1/tenants/{tenantId}/feature-flags/{flagKey}` - Actualizar flag

**Implementación requerida:**
- `FeatureFlagsPage.tsx` - Gestión de feature flags por tenant

---

## Gaps Opcionales (P3)

### 5. DSL Validator
- Herramienta para validar/parsear DSL de agentes
- Útil para desarrolladores pero no crítico para operación

### 6. Test Runner
- Ejecución de tests desde UI
- Complementario, no esencial

---

## Plan de Implementación

### Sprint 1 (Esta semana)
- [ ] **ThreadsPage** - Lista de conversations + historial
- [ ] **EvaluationsPage** - Dashboard de calidad champion/challenger

### Sprint 2
- [ ] **SegmentRoutingPage** - Configuración de segmentación
- [ ] **FeatureFlagsPage** - Gestión de flags

### Sprint 3
- [ ] **DSL Validator Tool** (opcional)
- [ ] **Test Runner UI** (opcional)

---

## Estructura de Archivos a Crear

```
frontend/aiagent_flow/src/aiagentflow/pages/
├── threads/
│   ├── ThreadsPage.tsx          # Lista de threads
│   ├── Config/
│   │   └── Columns.tsx          # Columnas para DataGrid
│   ├── Hooks/
│   │   └── useThreads.ts        # Hook para fetch de threads
│   └── Redux/
│       └── Slice.ts             # Estado Redux
├── evaluations/
│   ├── EvaluationsPage.tsx      # Dashboard de evaluaciones
│   ├── Config/
│   │   └── Columns.tsx
│   ├── Hooks/
│   │   └── useEvaluations.ts
│   └── Redux/
│       └── Slice.ts
├── segment-routing/
│   ├── SegmentRoutingPage.tsx
│   └── ...
└── feature-flags/
    ├── FeatureFlagsPage.tsx
    └── ...
```

---

## Notas de Implementación

1. **Seguir patrones existentes:** Reutilizar estructura de AgentsPage/ExecutionsPage
2. **Usar useTenantId:** Todas las páginas deben ser multi-tenant
3. **DataGrid de MUI:** Usar para listas tabulares
4. **Redux Toolkit:** Para estado compartido cuando sea necesario
5. **Axios endpoints:** Agregar rutas en `src/lib/axios.ts`

---

## Criterios de Aceptación

- [ ] Build frontend sin errores
- [ ] Lint sin errores bloqueantes
- [ ] Tests smoke pasando
- [ ] Navegación desde menú dashboard
- [ ] CRUD completo (cuando aplique)
- [ ] Manejo de errores apropiado
