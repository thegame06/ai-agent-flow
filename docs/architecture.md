# AgentFlow — AI Agent Orchestration & Governance Platform
## Arquitectura Técnica v3.0

> Comparable en filosofía a: Elsa Workflows + OPA + Temporal + Kubernetes Control Plane
> Aplicado a: AI Agents, LLMs, Systems Event-Driven

---

> Arquitectura de producto por suites (Studio/Connect/Control): ver [`PRODUCT-ARCHITECTURE.md`](./PRODUCT-ARCHITECTURE.md).

## 🎯 Decisión Arquitectónica Central

**DSL Composable + Runtime Controlado + Gobernanza Centralizada**

```
❌ NO: LLM controla el loop
❌ NO: Comportamiento hardcodeado en código
❌ NO: Single binary monolítico
✅ SÍ: Runtime controla el loop. DSL define el comportamiento. Policy Engine es el árbitro.
```

| Atributo | Cómo se logra |
|---|---|
| Control | Runtime Engine con máquina de estados explícita |
| Determinismo | Temperatura 0.0 por defecto, seed registrado, no hot reload |
| Observabilidad | Cada step = span OTel. Rationale del LLM = span event. |
| Calidad medible | Evaluation Engine con 4 scores por ejecución |
| Seguridad enterprise | Policy Engine transversal, RBAC, audit WORM, sandboxing |
| Extensibilidad | 6 tipos de extensión fijos: Tool, PolicyEvaluator, ModelProvider, MemoryProvider, Evaluator, PromptStore |
| **Orchestration Muscle** | Integración nativa con **Microsoft Agent Framework (MAF)** para colaboración A2A. |
| **Gobernanza Layer** | Control Plane superior que envuelve a MAF en una capa de Trust inmutable. |

---

## 🏗 Arquitectura por Capas

```
┌─────────────────────────────────────────────────────────────────┐
│                      Ingress Layer                              │
│           API Gateway  ·  WebSockets  ·  Event Bus             │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Orchestration Layer                          │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐ │
│  │  Runtime    │  │  DSL Engine  │  │   Event Transport      │ │
│  │  Engine     │  │              │  │                        │ │
│  └──────┬──────┘  └──────┬───────┘  └────────────────────────┘ │
│         │                │                                      │
│  ┌──────▼────────────────▼──────────────────────────────────┐  │
│  │         AgentExecution State Machine (Source of Truth)   │  │
│  └──────────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Intelligence Layer                           │
│  ┌──────────────┐  ┌─────────────┐  ┌───────────────────────┐  │
│  │ Prompt Engine│  │ Model Router│  │   Memory Service      │  │
│  │ (composable  │  │ (IModelRouter│  │  Working/LT/Vector    │  │
│  │  blocks)     │  │  + fallback) │  │                       │  │
│  └──────────────┘  └─────────────┘  └───────────────────────┘  │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Governance Layer                             │
│  ┌──────────────┐  ┌─────────────┐  ┌───────────────────────┐  │
│  │Policy Engine │  │  Evaluation │  │  Experimentation      │  │
│  │(transversal) │  │   Engine    │  │  (Shadow/Canary/Flags) │  │
│  └──────────────┘  └─────────────┘  └───────────────────────┘  │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Persistence Layer                            │
│         Redis (hot state)         MongoDB (source of truth)    │
│         Qdrant (vector — opt)     S3/Blob (artifacts — opt)    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 1️⃣ Runtime Engine

**El corazón. Implementa la máquina de estados del agente.**

### Loop

```
[Event/Trigger In]
        │
        ▼
[Policy: PreAgent] ──BLOCK──► Rejected
        │
        ▼
[THINK: LLM call]
        │
        ▼
[Validate: Schema + TokenBudget] ──FAIL──► Error
        │
        ▼
[Policy: PreTool] ──BLOCK──► Halt
        │
        ▼
[Tool Execute + Sandbox]
        │
        ▼
[Policy: PostTool] ──ESCALATE──► HumanReview
        │
        ▼
[OBSERVE: LLM interprets result]
        │
        ▼
[Goal achieved?] ──NO──► back to THINK (max iterations)
        │ YES
        ▼
[Policy: PreResponse] ──BLOCK──► Suppress response
        │
        ▼
[Audit: WORM write]
        │
        ▼
[Evaluation Engine: async]
        │
        ▼
