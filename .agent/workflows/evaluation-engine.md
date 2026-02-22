---
description: Reglas para implementar el Evaluation Engine — scores, detección de alucinaciones y human review
---

# Evaluation Engine — Reglas de Implementación

## Principio

> La evaluación convierte cada ejecución en una señal de calidad medible.
> Es asíncrona (no bloquea el response). Alimenta el re-calibration loop.
> El mismo LLM que ejecutó el agente NO puede evaluarse a sí mismo.

---

## Los 4 Scores

### 1. QualityScore (0.0 - 1.0)

**Quién**: LLM juez externo (diferente modelo, diferente llamada)
**Cuándo**: Post-ejecución, asíncrono

```
Prompt al LLM juez:
  Input del usuario: {input}
  Respuesta del agente: {response}
  Context disponible: {toolOutputsSummary}

  Evalúa de 0.0 a 1.0:
  - Relevancia (¿responde lo que se preguntó?)
  - Completitud (¿responde completamente?)
  - Precisión (¿los datos son correctos vs tool outputs?)
  - Tono (¿apropiado para el contexto del tenant?)

  Responde SOLO: { "score": 0.0, "rationale": "..." }
```

**Regla**: Si `HallucinationRisk >= High` → `QualityScore = 0` automático (sin llamar al LLM juez).

---

### 2. PolicyComplianceScore (0.0 - 1.0)

**Quién**: Determinístico — sin LLM
**Cuándo**: Post-ejecución, síncrono

```
Checks evaluados:
✓ steps ejecutados <= maxSteps del DSL
✓ tools usados ⊆ authorizedTools del DSL
✓ ningún tool superó MaxCallsPerExecution
✓ ejecución completó en <= maxExecutionSeconds
✓ flow ejecutado matchea el intent declarado
✓ todos los required=true steps fueron ejecutados
✓ no se ejecutaron tools con RiskLevel > permitido para el usuario

Score = checksPasados / totalChecks
```

**Si < 0.8 → human review automatic.**

---

### 3. HallucinationRisk (None | Low | Medium | High | Critical)

**Quién**: Análisis estructural — sin LLM para detección básica
**Cuándo**: Post-ejecución

#### Algoritmo

```
1. Extraer todos los valores numéricos de la respuesta final
   → [5000, 2026, 3]

2. Extraer todos los valores numéricos de todos tool outputs
   → [3000, 2026, 1]

3. Por cada valor en respuesta NOT IN tool outputs:
   → candidato a alucinación

4. Calcular severidad:
   - >50% de valores numéricos son alucinados → Critical
   - 25-50%                                  → High
   - 10-25%                                  → Medium
   - <10%                                    → Low
   - 0%                                      → None

5. También detectar: entidades nombradas, fechas, montos con símbolo de moneda
```

#### Caso Crítico Estándar

```
Tool GetLoanStatus → { "approvedAmount": 3000, "status": "Approved" }
Respuesta agente → "Tu préstamo de $5,000 fue aprobado"
                              ↑ $5,000 ≠ $3,000
→ HallucinationRisk = Critical
→ QualityScore forced to 0
→ Policy: Block or Escalate (según config)
→ Human review: IMMEDIATE
```

---

### 4. ToolUsageAccuracy (0.0 - 1.0 | null)

**Quién**: Determinístico — comparación vs test case
**Cuándo**: Solo cuando hay un test case aplicable

```
Si hay test case activo con expectedTools:
  actualTools   = tools ejecutados en orden
  expectedTools = tools del test case

  accuracy = |intersection(actual, expected)| / max(|actual|, |expected|)

  También evalúa ORDEN como bonus:
  orderBonus = (steps en orden correcto / totalSteps) * 0.1

Si no hay test case → null (no evaluable sin ground truth)
```

---

## Cuándo Disparar Human Review

```
QualityScore < evaluation.requireHumanReviewOnScoreBelow   → queue para review
HallucinationRisk >= High                                  → IMMEDIATE review
PolicyComplianceScore < 0.8                                → queue para review
Policy con action=Escalation fue activada                  → IMMEDIATE review
```

