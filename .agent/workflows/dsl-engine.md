---
description: Reglas de diseño para el DSL composable — cómo definir, parsear y validar agent definitions
---

# DSL Engine — Reglas de Diseño

## Principio Central

> **El DSL es la fuente única de verdad del comportamiento del agente.**
> Todo lo que el agente puede hacer, debe estar declarado en el DSL.
> Nada de comportamiento vive en código de aplicación.

El DSL define:
- **Qué** puede hacer el agente (tools autorizados)
- **Cómo** debe hacerlo (flows, steps, guardrails)
- **Con qué LLM** (model routing strategy)
- **Con qué prompt** (promptProfile ID versionado)
- **Bajo qué reglas** (policySetId versionado)
- **Cómo evaluarse** (evaluation config)
- **Cómo probarse** (testSuite)

---

## Estructura Obligatoria (todos los campos)

```json
{
  "agent": {
    "key": "snake_case — único por tenant",
    "version": "semver — 1.0.0",
    "role": "descripción del rol — seed del system prompt",

    "runtime": {
      "mode": "deterministic | hybrid | autonomous",
      "temperature": 0.0,
      "allowOverride": false,
      "maxIterations": 6,
      "maxExecutionSeconds": 120
    },

    "modelRouting": {
      "strategy": "static | task-based | policy-based | fallback-chain",
      "default": "model-id",
      "fallbackChain": ["model-primary", "model-secondary", "model-local"]
    },

    "promptProfile": "prompt-profile-id@version",
    "policies": {
      "policySetId": "policy-set-id@version",
      "maxSteps": 6,
      "requireToolValidation": true,
      "allowParallelTools": false
    },

    "flows": [...],
    "authorizedTools": [...],
    "evaluation": {...},
    "experiment": {
      "featureFlags": ["new-ui", "beta-model"],
      "shadowAgentId": "candidate-version-id",
      "canaryRolloutWeight": 0.10
    },
    "testSuite": {...}
  }
}
```

---

## Runtime Modes

| Modo | El LLM decide | El DSL define | Cuándo usar |
|---|---|---|---|
| `deterministic` | Solo redacción final | Todos los pasos | Flujos regulados (fintech, legal, medical) |
| `hybrid` | Qué flow + inputs de tools | Intents y flows disponibles | Mayoría de casos enterprise |
| `autonomous` | Qué tools usar y cómo | Límites (maxSteps, tools disponibles) | Research, exploración, no producción financiera |

### Regla de temperatura por modo

```
deterministic → temperature: 0.0 (obligatorio, allowOverride: false)
hybrid        → temperature: 0.1-0.4 (recomendado)
autonomous    → temperature: configurable, seed requerido
```

---

## Agent Lifecycle — Estados del DSL

```
Draft → Validating → TestPassed → Published → Deprecated → Archived

Draft:       editable, no ejecutable en producción
Validating:  en ejecución del test suite automático
TestPassed:  test suite OK, listo para review humano
Published:   activo en producción, INMUTABLE
Deprecated:  nueva versión publicada, acepta tráfico hasta que se drene
Archived:    no ejecutable, auditable
```

**Regla CRÍTICA**: Un `AgentDefinition` en estado `Published` es **inmutable**.
Cualquier cambio requiere nueva version semver → nuevo documento → lifecycle completo.

---

## Versionado Semántico

```
MAJOR.MINOR.PATCH

MAJOR: cambio en flows, tools autorizados, modo de runtime
MINOR: cambio en evaluation config, experiment flags, políticas
PATCH: corrección de typo en prompts, ajuste de temperatura
```

**Regla**: El sistema rechaza downgrade de versión (`2.0.0 → 1.9.0` es inválido).

---

## Flows — Reglas de Diseño