[Response to caller]
```

### Contrato Central

```csharp
// En: AgentFlow.Abstractions
public interface IAgentExecutor
{
    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default);

    Task<Result> CancelAsync(
        string executionId,
        string tenantId,
        string cancelledBy,
        CancellationToken ct = default);
}
```

### Runtime Modes (del DSL)

```json
{
  "runtime": {
    "mode": "deterministic",
    "temperature": 0.0,
    "allowOverride": false,
    "seedRegistered": true,
    "maxIterations": 6,
    "maxExecutionSeconds": 120
  }
}
```

**Regla**: Config exacta persistida en `AgentExecution` en el momento del inicio. Inmutable durante la ejecución.

---

## 2️⃣ DSL Engine

**El contrato entre product owner y runtime.**

### Schema Completo

```json
{
  "agent": {
    "key": "CustomerSupportAgent",
    "version": "2.1.0",
    "role": "Asistente financiero nivel 1",

    "runtime": {
      "mode": "hybrid",
      "temperature": 0.2,
      "allowOverride": false
    },

    "modelRouting": {
      "strategy": "task-based",
      "default": "gpt-4o",
      "routingRules": [
        { "taskType": "classification", "model": "gpt-4o-mini" },
        { "taskType": "generation", "model": "gpt-4o" }
      ],
      "fallbackChain": ["gpt-4o", "gpt-3.5-turbo", "local-llama"]
    },

    "promptProfile": "support-agent-v3",

    "flows": [
      {
        "name": "LoanStatusFlow",
        "trigger": { "type": "intent", "value": "LoanStatus" },
        "steps": [
          { "tool": "GetCustomerByPhone", "required": true, "policyGroup": "pii-access" },
          { "tool": "GetLoanStatus", "required": true }
        ],
        "guardrails": { "failOnToolError": true, "requireAllSteps": true }
      }
    ],

    "authorizedTools": ["GetCustomerByPhone", "GetLoanStatus", "SearchKB"],

    "policies": {
      "policySetId": "fintech-standard-v2",
      "maxSteps": 6,
      "requireToolValidation": true,
      "allowParallelTools": false,
      "humanReviewOnEscalation": true
    },

    "evaluation": {
      "enableQualityScoring": true,
      "qualityThreshold": 0.80,
      "requireHumanReviewOnScoreBelow": 0.70,
      "enableHallucinationDetection": true,
      "evaluatorId": "llm-judge-gpt4o"
    },

    "experiment": {
      "featureFlags": ["new-kb-connector", "extended-memory"],
      "canaryWeight": 0.10,
      "shadowEvalEnabled": true
    },

    "testSuite": {
      "testCases": [
        {
          "name": "Loan status - happy path",
          "input": "¿Ya me aprobaron el préstamo?",
          "expectedIntent": "LoanStatus",
          "expectedTools": ["GetCustomerByPhone", "GetLoanStatus"],
          "expectedOutputContains": ["aprobado", "préstamo"],
          "maxDurationMs": 5000,
          "tags": ["smoke", "regression"]
        }
      ]
    }
  }
}
```

### Validaciones Design-Time (Obligatorias)

1. `key` único por tenant (case-insensitive)
2. Todos los tools en `flows` existen en `authorizedTools`
3. Todos los tools en `authorizedTools` están registrados y activos
4. `policySetId` apunta a un PolicySet publicado
5. `promptProfile` apunta a un PromptProfile versionado publicado
6. `version` > versión actual del agente (no downgrade)
7. `modelRouting.fallbackChain` — todos los modelos existen en IModelRegistry
8. Test cases tienen al menos 1 entry en producción

---

## 3️⃣ Policy Engine

**Transversal. Evalúa en 6 puntos del loop. No hot reload.**

### Puntos de Evaluación

```
PreAgent → PreLLM → PostLLM → PreTool → PostTool → PreResponse
```

### Policy Modes

| Modo | Descripción |
|---|---|
| `Blocking` | Detiene ejecución. Resultado: `PolicyViolation`. |
| `Warning` | Continúa. Loguea. Genera metric. |
| `Escalation` | Pausa. Notifica. Espera human approval. |
| `Shadow` | Evalúa sin bloquear. Para observabilidad y calibración. |

### Ejemplo de Policy (JSON)

```json
{
  "policySetId": "fintech-standard-v2",
  "version": "2.0.0",
  "policies": [
    {
      "id": "no-pii-in-response",
      "description": "La respuesta no debe contener números de cuentas o SSN",
      "appliesAt": "PreResponse",
      "type": "regex-check",
      "config": { "pattern": "\\b\\d{9}\\b|\\b\\d{4}-\\d{4}\\b" },
      "action": "Blocking",
      "severity": "Critical"
    },
    {
      "id": "hallucination-guard",
      "description": "Bloquea si HallucinationRisk >= High",
      "targetSegments": ["Standard", "VIP"],
      "appliesAt": "PostLLM",
      "type": "evaluation-threshold",
      "config": { "metric": "HallucinationRisk", "maxAllowed": "Medium" },
      "action": "Blocking",
      "severity": "High"
    }
  ]
}
```

### Segment Rules (v3.1)

Las políticas pueden filtrarse por `TargetSegments`. Un agente en ejecución recibe el contexto del usuario (según sus claims) y solo aplica las reglas pertinentes. Esto permite, por ejemplo, tener guardrails más estrictos para usuarios Trial o reglas de negocio específicas para clientes VIP.

### Contratos

```csharp
// En: AgentFlow.Abstractions
public interface IPolicyEngine
{
    Task<PolicyResult> EvaluateAsync(
        PolicyCheckpoint checkpoint,
        PolicyContext context,
        CancellationToken ct = default);
}

