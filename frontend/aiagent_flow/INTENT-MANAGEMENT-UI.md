# Intent Management UI - Frontend MVP

## 📋 Resumen de Implementación

Se han implementado exitosamente **3 pantallas principales** para el Intent Routing Frontend MVP:

### ✅ Páginas Implementadas

1. **Intent Management Page** (`/dashboard/intents`)
   - Lista completa de intenciones configuradas
   - Filtros por categoría y estado (enabled/disabled)
   - Búsqueda por nombre, key o descripción
   - Tabla con información detallada de cada intent
   - Acciones: Editar, Habilitar/Deshabilitar, Eliminar
   - Dialog para Crear/Editar intenciones

2. **Intent Playground** (`/dashboard/intents/playground`)
   - Input para mensajes de prueba
   - Clasificación en tiempo real
   - Visualización del mejor match con confianza
   - Lista de todos los candidatos evaluados
   - Explicación detallada de la decisión
   - Mensajes de ejemplo precargados

3. **Conversation Inbox** (`/dashboard/inbox`)
   - Dashboard con métricas de conversaciones
   - Tabla de conversaciones activas
   - Filtros por estado y nivel de confianza
   - Acciones: Ver, Reasignar, Resolver
   - Indicadores visuales para conversaciones que requieren revisión humana

---

## 📁 Estructura de Archivos Creados

```
frontend/aiagent_flow/src/aiagentflow/pages/intents/
├── types.ts                        # TypeScript types & interfaces
├── IntentsPage.tsx                 # Página principal de gestión
├── IntentsList.tsx                 # Tabla de intenciones
├── IntentFilters.tsx               # Filtros (category, enabled)
├── IntentSearchBar.tsx             # Barra de búsqueda
├── CreateIntentDialog.tsx          # Dialog crear/editar
├── PlaygroundPage.tsx              # Página de testing
├── BestMatchCard.tsx               # Card de mejor match
├── CandidatesListCard.tsx          # Lista de candidatos
├── ExplanationCard.tsx             # Explicación de decisión
├── InboxPage.tsx                   # Página de conversaciones
├── InboxTable.tsx                  # Tabla de conversaciones
├── InboxFilters.tsx                # Filtros de inbox
└── InboxStatsCards.tsx             # Cards con métricas
```

---

## 🔗 Rutas Configuradas

### Paths actualizados en `src/routes/paths.ts`:

```typescript
dashboard: {
  // ... existing paths
  intents: '/dashboard/intents',
  intentsPlayground: '/dashboard/intents/playground',
  inbox: '/dashboard/inbox',
}
```

### Rutas en `src/routes/sections/dashboard.tsx`:

```typescript
{ path: 'intents', element: <IntentsPage /> }
{ path: 'intents/playground', element: <PlaygroundPage /> }
{ path: 'inbox', element: <InboxPage /> }
```

### Navegación en Sidebar:

- **Construcción** > "Intenciones del cliente" → `/dashboard/intents`
- **Operación** > "Conversaciones" → `/dashboard/inbox`

---

## 🔌 API Endpoints Agregados

En `src/lib/axios.ts`:

