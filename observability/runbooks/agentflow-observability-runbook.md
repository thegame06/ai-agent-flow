# AgentFlow Observability Runbook

## Estándar operativo
- Contrato único de eventos: `observability/standards/unified-event-schema.md`.
- Tipos canónicos: `session_start`, `session_end`, `handoff`, `tool_call`, `retry`, `timeout`, `deny`, `final_response`.
- Campos obligatorios en todos los eventos: `correlation_id`, `tenant_id`, `agent_id`, `tool_name`, `policy_decision`, `latency_ms`, `cost_estimate`.
- Severidad y error unificados para SRE/forensics:
  - Severidad: `debug|info|warn|error|fatal`.
  - Error: `error.code`, `error.message`, `error.type`, `error.retryable`, `error.stack_hash`, `error.upstream`.

## Señales a leer (dashboards mínimos)
1. **Latencia p50/p95 por segmento** (`agentflow_executions_latency_by_segment_ms`).
2. **Error rate** (`agentflow_executions_outcomes_total{status="failed"}`).
3. **Retries por tool** (`agentflow_execution_retries_total`).
4. **Denials por tenant/política** (`agentflow_execution_denials_total`).
5. **Costo por flujo** (`agentflow_execution_cost_estimate_usd`).
6. **Éxito de ejecución** (`agentflow_executions_outcomes_total`, status=completed/failed).
7. **Costo por 1K tokens y costo por ejecución** (`agentflow_tokens_cost_per_1k_usd`, `agentflow_tokens_cost_per_execution_usd`).
8. **Distribución canary por segmento** (`agentflow_canary_assignments_total`).
9. **Comparación champion/challenger y SK vs MAF** (`agentflow_evaluations_comparisons_total`, `agentflow_executions_outcomes_total{brain=*}`).
10. **Fallos de tools y MCP** (`agentflow_tools_failures_total`, `agentflow_tools_mcp_failures_total`).
11. **Latencia de endpoints de control** (`agentflow_api_endpoint_latency_ms` en `EvaluationsController`, `SegmentRoutingController`, `FeatureFlagsController`).

## Diagnóstico rápido

- **Aumento de retries**:
  - Revisar `agentflow_execution_retries_total` por `tool_name`.
  - Correlacionar con `timeouts` y `mcp failures`.
- **Pico de denials**:
  - Revisar `agentflow_execution_denials_total` por `tenant_id` y `policy_decision`.
  - Confirmar cambios recientes en reglas de policy.
- **Desbalance canary**:
  - Validar `agentflow_canary_assignments_total` por `segment` y `variant`.
  - Revisar cambios recientes en reglas de `SegmentRoutingController` y flags.
- **Degradación por segmento**:
  - Correlacionar `p95` por `segment` con `error rate` (`status=failed`).
  - Si sólo afecta challenger, reducir o detener canary.
- **Incremento de costo/token**:
  - Comparar `brain=sk` vs `brain=maf`.
  - Revisar crecimiento de `agentflow_tokens_used_total` sin mejora de éxito.

## Acciones de mitigación

1. **Rollback rápido canary**: poner peso canary en 0% o deshabilitar regla de segmento.
2. **Aislamiento por segmento**: enrutar segmento degradado al champion estable.
3. **Control de costos**: limitar rollout de brain con costo alto (SK/MAF), reducir max iterations.
4. **MCP degradado**: activar fallback sin tool o flujo de human-review para requests de riesgo.
5. **SRE escalation**: si alerta critical > 15m, abrir incidente y congelar cambios de routing/flags.

## Retención y auditoría regulatoria
- Eventos crudos: retención mínima **400 días**.
- Agregados de métricas: retención mínima **24 meses**.
- Auditoría en WORM para eventos `deny`, `timeout`, `final_response` y evidencias de policy.
- Activar **legal hold** para incidentes abiertos o requerimientos regulatorios.
- Verificar cadena de custodia (hash por lote diario + sello temporal).

## Validación post-mitigación
- 15 minutos sin alertas activas.
- Error rate de segmento afectado vuelve a baseline.
- p95 por segmento debajo del umbral definido.
- Costo por 1K tokens vuelve al rango esperado.