public enum PolicyCheckpoint
{
    PreAgent, PreLLM, PostLLM, PreTool, PostTool, PreResponse
}

public sealed record PolicyResult
{
    public PolicyDecision Decision { get; init; } // Allow | Block | Escalate | Warn
    public IReadOnlyList<PolicyViolation> Violations { get; init; } = [];
}
```

---

## 4️⃣ Prompt Engine

**Prompt blocks composables. Nunca hardcodeados. Nunca monolíticos.**

### Jerarquía de Composición

```
Global Template (plataforma)
    └── Tenant Override
            └── Agent PromptProfile
                    └── Flow Context
                            └── Step-level injection
```

### Ejemplo de PromptProfile

```json
{
  "promptProfileId": "support-agent-v3",
  "version": "3.0.0",
  "blocks": [
    { "id": "role", "content": "Eres un asistente de soporte de {{tenant.name}}." },
    { "id": "security-rules", "ref": "global.security-invariants-v2" },
    { "id": "tools-context", "type": "dynamic", "source": "available-tools" },
    { "id": "memory-context", "type": "dynamic", "source": "working-memory-summary" },
    { "id": "output-format", "ref": "global.json-output-schema-v1" }
  ],
  "maxTokens": 4096
}
```

### Persistence (v3.1)

Los perfiles se almacenan en la colección `prompt_profiles`. El `IPromptProfileStore` permite cargar versiones específicas en tiempo de ejecución, asegurando que los cambios en prompts globales no rompan agentes en producción sin un despliegue controlado.

---

## 5️⃣ Memory Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  REDIS (Hot State — Ephemeral)                               │
│  ├── WorkingMemory:{executionId}  — estado actual (struct)   │
│  ├── SessionMemory:{sessionId}    — cross-ejecución          │
│  ├── TokenBudget:{tenantId}       — contador de tokens       │
│  └── DistributedLock:{execId}     — evita duplicados         │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  MONGODB (Source of Truth — Persistent)                      │
│  ├── agent_definitions           — DSL versionado           │
│  ├── agent_executions            — state machine history    │
│  ├── agent_steps                 — steps append-only        │
│  ├── evaluation_results          — scores por ejecución     │
│  ├── tool_execution_logs         — WORM (insert+find only)  │
│  ├── policy_sets                 — policies versionadas     │
│  ├── prompt_profiles             — prompts versionados      │
│  └── test_case_results           — historial de CI/CD       │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  QDRANT (Vector — Opcional)                                  │
│  └── tenant_{id}_kb              — embeddings por tenant    │
└──────────────────────────────────────────────────────────────┘
```

**Invariante**: Nunca guardar prompt completo en Redis. Solo estado estructurado reconstruible.

---

## 6️⃣ Model Routing

```csharp
// En: AgentFlow.Abstractions
public interface IModelRouter
{
    Task<ModelSelection> SelectModelAsync(
        ModelRoutingRequest request,
        CancellationToken ct = default);
}

public interface IModelProvider
{
    string ProviderId { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
```

### Estrategias de Routing

| Estrategia | Cuándo usar |
|---|---|
| `static` | Un solo modelo. Reproducibilidad máxima. |
| `task-based` | Diferente modelo por tipo de task (classify→small, generate→large) |
| `policy-based` | Reglas de negocio (datos PII → modelo local, no cloud) |
| `fallback-chain` | Si modelo principal falla → siguiente en la cadena |

**Cada ejecución registra**: modelo usado, fallback aplicado, tokens consumidos, costo estimado.

---