```typescript
intentRouting: {
  rules: (tenantId: string) => `/api/v1/tenants/${tenantId}/intent-routing/rules`,
  ruleEnable: (tenantId: string, ruleId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/rules/${ruleId}/enable`,
  ruleById: (tenantId: string, ruleId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/rules/${ruleId}`,
  classify: (tenantId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/classify`,
  simulate: (tenantId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/simulate`,
  conversations: (tenantId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/conversations`,
  conversationReassign: (tenantId: string, conversationId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/conversations/${conversationId}/reassign`,
  conversationResolve: (tenantId: string, conversationId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/conversations/${conversationId}/resolve`,
  stats: (tenantId: string) => 
    `/api/v1/tenants/${tenantId}/intent-routing/stats`,
}
```

---

## 🎨 Componentes y Features

### Intent Management Page

**Componentes:**
- `IntentsList`: Tabla con todas las intenciones
- `IntentFilters`: Filtros por categoría y estado
- `IntentSearchBar`: Búsqueda en tiempo real
- `CreateIntentDialog`: Dialog completo para crear/editar intenciones

**Features:**
- ✅ Vista de lista con información detallada
- ✅ Filtrado por categoría (Customer Service, Sales, Support, etc.)
- ✅ Filtrado por estado (Enabled/Disabled)
- ✅ Búsqueda por nombre, key o descripción
- ✅ Toggle rápido para habilitar/deshabilitar
- ✅ Dialog modal para crear/editar con validación
- ✅ Campos: Key, Name, Description, Category, Examples, Synonyms
- ✅ Sliders para Priority y Confidence Threshold
- ✅ Chips para gestionar ejemplos y sinónimos
- ✅ Badge visual para "BASE" intents
- ✅ Mock data para desarrollo (funciona sin backend)

### Intent Playground

**Componentes:**
- `BestMatchCard`: Muestra el mejor match con confianza
- `CandidatesListCard`: Todos los candidatos evaluados
- `ExplanationCard`: Factores que contribuyeron a la decisión

**Features:**
- ✅ Input multilinea para mensajes de prueba
- ✅ Botones de ejemplo precargados
- ✅ Clasificación en tiempo real
- ✅ Visualización del mejor match con:
  - Nombre e intent_key
  - Score de confianza (%)
  - Nivel de confianza (High/Medium/Low)
  - Progress bar visual
- ✅ Lista de todos los candidatos con scores
- ✅ Explicación detallada:
  - Decisión textual
  - Factores contribuyentes (con %)
  - Número de alternativas consideradas
- ✅ Tiempo de procesamiento
- ✅ Mock data para desarrollo

### Conversation Inbox

**Componentes:**
- `InboxStatsCards`: Grid de 4 cards con métricas
- `InboxFilters`: Filtros por estado y confianza
- `InboxTable`: Tabla de conversaciones

**Features:**
- ✅ Dashboard con 4 métricas clave:
  - Total Conversations
  - Awaiting Classification
  - Requires Review
  - Resolved Today
- ✅ Tabla con información completa:
  - User identifier & Channel
  - Last message (truncado)
  - Estado (AwaitingClassification, Classified, InProgress, Resolved, Abandoned)
  - Intent detectado
  - Nivel de confianza
  - Timestamp de creación
- ✅ Filtros por estado y confianza
- ✅ Indicador visual (fondo amarillo) para conversaciones que requieren revisión
- ✅ Acciones: Ver, Reasignar, Resolver
- ✅ Botón de refresh
- ✅ Mock data para desarrollo

---

## 🎯 TypeScript Types

### Core Types:

```typescript
interface Intent {
  id: string;
  key: string;
  name: string;
  description: string;
  category: string;
  examples: string[];
  synonyms: string[];
  confidence_threshold: number;
  priority: number;
  suggested_workflow?: string;
  enabled: boolean;
  is_base_intent: boolean;
  created_at: string;
  updated_at: string;
}

interface ClassificationResult {
  best_match: {
    intent_key: string;
    intent_name: string;
    description: string;
  };
  best_score: number;
  confidence: 'High' | 'Medium' | 'Low';
  all_candidates: IntentCandidate[];
  explanation_json: string;
  processing_time_ms: number;
}

interface InboxConversation {
  id: string;
  tenant_id: string;
  channel: string;
  user_identifier: string;
  last_message: string;
  state: ConversationState;
  confidence: ConfidenceLevel;
  detected_intent_key?: string;
  created_at: string;
  updated_at: string;
  requires_human_review: boolean;
}
```

---

## 🚀 Cómo Usar

### 1. Navegar a Intent Management

```
http://localhost:5173/dashboard/intents
```

- Ver todas las intenciones configuradas
- Filtrar por categoría o estado
- Buscar por texto
- Crear nueva intención (botón "Create Intent")
- Editar intención existente (icono lápiz)
- Habilitar/Deshabilitar con toggle

### 2. Probar en el Playground

```
http://localhost:5173/dashboard/intents/playground
```

- Escribir un mensaje de prueba
- O seleccionar un ejemplo predefinido
- Click "Classify Intent"
- Ver resultados:
  - Mejor match con confianza
  - Todos los candidatos evaluados
  - Explicación detallada

### 3. Gestionar Conversaciones

```
http://localhost:5173/dashboard/inbox
```

- Ver métricas del día
- Filtrar por estado o confianza
- Ver conversaciones pendientes
- Reasignar intenciones incorrectas
- Marcar como resueltas

---

## 🔧 Estado Actual

### ✅ Completado

- [x] 3 páginas principales implementadas
- [x] Componentes reutilizables creados
- [x] TypeScript types definidos
- [x] Routing configurado
- [x] Navegación en sidebar actualizada
- [x] API endpoints definidos
- [x] Mock data para desarrollo sin backend
- [x] Loading states
- [x] Empty states
- [x] Diseño responsive con MUI v6
- [x] Validación de formularios
- [x] Iconografía con Iconify

### ⚠️ Pendiente (Requiere Backend)

- [ ] Conectar con APIs reales del backend
- [ ] Implementar error handling robusto
- [ ] Agregar toast notifications
- [ ] Implementar paginación en tablas
- [ ] Agregar skeleton loaders
- [ ] Implementar real-time updates (WebSocket)
- [ ] Agregar confirmación dialogs
- [ ] Mejorar accesibilidad (ARIA labels)

---

## 🎨 Diseño y UX

### Material UI v6

Todos los componentes utilizan MUI v6:
- `Card`, `CardContent`, `CardHeader`
- `Table`, `TableContainer`, `TableRow`, etc.
- `Button`, `IconButton`
- `TextField`, `Select`, `MenuItem`
- `Chip`, `Switch`, `Slider`
- `Stack`, `Grid`, `Box`
- `Typography`
- `Dialog`, `DialogTitle`, `DialogContent`, `DialogActions`
- `LinearProgress`, `CircularProgress`

### Color System

- **Success** (Verde): High confidence, Enabled, Resolved
- **Warning** (Amarillo): Medium confidence, Draft, Awaiting
- **Error** (Rojo): Low confidence, Disabled, Abandoned
- **Primary** (Azul): In Progress, Base Intent
- **Info** (Celeste): Classified

### Iconos

Todos los iconos provienen de **Iconify** con prefijo `eva:`:
- `eva:plus-fill` - Crear nuevo
- `eva:edit-outline` - Editar
- `eva:trash-2-outline` - Eliminar
- `eva:checkmark-circle-2-fill` - Success
- `eva:alert-triangle-fill` - Warning
- `eva:inbox-outline` - Empty state
- `eva:flash-fill` - Classify
- `eva:refresh-outline` - Refresh
- etc.

---

## 📝 Notas de Desarrollo

### Mock Data

Todas las páginas incluyen mock data que se activa cuando las llamadas al backend fallan. Esto permite:
- Desarrollo sin dependencia del backend
- Testing de UI sin API real
- Demostración de funcionalidad

Para desactivar mock data, implementar las APIs en el backend según los endpoints definidos.

### Estado Global

Actualmente se usa estado local con `useState`. Para escalar:
- Considerar Redux Toolkit para estado global
- Implementar React Query para cache de API
- Agregar optimistic updates

### Responsive Design

Todas las páginas son responsive:
- Desktop: Grid de 3-4 columnas
- Tablet: Grid de 2 columnas
- Mobile: Stack vertical

---

## 🔗 Referencias

- **Documentación de arquitectura**: `docs/INTENT-ROUTING-ARCHITECTURE.md`
- **Plan de implementación**: `docs/INTENT-ROUTING-IMPLEMENTATION-PLAN.md`
- **API endpoints**: `src/lib/axios.ts`
- **Types**: `src/aiagentflow/pages/intents/types.ts`
- **Routes**: `src/routes/paths.ts`, `src/routes/sections/dashboard.tsx`
- **Navigation**: `src/layouts/nav-config-dashboard.tsx`

---

## ✨ Próximos Pasos

1. **Backend Integration**: Conectar con APIs reales
2. **Testing**: Agregar tests unitarios con Jest/Vitest
3. **Refinamiento UX**: Mejorar transiciones y feedback
4. **Real-time**: Implementar updates en tiempo real
5. **Analytics**: Agregar tracking de eventos
6. **Exportación**: Implementar export a CSV/JSON
7. **Bulk Actions**: Agregar acciones masivas
8. **Advanced Filters**: Más opciones de filtrado

---

**Status**: ✅ Frontend MVP Completo y Funcional

**Tecnologías**: React 18, TypeScript, MUI v6, Axios, React Router, Date-fns

**Compatibilidad**: Listo para integración con backend Phase 1 completado
