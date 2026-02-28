# Roadmap + Contrato Técnico v1 (Manager + Subagentes)

## Objetivo
Implementar arquitectura multi-agente configurable donde:
- el **Manager** controla canal, sesión e intención,
- los **Subagentes** ejecutan trabajo especializado,
- y las integraciones externas entran por **MCP** (plug-and-play).

## Modelo operativo
- **Manager Agent (con canal):** único punto de entrada/salida con usuario final.
- **Subagentes (sin canal externo):** workers internos invocados por Manager.
- **Modo test de subagentes:** permitido desde UI, aislado de sesiones productivas y auditado.

## Reglas duras
1. Solo Manager responde por canales externos.
2. Subagentes no responden directo al cliente en producción.
3. Todo handoff requiere contrato de entrada/salida + `correlationId`.
4. MCP es conectividad; reglas críticas quedan en backend/manager.
5. Un solo receptor por canal para evitar colisiones multi-bot.

---

## Roadmap

### Fase 0 - Fundaciones
- Contrato técnico v1 aprobado
- Session model + sticky routing + ownership
- Matriz de permisos por tool/tenant
- Observabilidad base (logs, correlationId, métricas)

### Fase 1 - Manager y routing
- Webhook único por canal
- Session store (`owner_agent`, TTL, lock)
- Routing por intención + handoff
- Cero respuestas duplicadas en pruebas de concurrencia

### Fase 2 - MCP plug-and-play
- Registro de MCP servers por tenant
- Descubrimiento de tools
- Binding `contract_action -> tool` por proveedor
- Drive + Sheets como conectores iniciales

### Fase 3 - Flujos E2E
- Renta: disponibilidad, cotización, reserva, comprobante, evidencia
- Cobranza 9:00 AM: archivo `yyyy_mm_dd_collection`, envío, estado, retry
- Idempotencia validada

### Fase 4 - Gobernanza
- IAM formal por scope
- Auditoría completa de tool calls
- Rate limit y DLQ/replay

---

## Contrato Técnico v1

### Entidades mínimas

#### Session
- `session_id`
- `tenant_id`
- `channel`
- `user_key`
- `owner_agent`
- `state` (`active|handoff|closed|expired`)
- `expires_at`
- `lock_version`
- `correlation_id`

#### ToolBinding
- `tenant_id`
- `provider`
- `mcp_server_id`
- `tool_name`
- `contract_action`
- `enabled`

#### JobRun
- `job_id`
- `run_key`
- `status`
- `stats`

### Acciones unificadas
- `records.read`
- `records.upsert`
- `records.search`
- `files.upload`
- `files.get`
- `tickets.create`
- `tickets.update`
- `contacts.lookup`
- `messages.render_template`

### Contrato de handoff
**Manager -> Subagente**
- `tenant_id`
- `session_id`
- `correlation_id`
- `target_agent`
- `intent`
- `payload`
- `policy_context`

**Subagente -> Manager**
- `ok`
- `result`
- `error_code`
- `retryable`
- `state_patch`
- `tool_calls[]`

### Criterios de aceptación
- Cero colisiones multi-agente
- Cambio de proveedor por configuración
- Jobs sin duplicidad
- Trazabilidad completa por `correlation_id`
