
# AgentFlow — AI Agent Orchestration & Governance Platform

## 🎯 IDENTIDAD DEL PRODUCTO

> AI Agent Orchestration & Governance Platform
> Self-Hosted First · Single-Region · Enterprise-Grade · Extensible como Elsa · Gobernable · Versionada

Comparable en filosofía a:
- **Elsa Workflows** (extensibilidad + DSL + designer visual)
- **OPA / Open Policy Agent** (policy engine centralizado y transversal)
- **Temporal** (orquestación stateful con durabilidad)
- **Kubernetes** (control plane declarativo)

Pero aplicado a: **AI Agents, LLMs, sistemas event-driven**.

---

## 🧱 LOS 10 PRINCIPIOS CORE (nunca violar)

1. El LLM **no controla** el loop — el Runtime controla el loop
2. El DSL es la **fuente única de verdad** del comportamiento del agente
3. **Todo es versionado** — AgentDefinition, PolicySet, PromptProfile, Tools, ModelRouting, FeatureFlags
4. **Nada crítico es mutable en caliente** — cambios = nueva versión
5. **Todo es extensible por contrato** — assembly loadable, interface-first
6. **Todo es auditable** — cada decisión LLM, tool call, policy check
7. **Estado persistente + cache distribuido** — MongoDB truth + Redis hot state
8. **Determinismo por defecto** — temperatura 0.0, seed registrado, no hot reload
9. **Gobernanza centralizada** — Policy Engine transversal
10. **Self-hosted first** — sin dependencia de cloud vendor

---

## 🏗 ARQUITECTURA

### 14 Subsistemas

```
AgentFlow Platform
 ├── 1.  Runtime Engine           ← Loop: Think→Validate→PolicyCheck→Tool→Observe
 ├── 2.  DSL Engine               ← Parser, validator, versioning
 ├── 3.  Policy Engine            ← Transversal: 6 checkpoints, 4 action types
 ├── 4.  Prompt Engine            ← Composable blocks, jerarquía tenant/agent
 ├── 5.  Memory Architecture      ← MongoDB (truth) + Redis (hot) + Qdrant (vector)
 ├── 6.  Model Routing            ← Static/task/policy/fallback-chain
 ├── 7.  Event-Driven Transport   ← Conversational/Batch/Cron/Domain events
 ├── 8.  Evaluation Engine        ← QualityScore, HallucinationRisk, ComplianceScore
 ├── 9.  Experimentation Layer    ← Shadow/Canary/Feature flags/Segment rules
 ├── 10. Test Runner              ← CI/CD para agentes, suite por versión
 ├── 11. Sandbox / Debugger       ← Timeline visual, replay, comparación versiones
 ├── 12. Extension System         ← Assembly loading tipo Elsa
 ├── 13. Telemetry Engine         ← OpenTelemetry: traces + metrics + logs
 └── 14. Multi-Tenant Security    ← RBAC, TenantContext, audit WORM, sandboxing
```

### Stack Tecnológico

- **.NET 9 / C#** — Backend + engines
- **Semantic Kernel** — SOLO invocación LLM (nunca controla estado ni auth)
- **MongoDB** — Source of truth (TenantId siempre primer índice)
- **Redis** — Working memory + distributed locks (nunca prompt completo)
- **Qdrant** — Vector memory (opcional)
- **React** — Agent Designer + Sandbox + Dashboard
- **OpenTelemetry** — Trazabilidad completa

### Deployment Model

```
Self-Hosted · Single Region · Multi-Node

Load Balancer → API Nodes (N) + Worker Pool (N) + Designer Backend
                     ↕                  ↕
              Redis Cluster       MongoDB Replica Set
                     ↕
              Qdrant (opcional)
```

---

## 🤖 AGENT DSL

El DSL define: Tools · Policies · Flows · Triggers · Agent-to-Agent · Runtime config · Model routing · Feature flags · Experimentos · Evaluación segmentada

### Modos de Ejecución

| Modo | Descripción | Temperatura default |
|---|---|---|
| `deterministic` | DSL define todos los pasos | 0.0 (obligatorio) |
| `hybrid` | DSL define flows, LLM elige ejecutor | 0.1-0.4 |
| `autonomous` | LLM decide dentro de límites | configurable + seed |

---

## 🔁 AGENT LIFECYCLE

```
Draft → Test → Evaluate → Publish → Monitor → Recalibrate → Version Upgrade
```

