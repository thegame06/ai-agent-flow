# AgentFlow Unified Event Schema (v1)

## Objetivo
Establecer un contrato único de eventos para trazabilidad operativa, SRE, forensics y auditoría regulatoria.

## Eventos canónicos (event_type)
Todos los productores deben emitir **uno** de estos tipos:

- `session_start`
- `session_end`
- `handoff`
- `tool_call`
- `retry`
- `timeout`
- `deny`
- `final_response`

## Campos obligatorios (en todos los eventos)
Los siguientes campos son **required** en el contrato canónico:

- `correlation_id` (string)
- `tenant_id` (string)
- `agent_id` (string)
- `tool_name` (string, usar `"none"` si no aplica)
- `policy_decision` (`allow|deny|shadow|escalate|not_applicable`)
- `latency_ms` (number >= 0)
- `cost_estimate` (number >= 0, USD)

Además:

- `event_id` (uuid)
- `event_type` (enum canónico)
- `timestamp_utc` (RFC3339 UTC)
- `severity` (`debug|info|warn|error|fatal`)
- `service` (e.g., `AgentFlow.Api`)
- `environment` (`dev|staging|prod`)

## Severidad unificada
Mapeo único para logs, traces y eventos de auditoría:

- `debug`: diagnóstico detallado.
- `info`: operación esperada.
- `warn`: degradación recuperable (retry, latencia alta).
- `error`: falla de operación no recuperada.
- `fatal`: pérdida de disponibilidad o integridad del flujo.

## Campos de error unificados
Para facilitar SRE y análisis forense, cuando exista error se debe completar:

- `error.code` (string normalizado, e.g. `tool_timeout`, `policy_denied`)
- `error.message` (string breve, redacted)
- `error.type` (enum: `validation|timeout|dependency|policy|internal|security`)
- `error.retryable` (bool)
- `error.stack_hash` (string opcional)
- `error.upstream` (string opcional, e.g. `mcp`, `openai`, `qdrant`)

## Payload de referencia (JSON)
```json
{
  "event_id": "c3e9f88c-3c6f-4f31-b3d0-661c17f92b2f",
  "event_type": "tool_call",
  "timestamp_utc": "2026-04-13T10:31:05Z",
  "severity": "warn",
  "service": "AgentFlow.Core.Engine",
  "environment": "prod",

  "correlation_id": "corr_7f3d6f9d",
  "tenant_id": "tenant_enterprise_a",
  "agent_id": "manager-agent",
  "tool_name": "credit_bureau_query",
  "policy_decision": "allow",
  "latency_ms": 1240,
  "cost_estimate": 0.0124,

  "error": {
    "code": "tool_timeout",
    "message": "tool invocation exceeded timeout",
    "type": "timeout",
    "retryable": true,
    "stack_hash": "sha256:4fdd...",
    "upstream": "mcp"
  }
}
```

## Dashboards mínimos obligatorios
Paneles mínimos para go-live:

1. Latencia `p50` y `p95` por flujo (`correlation_id`) y por `event_type`.
2. Error rate global y por `tenant_id`.
3. Retries (`event_type=retry`) por `tool_name`.
4. Denials (`event_type=deny` o `policy_decision=deny`) por política/tenant.
5. Costo por flujo (sumatoria `cost_estimate` por `correlation_id`).

## Retención y WORM (regulated)
Para sectores regulados:

- Retención mínima de eventos crudos: **400 días**.
- Retención de agregados de métricas: **24 meses**.
- Almacenamiento WORM (Write Once Read Many) para eventos de auditoría y denials.
- Bloqueo de borrado temprano (legal hold) para incidentes abiertos.
- Cadena de custodia: hash de lote diario + timestamping externo.

## Implementación recomendada en AgentFlow
- Emitir `agentflow.execution.events.total` con labels:
  - `event_type`, `severity`, `tenant_id`, `agent_id`, `tool_name`, `policy_decision`.
- Emitir histogram `agentflow.execution.event.latency_ms`.
- Emitir counter `agentflow.execution.denials.total`.
- Emitir counter `agentflow.execution.retries.total`.
- Emitir histogram `agentflow.execution.cost_estimate_usd`.