## 7️⃣ Orchestration & Skills: Agents as Tools

**Decisión Guru: Abstraer la complejidad de colaboración.**

En AgentFlow, una **Skill** no es una interfaz de código nueva, sino un **Agente especializado** expuesto como una `Tool` para otros agentes. Esto permite:

1. **Recursividad Infinita**: Un agente "Manager" puede usar agentes "Especialistas" como herramientas.
2. **Encapsulamiento de Políticas**: Cada sub-agente mantiene su propio `PolicySet`, pero el agente padre hereda la responsabilidad de auditoría.
3. **Microsoft Agent Framework Integration**: Usamos MAF como el protocolo de comunicación "bajo el capó" para conversaciones Agente-a-Agente (A2A), pero AgentFlow captura cada mensaje para el Audit Trail WORM.

### Jerarquía de Invocación
```
Agent: Principal
  └── Policy Check: PreTool (AgentAsTool)
  └── Invoke Agent: Especialista (via MAF)
      └── Internal Loop: Especialista
      └── Policy Check: PostLLM (Especialista)
  └── Policy Check: PostTool (AgentAsTool)
```

---

## 8️⃣ Event-Driven Transport

```csharp
// En: AgentFlow.Abstractions
public interface IAgentEventSource
{
    string SourceType { get; }
    IAsyncEnumerable<AgentEvent> StreamAsync(CancellationToken ct = default);
}

public interface IAgentEventTransport
{
    Task PublishAsync(AgentEvent @event, CancellationToken ct = default);
    Task<IAsyncDisposable> SubscribeAsync(
        string agentKey,
        Func<AgentEvent, Task> handler,
        CancellationToken ct = default);
}
```

**Event sources soportados**: Conversational (HTTP/WebSocket) · Batch (file/queue) · Domain Events · Cron/Schedule · Custom (via extensión)

---

## 9️⃣ Evaluation Engine

Ver: `.agent/workflows/evaluation-engine.md`

**Scores por ejecución**:
- `QualityScore` (LLM juez externo independiente)
- `PolicyComplianceScore` (determinístico)
- `HallucinationRisk` (comparación estructural tool output vs respuesta)
- `ToolUsageAccuracy` (vs expectedTools)

**Regla de alucinación**:
```
Tool retorna: { amount: 3000 }
Respuesta dice: "préstamo de $5,000"
→ HallucinationRisk = Critical
→ QualityScore = 0
→ Human review triggered
```

---

## 🔟 Experimentation Layer

| Feature | Descripción |
|---|---|
| Shadow Evaluation | Nueva versión ejecuta en paralelo (lectura), sin afectar respuesta real |
| Canary Rollout | % determinístico de tráfico enrutado a nueva versión |
| Feature Flags | Activación granular por agente/tenant/segment |
| Segment Rules | Diferente umbral de evaluación por tipo de input o usuario |

**Todo versionado. Todo auditable. Activación requiere aprobación.**

---

## 🔧 Extension System — Modelo Simplificado

> **Decisión de diseño (v3.1)**: El pipeline de ejecución es **estructural y fijo**.
> No existe middleware de negocio extensible. La lógica de negocio va en `IToolPlugin`
> o en el Agent (vía DSL). La gobernanza va en `IPolicyEngine`.

### Los 5 tipos de extensión válidos

```
                        AgentFlow.Abstractions
                              │
    ┌─────────┬─────────┬─────────┬─────────┬─────────┐
    IToolPlugin  IPolicyEval  IModelProvider IMemoryStore  IAgentEvaluator
└─────────┴─────────┴─────────┴─────────┴─────────┘
   Tool logic   PreAgent/    Azure,OpenAI   Redis/Mongo   LLM judge
    (IO, API)   PostLLM,     Anthropic,     InMemory      Domain-specific
                etc.         Local...                     quality checks
```

### Isolation de Tools — por `IToolPlugin.RiskLevel`

```csharp
public enum ToolRiskLevel
{
    Low     = 0,  // In-process, timeout engine-enforced
    Medium  = 1,  // In-process, strict timeout + resource limits
    High    = 2,  // In-process, additional auditing + rate limits
    Critical = 3  // Out-of-process [v3] — sidecar, no network access
}
```

> `ExtensionIsolationMode` como enum genérico fue **eliminado** (v3.1).
> El isolation mode es una propiedad del Tool, no un concepto de infraestructura genérico.

### Qué NO es una extensión (y por qué)