**Versionado obligatorio**: AgentDefinition · PolicySet · PromptProfile · ToolDefinition · ModelRoutingConfig · FeatureFlagSet · ExperimentConfig

**AgentDefinition Published = INMUTABLE**. Cualquier cambio = nueva versión semver.

---

## 🛡 POLICY ENGINE

Evalúa en 6 puntos: PreAgent → PreLLM → PostLLM → PreTool → PostTool → PreResponse

Acciones: Blocking · Warning · Escalation · Shadow

Policies son versionadas, agrupadas en PolicySets, ligadas a AgentDefinition publicada.
**No hot reload** — cambio de policy = nueva versión del PolicySet = nueva versión del agente.

---

## 🧠 MEMORY

| Tipo | Store | Contenido |
|---|---|---|
| Working Memory | Redis + TTL | Estado estructurado (nunca prompt completo) |
| Session Memory | Redis + TTL | Contexto cross-ejecución |
| Token Budget | Redis | Contador de tokens |
| Distributed Lock | Redis | Lock por executionId (anti-duplicados) |
| Long-Term | MongoDB | Hechos persistentes |
| Audit | MongoDB WORM | Steps, decisiones (insert+find only) |
| Vector | Qdrant | Embeddings para búsqueda semántica |

---

## 🔧 EXTENSIBILIDAD

Via assembly loading (tipo Elsa):
Tools · Flow Nodes · Event Sources · Prompt Blocks · Memory Providers · Model Providers · Policy Providers · Evaluators

Isolation: In-process (safe) vs Out-of-process (heavy/critical tools)

---

## 📦 PROYECTOS .NET

```
src/
├── AgentFlow.Abstractions          (NuGet público — interfaces exportables)
├── AgentFlow.Domain
├── AgentFlow.Application
├── AgentFlow.Runtime               (Engine principal)
├── AgentFlow.DSL
├── AgentFlow.Policy
├── AgentFlow.Prompting
├── AgentFlow.Evaluation
├── AgentFlow.ModelRouting
├── AgentFlow.Memory
├── AgentFlow.Events
├── AgentFlow.Extensions
├── AgentFlow.Persistence.Mongo
├── AgentFlow.Caching.Redis
├── AgentFlow.Observability
├── AgentFlow.Security
├── AgentFlow.Api
├── AgentFlow.Worker
└── AgentFlow.Designer.Backend

frontend/
├── designer/      (Visual Agent Designer)
├── sandbox/       (Debugger + viewer)
├── dashboard/     (Monitoring + evaluation)
└── policy-editor/ (Policy editor visual)
```

---

## 📋 REGLAS DE RESPUESTA

- No dar explicaciones genéricas
- No responder superficialmente
- No repetir definiciones básicas de qué es un agente
- Pensar como arquitecto senior en plataforma enterprise
- Incluir código C# production-grade cuando aplique
- Siempre interfaz primero, implementación después
- Priorizar: seguridad + gobernanza + extensibilidad + escalabilidad
- Si una decisión viola un principio core, decirlo explícitamente
- Validar que `dotnet build` pasa antes de declarar éxito
- El Policy Engine debe tener hooks en toda feature nueva

---

## 🗺 ROADMAP

### Fase 1 — MVP (Completado)
- [x] Domain Layer (aggregates, VOs, repos)
- [x] Application Layer (interfaces)
- [x] Runtime Engine básico (Think→Act→Observe)
- [x] Security Layer (TenantContext, RBAC)
- [x] Infrastructure (MongoDB repositories + índices)
- [x] Observability (OpenTelemetry)
- [x] REST API (controllers, DI, JWT)

### Fase 2 — Core Platform
- [ ] AgentFlow.Abstractions (NuGet package público)
- [ ] DSL Engine (parser, validator, lifecycle states)
- [ ] Policy Engine (6 checkpoints, 4 action types)
- [ ] Prompt Engine (composable blocks)
- [ ] Evaluation Engine (4 scores + hallucination detection)
- [ ] Model Routing (IModelRouter, fallback chains)
- [ ] Redis Memory (working memory + distributed locks)
- [ ] Event Transport (IAgentEventSource)
- [ ] Test Runner (CLI + API)

### Fase 3 — Full Platform
- [ ] Extension System (assembly loading)
- [ ] Experimentation Layer (shadow/canary/flags)
- [ ] Worker Service (background agents)
- [ ] Agent Designer (React visual)
- [ ] Sandbox UI (debugger visual)
- [ ] Evaluation Dashboard
- [ ] Policy Editor visual
- [ ] Multi-region readiness
