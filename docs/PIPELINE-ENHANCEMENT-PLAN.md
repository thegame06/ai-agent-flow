# Pipeline Enhancement Plan (Post-Functional Baseline)

## Objetivo
Definir mejoras de pipeline que aumenten calidad y confiabilidad (query rewrite, rerank, self-check) **sin frenar entregas**.

> Regla: primero baseline funcional estable (E2E real), luego estas mejoras por feature flags.

---

## Estado actual (resumen)
Ya existe loop controlado con límites (max iterations, timeout, retries, circuit breakers), por lo que no hay riesgo de ejecución infinita.

Lo pendiente es hacer explícitos algunos bloques del flujo tipo Agentic RAG + MCP:
1. Query rewrite formal
2. Rerank de resultados formal
3. Self-check final formal antes de responder

---

## Decisión de producto
Sí conviene implementarlo, pero en fases:

### Fase 1 (recomendada, bajo riesgo)
- `enableQueryRewrite`
- `enableAnswerSelfCheck`

### Fase 2 (selectiva)
- `enableRerank` para flujos con retrieval/MCP múltiple y alto impacto.

### Fase 3 (opcional)
- Pipeline completo 1:1 (rewrite + rerank + self-check en todos los casos).

---

## Feature Flags propuestas
- `Pipeline:EnableQueryRewrite` (bool)
- `Pipeline:EnableAnswerSelfCheck` (bool)
- `Pipeline:EnableRerank` (bool)
- `Pipeline:RewriteMinConfidenceThreshold` (decimal)
- `Pipeline:SelfCheckMinScore` (decimal)
- `Pipeline:RerankTopK` (int)

Activación por tenant/policy profile.

---

## Diseño funcional (alto nivel)

### 1) Query Rewrite
**Cuándo:** si query es ambigua, incompleta o de baja confianza.

**Entrada:** user query + contexto de sesión.
**Salida:** rewritten query + rationale + confidence.

**Fallback:** si falla rewrite, usar query original.

### 2) Rerank
**Cuándo:** cuando hay múltiples resultados de retrieval/tools/MCP.

**Entrada:** lista de candidatos.
**Salida:** top-k ordenados + score.

**Fallback:** orden original si rerank falla.

### 3) Answer Self-Check
**Cuándo:** justo antes de responder.

**Entrada:** draft answer + query + evidencias.
**Salida:** `PASS | FAIL | PARTIAL`, score, motivos.

**Comportamiento:**
- PASS -> responde
- FAIL/PARTIAL -> 1 reintento controlado o request de aclaración

---

## Guardrails
- Nunca más de 1 reintento de self-check por ejecución (configurable)
- Presupuesto de tokens/latencia por bloque
- Auditoría obligatoria por etapa (decision + input/output resumido)
- No bloquear respuesta por módulos opcionales (degradación elegante)

---

## KPI de éxito
- +% de `SUCCESS` sin HITL
- -% de respuestas incorrectas/no relevantes
- +consistencia en corridas repetidas
- impacto controlado en latencia p95 y costo por ejecución

---

## Riesgos y mitigación

### Riesgo: latencia/costo suben
Mitigación: flags por tenant, thresholds, early-exit, top-k corto.

### Riesgo: sobre-complejidad
Mitigación: Fase 1 mínima y medición antes de escalar.

### Riesgo: comportamiento inestable
Mitigación: contract tests por etapa + champion/challenger.

---

## Plan de implementación sugerido

### Iteración A (1 semana)
- Contratos y flags de rewrite/self-check
- Instrumentación y logs de etapa
- Tests de contrato

### Iteración B (1 semana)
- Activación en tenant interno
- Ajuste de umbrales
- Reporte de KPI inicial

### Iteración C (1 semana)
- Rerank en flujos seleccionados
- Evaluación de costo/beneficio
- Decisión de expansión o mantener selectivo

---

## Definition of Done
Una etapa se considera DONE cuando:
- Tiene flag de activación por tenant
- Tiene fallback seguro
- Tiene auditoría y métricas
- Tiene tests de contrato y pruebas E2E
- No degrada SLO fuera del umbral acordado

---

## Nota operativa
Este plan está diferido intencionalmente para ejecutarse **después** de cerrar corridas E2E funcionales reales (10/10) del flujo principal.