| Necesidad | Solución correcta | Por qué NOT middleware |
|---|---|---|
| Validar eligibilidad | `EligibilityTool` | Auditable, versionado, en DSL |
| Detectar fraude | `PolicyEvaluator` en `PreAgent` | Bloqueable, sin afectar agent |
| Enriquecer contexto | Tool + working memory | Trazable en cada step |
| Custom logging | OpenTelemetry activity | No duplicar responsabilidades |
| Auth/session check | `PreAgent` Policy | Centralizado, no ad-hoc |

> **Middleware de negocio extensible = v3+ roadmap**, solo si el sistema lo requiere.
> En v1/v2 añadir middleware antes de validarlo con casos reales
> introduciría complejidad sin beneficio medible.

---

## 🛡 Security Architecture

Ver: `.agent/workflows/security-rules.md`

### Invariantes

1. TenantId — siempre del JWT, nunca del body/route
2. TenantId — primer campo en TODOS los índices compuestos MongoDB
3. `tool_execution_logs` — WORM (MongoDB user: insert + find only)
4. Policy Engine — evalúa en PreResponse aunque la ejecución sea exitosa
5. Prompt injection → `ThinkDecision.Checkpoint` inmediato
6. TenantContextAccessor — siempre Scoped
7. WorkingMemory en Redis — expiración automática (TTL = duración máxima de ejecución)
8. Distributed lock por `executionId` — evita procesamiento duplicado

---

## 🌍 Deployment Model

```
Self-Hosted First · Single Region · Multi-Node

┌─────────────────────────────────────────┐
│  Load Balancer (Nginx / Traefik)        │
├────────────┬─────────────┬──────────────┤
│  API Nodes │  Worker     │  Designer    │
│  (N)       │  Nodes (N)  │  Backend (N) │
│            │  (stateless │              │
│            │   executors)│              │
├────────────┴─────────────┴──────────────┤
│  Redis Cluster (hot state + locks)      │
├─────────────────────────────────────────┤
│  MongoDB Replica Set (source of truth)  │
├─────────────────────────────────────────┤
│  Qdrant (vector — opcional)             │
└─────────────────────────────────────────┘

Multi-region: arquitecturalmente preparado, no activo-activo en MVP
SaaS: ready arquitecturalmente, sin hard-dependency en Fase 1
```

---

| Subsistema | Proyecto | Estado |
|---|---|---|
| Domain Layer | `AgentFlow.Domain` | ✅ Fase 1 |
| Application Interfaces | `AgentFlow.Application` | ✅ Fase 1 |
| Runtime Engine | `AgentFlow.Runtime` | ✅ Fase 1 |
| Security | `AgentFlow.Security` | ✅ Fase 1 |
| MongoDB Repos | `AgentFlow.Persistence.Mongo` | ✅ Fase 1 |
| Observability | `AgentFlow.Observability` | ✅ Fase 1 |
| REST API | `AgentFlow.Api` | ✅ Fase 1 |
| **Abstractions (NuGet pkg)** | `AgentFlow.Abstractions` | **✅ Fase 2** |
| **DSL Engine** | `AgentFlow.DSL` | **✅ Fase 2** |
| **Policy Engine** | `AgentFlow.Policy` | **✅ Fase 2** |
| **Prompt Engine** | `AgentFlow.Prompting` | **✅ Fase 2** |
| **Evaluation Engine** | `AgentFlow.Evaluation` | **✅ Fase 2** |
| **Model Routing** | `AgentFlow.ModelRouting` | **✅ Fase 2** |
| **Redis Memory + Locks** | `AgentFlow.Caching.Redis` | **✅ Fase 2** |
| **Event Transport** | `AgentFlow.Events` | **✅ Fase 2** |
| **Worker Service** | `AgentFlow.Worker` | ✅ Fase 2 |
| **Test Runner** | `AgentFlow.TestRunner` | ✅ Fase 2 |
| **Checkpoint Management** | `AgentFlow.Api` / `AgentFlow.Core.Engine` | ✅ Fase 2 |
| Experimentation Layer | `AgentFlow.Evaluation` | ✅ Fase 3 |
| Extension Loader | `AgentFlow.Extensions` | 🚧 Fase 3 |
| Designer Backend | `AgentFlow.Designer.Backend` | 🚧 Fase 3 |
| React Designer | `frontend/designer` | ✅ Fase 3 (MVP) |
| Sandbox UI | `frontend/sandbox` | 🚧 Fase 3 |
| Dashboard | `frontend/dashboard` | ✅ Fase 3 (MVP) |

> **Build status**: `dotnet build AgentFlow.sln` — 0 errors, 0 warnings ✅
