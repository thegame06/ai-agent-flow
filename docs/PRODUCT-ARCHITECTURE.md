# Product Architecture — Studio, Connect y Control

## Objetivo

Definir la arquitectura de producto de AgentFlow con una organización por **suites funcionales** (no por categorías técnicas), estableciendo:

1. Navegación principal centrada en **Studio** y **Connect**.
2. Reorganización de rutas frontend por suite.
3. Alineación backend por bounded contexts (`studio/*`, `connect/*`, `control/*`).
4. Modelo RBAC por suite y capacidades (`builder`, `operator`, `admin`, `compliance`).

---

## 1) Navegación y dominios funcionales

### Suites principales

- **Studio**: diseño, configuración y evolución de agentes, flujos y políticas de diseño.
- **Connect**: operación en canales, sesiones, integraciones y ejecución en tiempo real.

### Suite transversal

- **Control**: gobierno, seguridad, observabilidad, compliance y administración de plataforma.

### IA de navegación propuesta

```text
AgentFlow
├── Studio
│   ├── Overview
│   ├── Agents
│   ├── Flows
│   ├── Prompts
│   ├── Policies (design-time)
│   ├── Evaluations (offline)
│   └── Experiments
├── Connect
│   ├── Overview
│   ├── Channels
│   ├── Sessions
│   ├── Conversations
│   ├── Live Executions
│   ├── Integrations
│   └── Incidents
└── Control
    ├── Access (RBAC)
    ├── Compliance
    ├── Audit
    ├── Runtime Policies
    ├── Tenants & Settings
    └── Observability
```

### Principios de UX/IA

- La navegación de primer nivel muestra primero **Studio** y **Connect**.
- **Control** permanece visible pero como dominio de gobierno transversal.
- Cada pantalla debe declarar explícitamente su suite para trazabilidad de ownership.

---

## 2) Reorganización de rutas/frontend por suite

Se abandona la agrupación por tecnología (`/agents`, `/policies`, `/sessions`) y se adopta una agrupación por producto.

## Rutas objetivo

### Studio

- `/studio`
- `/studio/agents`
- `/studio/flows`
- `/studio/prompts`
- `/studio/policies`
- `/studio/evaluations`
- `/studio/experiments`

### Connect

- `/connect`
- `/connect/channels`
- `/connect/sessions`
- `/connect/conversations`
- `/connect/executions`
- `/connect/integrations`
- `/connect/incidents`

### Control

- `/control`
- `/control/access`
- `/control/compliance`
- `/control/audit`
- `/control/runtime-policies`
- `/control/tenants`
- `/control/observability`

## Convenciones de implementación frontend

- Carpeta por suite:
  - `frontend/.../src/suites/studio/*`
  - `frontend/.../src/suites/connect/*`
  - `frontend/.../src/suites/control/*`
- Cada suite define:
  - `routes.tsx`
  - `pages/*`
  - `components/*`
  - `services/*`
  - `permissions.ts`
- Layout y breadcrumbs derivados de la suite para consistencia visual.

## Estrategia de migración de rutas

1. Introducir rutas nuevas en paralelo.
2. Mantener redirects 301/302 internos desde rutas legacy.
3. Actualizar menús y deep-links.
4. Deprecar rutas legacy después de dos releases estables.

---

## 3) Alineación backend por bounded contexts

Se define una organización explícita por contextos de dominio:

- `studio/*`
- `connect/*`
- `control/*`

## Mapeo de responsabilidades

### Studio context

Incluye capacidades de diseño y preparación:

- Definición/versionado de agentes
- DSL y plantillas
- Prompt profiles
- Evaluación offline y experimentación

### Connect context

Incluye capacidades operativas online:

- Canales (WhatsApp/Web/API)
- Sesiones y conversaciones
- Ejecuciones en vivo
- Integraciones y manejo de incidentes

### Control context

Incluye gobierno y seguridad:

- RBAC y perfiles de acceso
- Auditoría y retención
- Policies runtime
- Configuración multi-tenant
- Observabilidad y compliance

## Convenciones para API y aplicación

- Endpoints REST agrupados por prefijo de suite:
  - `/api/studio/*`
  - `/api/connect/*`
  - `/api/control/*`
- Application services y handlers por namespace/carpeta de contexto.
- Prohibido acceso cruzado directo entre contextos; usar contratos explícitos (interfaces/eventos).
- Eventos de dominio con prefijo de suite (`studio.agent.published`, `connect.session.closed`, etc.).

---

## 4) RBAC por suite y capacidades

RBAC combina:

- **Ámbito de suite** (`studio`, `connect`, `control`)
- **Capacidad** (`builder`, `operator`, `admin`, `compliance`)
- **Acción** (`read`, `write`, `publish`, `execute`, `approve`, `audit`)

## Matriz base de roles

| Rol | Studio | Connect | Control |
|---|---|---|---|
| `builder` | Diseña, edita y publica artefactos | Solo lectura operativa | Sin acceso por defecto |
| `operator` | Lectura de configuraciones publicadas | Opera sesiones/canales/ejecuciones | Acceso limitado a tableros |
| `admin` | Administra configuración y releases | Administra operación e integraciones | Administra tenants, políticas y accesos |
| `compliance` | Revisión de artefactos críticos | Revisión de eventos operativos | Auditoría, evidencias y aprobaciones |

## Modelo de permisos (claim recomendado)

Formato:

```text
{suite}:{resource}:{action}
```

Ejemplos:

- `studio:agents:write`
- `studio:policies:publish`
- `connect:sessions:execute`
- `connect:channels:admin`
- `control:audit:read`
- `control:compliance:approve`

## Reglas de gobierno

- `publish` en Studio requiere al menos `builder` + policy de aprobación (si aplica).
- Acciones sensibles en Connect (shutdown de canal, replay masivo) requieren `admin`.
- Acceso a evidencias/auditoría requiere `compliance` o `admin` de Control.
- Denegación por defecto cuando no exista grant explícito.

---

## 5) Plan de implementación incremental

## Fase 1 — Arquitectura y contrato

- Publicar este documento como referencia oficial de producto.
- Congelar naming de suites/rutas/permisos.
- Definir checklist de DoR/DoD por suite.

## Fase 2 — Frontend

- Crear estructura por suites y mover páginas.
- Introducir prefijos `/studio`, `/connect`, `/control`.
- Agregar capa de compatibilidad con rutas legacy.

## Fase 3 — Backend

- Agrupar controladores y handlers por bounded context.
- Normalizar endpoints con prefijo de suite.
- Alinear eventos y contratos entre contextos.

## Fase 4 — Seguridad y operación

- Implementar claims RBAC por suite.
- Instrumentar auditoría de decisiones de autorización.
- Validar segregación de deberes en flujos críticos.

---

## KPIs de éxito

- 100% de páginas mapeadas a una suite (`studio`, `connect`, `control`).
- 100% de endpoints bajo prefijo de contexto.
- 0 permisos globales ambiguos sin suite.
- Reducción de tiempo de descubrimiento de navegación (UX) y de onboarding técnico.

---

## Decisión

A partir de esta definición, **Studio** y **Connect** son las entradas primarias de producto, y **Control** opera como suite de gobierno transversal. Toda evolución de frontend, backend y seguridad debe respetar esta segmentación.
