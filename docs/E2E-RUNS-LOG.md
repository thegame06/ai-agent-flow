# E2E Runs Log (Canales Reales)

Registro oficial de corridas E2E para validar "DONE real" por canal.

## Campos obligatorios por corrida
- `runId`
- `dateTime`
- `channel`
- `tenantId`
- `inputSummary`
- `executionId`
- `channelMessageIdIn`
- `channelMessageIdOut`
- `latencyMs`
- `verdict` (`SUCCESS|PARTIAL|BLOCKED_BY_POLICY|NEEDS_HITL|TOOL_FAILURE`)
- `notes`

## Plantilla de corrida

```yaml
runId: E2E-WA-001
dateTime: 2026-02-26T08:45:00-06:00
channel: whatsapp-qr
tenantId: tenant-1
inputSummary: "usuario pregunta saldo del préstamo"
executionId: exec-xxxxx
channelMessageIdIn: wamid.in.xxxxx
channelMessageIdOut: wamid.out.xxxxx
latencyMs: 1820
verdict: SUCCESS
notes: "flujo completo inbound->agent->outbound"
```

---

## Corridas

| Run | DateTime | Channel | ExecutionId | MsgId In | MsgId Out | Latency (ms) | Verdict | Notes |
|---|---|---|---|---|---|---:|---|---|
| E2E-WA-001 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | primera corrida |
| E2E-WA-002 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-003 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-004 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-005 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-006 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-007 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-008 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-009 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | |
| E2E-WA-010 | _pending_ | whatsapp-qr | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ | cierre 10/10 |

---

## Criterio de aceptación
Se considera canal en estado **DONE real** cuando:
- 10/10 corridas con `verdict=SUCCESS`
- evidencia completa de IDs y latencia en todas
- sin bypass de policy
- con runbook operativo actualizado
