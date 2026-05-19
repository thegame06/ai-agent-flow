# 🎯 Consolidación del Sistema de Intent Routing

**Fecha**: 18 de mayo de 2026  
**Versión**: 1.0  
**Estado**: ✅ Completado

---

## 📋 Resumen Ejecutivo

Se consolidó la funcionalidad duplicada entre dos páginas del frontend en un **único módulo unificado** de Intent Routing con capacidades completas de orquestación.

---

## 🔄 Cambios Realizados

### **ANTES: Sistema Duplicado**

#### Página 1: "Motivos del cliente" (ANTIGUA)
- **Ruta**: `/dashboard/orchestration`
- **Componente**: `ManagerOrchestrationPage.tsx`
- **Funcionalidad**:
  - ✅ Definir motivos/intenciones
  - ✅ Conectar con workflows
  - ✅ Asignar agentes (source/target)
  - ✅ Probar/simular mensajes
  - ⚠️ UI desactualizada
  - ⚠️ Textos mezclados inglés/español

#### Página 2: "Reglas de intención" (NUEVA)
- **Ruta**: `/dashboard/intents`
- **Componente**: `IntentsPage.tsx`
- **Funcionalidad**:
  - ✅ Definir intenciones
  - ✅ Clasificación semántica avanzada
  - ✅ Playground para pruebas
  - ❌ Sin conexión clara a workflows
  - ❌ Sin selector de agentes
  - ✅ UI moderna (Material UI v6)
  - ✅ Español latinoamericano completo

**Problema**: Ambas páginas usaban el mismo backend pero con interfaces diferentes, causando confusión.

---

### **DESPUÉS: Sistema Consolidado** ⭐

#### Página Única: "Reglas de intención"
- **Ruta**: `/dashboard/intents`
- **Redirección**: `/dashboard/orchestration` → `/dashboard/intents`
- **Funcionalidad Completa**:
  - ✅ Definir intenciones con clasificación semántica
  - ✅ Conectar con workflows (selector visual)
  - ✅ Asignar agentes destino (selector visual)
  - ✅ Playground para pruebas en tiempo real
  - ✅ Ejemplos y sinónimos
  - ✅ Prioridad y umbral de confianza
  - ✅ UI moderna y consistente
  - ✅ 100% español latinoamericano

---

## 🎨 Cambios en la UI

### 1. **Tabla de Intenciones** (IntentsList.tsx)

**Columnas agregadas**:
```typescript
- Workflow → Muestra nombre y ID del workflow asignado
- Agente destino → Muestra el agente que resolverá la intención
```

**Antes**:
```
| Intención | Categoría | Ejemplos | Prioridad | Confianza | Activo | Acciones |
```

**Ahora**:
```
| Intención | Categoría | Workflow | Agente destino | Ejemplos | Prioridad | Confianza | Activo | Acciones |
```

---

### 2. **Dialog de Creación/Edición** (CreateIntentDialog.tsx)

**Campos agregados**:
```typescript
- Workflow (opcional) → Selector con lista de workflows disponibles
- Agente destino (opcional) → Selector con lista de agentes disponibles
```

**Campos eliminados**:
```typescript
- suggested_workflow (obsoleto) ❌
```

**UI Completa**:
```
┌─────────────────────────────────────────┐
│ Nueva regla de intención                │
├─────────────────────────────────────────┤
│ • Clave única                            │
│ • Nombre                                 │
│ • Descripción                            │
│ • Categoría                              │
│ • Ejemplos (chips editables)            │
│ • Sinónimos (opcional)                   │
│ • Workflow (selector)          ← NUEVO   │
│ • Agente destino (selector)    ← NUEVO   │
│ • Prioridad (1-10)                       │
│ • Umbral de confianza (50-100%)          │
│ • Activado (switch)                      │
└─────────────────────────────────────────┘
```

---

## 🔧 Cambios Técnicos

### 1. **Types (types.ts)**

```typescript
// ANTES
export interface Intent {
  suggested_workflow?: string;  // ❌ Obsoleto
}

// AHORA
export interface Intent {
  workflow_id?: string;         // ✅ ID del workflow
  workflow_name?: string;       // ✅ Nombre del workflow
  target_agent_id?: string;     // ✅ Agente destino
}

// NUEVOS TIPOS
export interface Workflow {
  id: string;
  name: string;
  description?: string;
  status?: string;
}

export interface Agent {
  id: string;
  name: string;
  status?: string;
}
```

---

### 2. **IntentsPage.tsx**

```typescript
// Carga workflows y agentes al iniciar
const loadIntents = useCallback(async () => {
  const [intentsRes, workflowsRes, agentsRes] = await Promise.all([
    axios.get(endpoints.agentflow.intentRouting.rules(tenantId)),
    axios.get(`/api/v1/tenants/${tenantId}/workflows`),
    axios.get(endpoints.agentflow.agents.list(tenantId)),
  ]);
  
  setIntents(intentsRes.data || []);
  setWorkflows(workflowsRes.data || []);  // ← NUEVO
  setAgents(agentsRes.data || []);        // ← NUEVO
}, [tenantId]);

// Pasa workflows y agentes al dialog
<CreateIntentDialog
  workflows={workflows}  // ← NUEVO
  agents={agents}        // ← NUEVO
  ...
/>
```

