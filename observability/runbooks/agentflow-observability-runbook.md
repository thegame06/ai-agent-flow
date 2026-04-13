# AgentFlow Observability Runbook

## Señales a leer

1. **Éxito de ejecución** (`agentflow_executions_outcomes_total`, status=completed/failed)
2. **Latencia p95 por segmento** (`agentflow_executions_latency_by_segment_ms`)
3. **Costo por 1K tokens y costo por ejecución** (`agentflow_tokens_cost_per_1k_usd`, `agentflow_tokens_cost_per_execution_usd`)
4. **Distribución canary por segmento** (`agentflow_canary_assignments_total`)
5. **Comparación champion/challenger y SK vs MAF** (`agentflow_evaluations_comparisons_total`, `agentflow_executions_outcomes_total{brain=*}`)
6. **Fallos de tools y MCP** (`agentflow_tools_failures_total`, `agentflow_tools_mcp_failures_total`)
7. **Latencia de endpoints de control** (`agentflow_api_endpoint_latency_ms` en `EvaluationsController`, `SegmentRoutingController`, `FeatureFlagsController`)

## Diagnóstico rápido

- **Desbalance canary**:
  - Validar `agentflow_canary_assignments_total` por `segment` y `variant`.
  - Revisar cambios recientes en reglas de `SegmentRoutingController` y flags.
- **Degradación por segmento**:
  - Correlacionar `p95` por `segment` con `error rate` (`status=failed`).
  - Si sólo afecta challenger, reducir o detener canary.
- **Incremento de costo/token**:
  - Comparar `brain=sk` vs `brain=maf`.
  - Revisar crecimiento de `agentflow_tokens_used_total` sin mejora de éxito.
- **Fallos MCP/tool**:
  - Revisar tool con mayor `tool_name` en métricas de fallo.
  - Verificar disponibilidad del backend MCP, timeouts y credenciales.

## Acciones de mitigación

1. **Rollback rápido canary**: poner peso canary en 0% o deshabilitar regla de segmento.
2. **Aislamiento por segmento**: enrutar segmento degradado al champion estable.
3. **Control de costos**: limitar rollout de brain con costo alto (SK/MAF), reducir max iterations.
4. **MCP degradado**: activar fallback sin tool o flujo de human-review para requests de riesgo.
5. **SRE escalation**: si alerta critical > 15m, abrir incidente y congelar cambios de routing/flags.

## Validación post-mitigación

- 15 minutos sin alertas activas.
- Error rate de segmento afectado vuelve a baseline.
- p95 por segmento debajo del umbral definido.
- Costo por 1K tokens vuelve al rango esperado.