```
Cada flow tiene:
- name: string único en el agente
- trigger: { type: "intent" | "event" | "scheduled" | "manual", value: "..." }
- steps: lista ordenada de tools
- guardrails: comportamiento ante fallos

Orden de evaluación de flows:
1. Por trigger más específico primero
2. Flow con trigger type "intent" y `value: "*"` es el catch-all (último)

Anti-patterns:
❌ Flows con ciclos (A llama tool que triggerea B que llama A)
❌ More than 1 catch-all flow
❌ Steps sin herramienta válida registrada
❌ `required: true` en tool con RiskLevel.Critical sin policyGroup asignado
```

---

## Tools en el DSL

```json
{
  "authorizedTools": ["ToolA", "ToolB"],
  "flows": [
    {
      "steps": [
        {
          "tool": "ToolA",
          "required": true,
          "policyGroup": "pii-access",
          "timeout": 5000,
          "retries": 1,
          "onError": "halt | skip | fallback",
          "guardrails": {
             "requireHumanApproval": true,
             "approvalThreshold": "high_score | always"
          }
        }
      ]
    }
  ]
}
```

**Regla**: Solo tools listados en `authorizedTools` pueden aparecer en `flows.steps`.
El Runtime rechaza ejecución de cualquier tool no declarado, aunque esté registrado globalmente.

---

## Referencias Versionadas

| Campo DSL | A qué apunta |
|---|---|
| `promptProfile` | `PromptProfile.ProfileId` + versión publicada |
| `policies.policySetId` | `PolicySet.PolicySetId` + versión publicada |
| `modelRouting.default` | `IModelRegistry` — model disponible |
| `authorizedTools[]` | `ToolDefinition.Name` en estado Active |
| `experiment.featureFlags[]` | `FeatureFlagSet` activo para el tenant |

**Regla**: Si cualquiera de estas referencias no resuelve en el momento de publicar el agente → publicación rechazada.

---

## Validaciones Design-Time (Todas Obligatorias)

```
1. key único por tenant (case-insensitive, solo snake_case)
2. version > versión actual publicada (no downgrade)
3. authorizedTools ⊇ tools en flows.steps (no hay paso sin autorización)
4. todos authorizedTools existen en ToolRegistry como Active
5. policySetId apunta a PolicySet publicado
6. promptProfile apunta a PromptProfile publicado
7. modelRouting.fallbackChain — todos los models existen en IModelRegistry
8. Si mode=deterministic → temperature=0.0 (obligatorio)
9. testSuite.testCases.length >= 1 (para publicar en producción)
10. Si RiskLevel de algún tool >= High → requiredPermissions declarados
```

---

## Anti-Patterns del DSL (Prohibidos)

```
❌ mode: autonomous para agentes en producción financiera/médica/legal
❌ maxSteps > 20 (necesita justificación documentada + aprobación)
❌ allowParallelTools: true sin análisis de idempotencia de tools
❌ fallbackChain vacío con model cloud (¿qué pasa si cae el proveedor?)
❌ testSuite vacío en agentes de producción
❌ temperature > 0.7 en modo deterministic/hybrid
❌ promptProfile sin versión explícita (siempre anclar a versión)
❌ policySetId sin versión explícita
❌ Lógica de negocio fuera del DSL — no existe campo "middleware" en el DSL
❌ "Interceptors" de business logic — si necesitas interceptar, úsalo como Tool o Policy
❌ "Pre/Post hooks" custom en flows — el Policy Engine ya cubre PreTool/PostTool
```

---

## Principio: El DSL NO tiene Middlewares de Negocio

El DSL declara **qué hace** el agente, no **cómo interceptar** la ejecución.

Si encuentras que necesitas "interceptar" algo antes o después de un tool:
- ✅ Usa una Policy en el checkpoint correspondiente (`PreTool`, `PostTool`)
- ✅ Convierte la lógica en un Tool separado en el flow

Si encuentras que necesitas "enriquecer" el contexto antes de ejecutar:
- ✅ Añade un Tool de enriquecimiento como primer paso del flow
- ✅ Usa Working Memory (`IWorkingMemoryStore`) para datos de sesión

> Middleware de negocio extensible está marcado como **v3+ roadmap**.
> No se implementa hasta que haya un caso probado que Tools + Policies no cubran.