---

### 3. **Navegación (nav-config-dashboard.tsx)**

```typescript
// ANTES
{
  title: 'Reglas de intención',
  path: paths.dashboard.intents,
},
{
  title: 'Orquestación de managers',  // ← DUPLICADO
  path: paths.dashboard.intentMap,
},

// AHORA
{
  title: 'Reglas de intención',
  path: paths.dashboard.intents,
},
// "Orquestación de managers" eliminado ✅
```

---

### 4. **Rutas (dashboard.tsx)**

```typescript
// Redirección automática de la ruta antigua
{ 
  path: 'orchestration', 
  element: <Navigate to="/dashboard/intents" replace /> 
},
```

---

## 🔗 Conexión con Backend

El módulo consolidado sigue usando el mismo backend de Intent Routing:

```typescript
// Endpoints usados
endpoints.agentflow.intentRouting.rules(tenantId)      // GET/POST reglas
endpoints.agentflow.intentRouting.classify(tenantId)   // POST clasificar
endpoints.agentflow.agents.list(tenantId)              // GET agentes
/api/v1/tenants/${tenantId}/workflows                  // GET workflows
```

**Backend valida**:
```csharp
// RoutingOrchestrator.cs línea 113
if (string.IsNullOrEmpty(bestMatch.Rule.WorkflowDefinitionId) &&
    string.IsNullOrEmpty(bestMatch.Rule.TargetAgentId))
{
    // No tiene workflow ni agente → Queue
}

// AgentExecutionEngine.cs línea 192
workflowResult = await _workflowEngine.TriggerAsync(
    routingDecision.WorkflowDefinitionId,  // ← Dispara el workflow
    workflowContext
);
```

---

## 🚀 Cómo Usar el Sistema Consolidado

### 1. **Crear una Nueva Regla de Intención**

1. Ir a **Construcción → Reglas de intención**
2. Clic en **"Nueva regla"**
3. Llenar:
   - **Clave**: `solicitar_prestamo`
   - **Nombre**: Solicitud de préstamo
   - **Descripción**: Cliente quiere solicitar un crédito
   - **Categoría**: Ventas
   - **Ejemplos**: 
     - "Quiero un préstamo"
     - "Necesito un crédito"
   - **Workflow**: Seleccionar workflow existente
   - **Agente destino**: Seleccionar agente que atenderá
4. Clic en **"Crear regla"**

---

### 2. **Probar la Clasificación**

1. Ir a **Playground** (botón en la página)
2. Escribir mensaje de prueba: "Necesito un préstamo"
3. Clic en **"Clasificar mensaje"**
4. Ver resultado:
   - Mejor coincidencia
   - Puntuación de confianza
   - Workflow que se dispararía
   - Explicación de la decisión

---

### 3. **Revisar Casos sin Clasificar**

1. Ir a **Operación → Casos sin clasificar**
2. Ver conversaciones que el sistema no pudo clasificar automáticamente
3. Revisar y reclasificar manualmente si es necesario

---

## ✅ Beneficios de la Consolidación

| Antes | Ahora |
|-------|-------|
| 2 páginas duplicadas | 1 página unificada |
| Funcionalidad fragmentada | Funcionalidad completa |
| UI inconsistente | UI moderna y uniforme |
| Textos mezclados | 100% español latino |
| Confusión de usuarios | Experiencia clara |
| Difícil mantenimiento | Código consolidado |

---

## 📦 Archivos Modificados

### Frontend
```
✅ src/aiagentflow/pages/intents/types.ts
✅ src/aiagentflow/pages/intents/IntentsPage.tsx
✅ src/aiagentflow/pages/intents/IntentsList.tsx
✅ src/aiagentflow/pages/intents/CreateIntentDialog.tsx
✅ src/layouts/nav-config-dashboard.tsx
✅ src/routes/sections/dashboard.tsx
```

### Archivos Deprecados
```
⚠️ src/aiagentflow/pages/orchestration/ManagerOrchestrationPage.tsx
   (Se mantiene para referencia, pero ya no se usa)
```

---

## 🎯 Próximos Pasos

1. ✅ **COMPLETADO**: Consolidar funcionalidad
2. ✅ **COMPLETADO**: Actualizar navegación
3. ✅ **COMPLETADO**: Crear redirección
4. 📝 **PENDIENTE**: Eliminar ManagerOrchestrationPage.tsx (opcional)
5. 📝 **PENDIENTE**: Actualizar tests E2E
6. 📝 **PENDIENTE**: Documentar API endpoints

---

## 📚 Documentación Relacionada

- [INTENT-ROUTING-ARCHITECTURE.md](./INTENT-ROUTING-ARCHITECTURE.md) - Arquitectura completa
- [INTENT-ROUTING-QUICKSTART.md](./INTENT-ROUTING-QUICKSTART.md) - Guía rápida
- [INFRASTRUCTURE-SETUP.md](./INFRASTRUCTURE-SETUP.md) - Setup de infraestructura

---

## 🆘 Soporte

Si encuentras problemas:

1. Verificar que el backend esté corriendo en `http://localhost:5183`
2. Verificar que existan workflows y agentes configurados
3. Revisar logs del navegador (F12 → Console)
4. Revisar logs del backend

---

**¡Sistema consolidado y listo para producción!** 🚀✨