---

## Evaluation en Policy Mode

La evaluación puede configurarse en dos modos:

| Modo | Descripción |
|---|---|
| `observing` | Calcula scores, registra. No bloquea. Alimenta métricas. |
| `blocking` | Si QualityScore < threshold → bloquea respuesta al usuario |

Default: `observing`. Cambiar a `blocking` requiere validación en test suite primero.

---

## Experimentation + Evaluation

### Implemented Shadow Evaluation (v3.1)

The system now automatically performs shadow evaluations. When a primary agent execution completes, the `EvaluationBackgroundWorker` checks for a configured `ShadowAgentId`. If present, it triggers a parallel execution of the shadow agent with the same input, marking results as `IsShadowEvaluation: true`.

### Implemented Segment-Based Rules (v3.1)

Evaluation thresholds and policies now support `TargetSegments`. The `CompositePolicyEngine` filters rules based on the user's `UserSegments` found in the security context.

---

## Re-Calibration Loop (Aprendizaje Estructural)

```
Ejecución con score bajo o corrección humana
          │
          ▼
Correction guardada (append-only):
{
  "executionId": "...",
  "input": "...",
  "expectedBehavior": "...",
  "actualBehavior": "...",
  "correction": "...",
  "approvedBy": "user_abc",
  "approvedAt": "..."
}
          │
          ▼
Opciones de calibración (SOLO con aprobación humana):
  a) Ajustar system prompt → nueva versión de PromptProfile
  b) Agregar test case al TestSuite del agente
  c) Ajustar policies en el PolicySet
  d) Cambiar tool constraint en el DSL

          │
          ▼
Nueva versión del agente (semver bump OBLIGATORIO)
          │
          ▼
Test suite ejecutado automáticamente
          │
          ▼
Aprobación humana para deploy a producción
```

### NO Permitido en Re-Calibration

```
❌ Fine-tuning automático sin control
❌ Modificar AgentDefinition Published directamente (debe crear nueva versión)
❌ Aplicar corrección sin aprobación humana registrada
❌ Re-calibration sin test suite ejecutado
❌ Deploy automático post-calibración (siempre requiere aprobación)
```

---

## Interfaces a Implementar (Fase 2)

```csharp
// En: AgentFlow.Abstractions

public interface IAgentEvaluator
{
    Task<EvaluationResult> EvaluateAsync(
        AgentExecution execution,
        EvaluationConfig config,
        CancellationToken ct = default);
}

public sealed record EvaluationResult
{
    public required string ExecutionId { get; init; }
    public required string TenantId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentVersion { get; init; }
    public double QualityScore { get; init; }
    public double PolicyComplianceScore { get; init; }
    public HallucinationRisk HallucinationRisk { get; init; }
    public double? ToolUsageAccuracy { get; init; }
    public bool RequiresHumanReview { get; init; }
    public HumanReviewPriority ReviewPriority { get; init; }
    public string EvaluatorId { get; init; } = string.Empty;
    public string EvaluationRationale { get; init; } = string.Empty;
    public IReadOnlyList<EvaluationViolation> Violations { get; init; } = [];
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsShadowEvaluation { get; init; }
}

public enum HallucinationRisk { None, Low, Medium, High, Critical }
public enum HumanReviewPriority { Low, Medium, High, Immediate }
```

---

## MongoDB Collections

```javascript
// Append-only — igual que tool_execution_logs
db.evaluation_results.createIndex({ tenantId: 1, evaluatedAt: -1 })
db.evaluation_results.createIndex({ tenantId: 1, agentId: 1, evaluatedAt: -1 })
db.evaluation_results.createIndex({ tenantId: 1, requiresHumanReview: 1, evaluatedAt: -1 })

db.calibration_corrections.createIndex({ tenantId: 1, agentId: 1, approvedAt: -1 })
```
