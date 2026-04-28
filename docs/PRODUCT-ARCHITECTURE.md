# Product Architecture — Studio, Connect y Control

## Objetivo

Definir el modelo oficial de producto de AgentFlow con tres suites funcionales:

- **Studio** (Build Time)
- **Connect** (Run Time)
- **Control** (Governance Time)

Este documento alinea navegación, API, nomenclatura, capacidades y métricas operativas.

---

## 1) Narrativa de producto

### Propuesta de valor por suite

- **Studio**: convertir ideas de automatización en agentes publicables.
- **Connect**: operar agentes en producción sobre canales e integraciones.
- **Control**: asegurar que la operación sea segura, auditable y compliant.

### Mapa de navegación

```text
AgentFlow
├── Studio
│   ├── Overview
│   ├── Agents
│   ├── Flows
│   ├── Prompts
│   ├── Evaluations
│   └── Releases
├── Connect
│   ├── Overview
│   ├── Channels
│   ├── Sessions
│   ├── Conversations
│   ├── Live Executions
│   └── Incidents
└── Control
    ├── Access
    ├── Policies
    ├── Audit
    ├── Observability
    └── Compliance
```

Principios UX:
1. Studio y Connect son las entradas principales.
2. Control siempre visible como capa transversal.
3. Toda pantalla declara `suite` y `owner` explícitos.

---

## 2) Matriz de capacidades (suite × público objetivo)

> Ver versión detallada en [`CAPABILITY-MATRIX.md`](./CAPABILITY-MATRIX.md).

| Suite | Qué hace | Para quién | Resultado esperado |
|---|---|---|---|
| Studio | Diseña, prueba, publica y versiona agentes/flows/prompts | Product Builder, AI Engineer, Tech Lead | Menor tiempo de diseño y paso a release controlado |
| Connect | Ejecuta conversaciones, canales y sesiones en vivo; opera incidentes | Agent Operator, CX Ops, NOC | Mayor estabilidad operativa y menor MTTR |
| Control | Define acceso, políticas runtime, auditoría y compliance | Platform Admin, Security, Compliance Officer | Menor riesgo, mayor trazabilidad y cumplimiento |

---

## 3) Nomenclatura unificada UI/API/Documentación

### Convención canónica

- **Suite names:** `Studio`, `Connect`, `Control`
- **UI path prefix:** `/studio/*`, `/connect/*`, `/control/*`
- **API path prefix:** `/api/studio/*`, `/api/connect/*`, `/api/control/*`
- **Doc taxonomy:** `docs/studio-*`, `docs/connect-*`, `docs/control-*` (o secciones equivalentes por suite)

### Tabla de normalización de términos

| Antes (legacy) | Nuevo término canónico | Scope |
|---|---|---|
| Designer | Studio | UI + Docs |
| Operations / Runtime | Connect | UI + API + Docs |
| Governance / Admin Plane | Control | UI + API + Docs |
| Agents (genérico) | Studio / Agents | UI |
| Sessions (genérico) | Connect / Sessions | UI + API |
| Policies (ambigua) | Control / Runtime Policies o Studio / Design Policies | UI + API + Docs |

### Regla editorial

Cuando un recurso aparezca en UI/API/docs, debe incluir su suite en nombre o contexto. Ejemplo:
- `Studio Agents`
- `Connect Sessions`
- `Control Runtime Policies`

---

## 4) KPIs por suite

### Studio (adopción y velocidad de entrega)

- **Adopción Studio**: % de builders activos semanalmente.
- **Tiempo a primer agente publicado**: desde creación hasta primer `publish` exitoso.
- **Lead time de cambio de agente**: edición → validación → release.
- **Tasa de publicación exitosa**: releases aprobados / releases intentados.

### Connect (operación y confiabilidad)

- **Tiempo a producción (operacional)**: desde release hasta tráfico real en canal.
- **SLA de ejecución**: % ejecuciones dentro de umbral de latencia objetivo.
- **MTTR de incidentes**: tiempo promedio de recuperación.
- **Tasa de continuidad de sesión**: sesiones que completan flujo sin interrupción.

### Control (riesgo, calidad y cumplimiento)

- **Cobertura de auditoría**: % de acciones críticas con traza completa.
- **Tiempo de aprobación de policy**: propuesta → aprobación → aplicación.
- **Tasa de violaciones de policy**: incidencias por 1,000 ejecuciones.
- **Calidad de operación**: score compuesto de compliance + seguridad + error budget.

---

## 5) Plan de implementación incremental

### Fase 1 — Contrato de producto
- Congelar narrativa Studio/Connect/Control.
- Publicar matriz de capacidades y reglas de naming.

### Fase 2 — UI + API
- Migrar rutas a prefijos de suite.
- Normalizar nombres de menús, breadcrumbs y endpoints.

### Fase 3 — Métricas y gobierno
- Instrumentar KPIs por suite en dashboards.
- Activar revisiones trimestrales contra roadmap.

---

## Decisión

AgentFlow adopta oficialmente un modelo de producto por suites donde:
- **Studio** optimiza creación,
- **Connect** optimiza operación,
- **Control** optimiza gobierno.

Toda evolución de UX, API y documentación debe respetar esta segmentación.
