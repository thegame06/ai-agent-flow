# AgentFlow Integration Playbook

## 1) Contexto del proyecto (estado actual)

AgentFlow es una plataforma **enterprise** para ejecutar agentes de IA con control operativo:
- Backend en .NET (runtime, policies, evaluation, routing, API).
- Frontend React para operación/configuración.
- Gobernanza: políticas por checkpoints, HITL, auditoría, segmentación.
- Extensibilidad por ToolSDK + integración progresiva con **MAF** y **MCP**.

### Objetivo de negocio
Vender instancias de la solución a clientes (modelo B2B), con:
- multi-tenant,
- seguridad/compliance fuerte,
- capacidad de personalización por cliente,
- operación auditable de punta a punta.

### Problema actual
Aunque el pipeline base existe, falta estandarizar un camino de integración y operación para lograr un flujo “agente se encarga de todo” de forma repetible y demostrable por tenant/caso de uso.

---

## 2) Principios de implementación (no negociables)

1. **El runtime manda**: el LLM no controla el loop.
2. **Política transversal**: toda tool pasa por policy checkpoints.
3. **Tenant isolation**: datos y secretos siempre aislados por tenant.
4. **Audit-first**: toda decisión e invocación crítica deja evidencia.
5. **Idempotencia y resiliencia**: retries acotados + timeouts + fallback.
6. **Contrato sobre improvisación**: cada integración debe tener schema de input/output.

---

## 3) Marco técnico para MAF + MCP + ToolSDK

## 3.1 Arquitectura objetivo
- **SK/MAF coexistiendo** por fases (feature flags/champion-challenger).
- **MCP como estándar de herramientas/contexto** para ambos motores.
- **ToolSDK** como ruta oficial para construir conectores/integraciones.

## 3.2 Contratos mínimos de integración
Cada tool/integración debe declarar:
- `toolId`, `version`, `owner`, `riskLevel`.
- `inputSchema` y `outputSchema` versionados.
- `timeouts`, `retryPolicy`, `circuitBreaker`.
- `requiredPolicies` (ej. PII redaction, allowlist externa).
- `auditFields` obligatorios (tenantId, executionId, correlationId, outcome).

## 3.3 Gobernanza MCP
Antes de invocar un servidor MCP:
1. Validar autorización por tenant/policy.
2. Aplicar data classification (qué puede salir/entrar).
3. Ejecutar con timeout y límites de payload.
4. Registrar auditoría de request/response (masked si aplica PII).
5. Evaluar `PostTool` para bloquear/escalar si corresponde.

---

## 4) Plan de ejecución (pasos a seguir)

## Fase A — Golden Path E2E (1 sprint)
Objetivo: demostrar un flujo completo, confiable y auditable.

### Entregables
- Caso vertical único (ejemplo: soporte/riesgo/loan-status).
- Agente manager + al menos 1 especialista (A2A).
- 1 tool crítica por MCP (o puente equivalente) con políticas activas.
- Ejecución completa con resultado trazable en UI/API.

### Criterio de éxito
- 10/10 ejecuciones consecutivas exitosas en entorno de prueba.
- Sin bypass de policies.
- Evidencia auditable completa por ejecución.

---

## Fase B — Tool Integration Factory (1 sprint)
Objetivo: hacer repetible la creación de nuevas integraciones.

### Entregables
- Plantilla/scaffold para nuevo conector (REST/SQL/MCP).
- Checklist estándar de seguridad/compliance por conector.
- Guía de pruebas mínimas (unit + integration + contract).
- Definición de “Done” para aprobar conectores.

### Definition of Done (DoD) de una Tool
- [ ] Contratos input/output versionados.
- [ ] Timeouts/retries/circuit-breaker definidos.
- [ ] Logs + auditoría + masking de datos sensibles.
- [ ] Cobertura mínima de pruebas acordada.
- [ ] Validación de policy checkpoints (PreTool/PostTool).
- [ ] Documentación de operación y límites.

---

## Fase C — Operación por tenant (1 sprint)
Objetivo: que onboarding y operación comercial sean repetibles.

### Entregables
- Checklist de readiness por tenant:
  - perfiles AI/tokens,
  - gateway/canales,
  - tools habilitadas,
  - policy sets,
  - prompts,
  - límites de costo/latencia.
- Smoke test automático de tenant onboarding.
- Tablero operativo con “Execution Verdict”:
  - `SUCCESS`, `PARTIAL`, `BLOCKED_BY_POLICY`, `NEEDS_HITL`, `TOOL_FAILURE`.

---

## 5) Métricas clave (producto + operación)

1. **Autonomía útil**: % tareas completadas sin intervención humana.
2. **Seguridad operacional**: % bloqueos/escalaciones por policy (esperado y auditado).
3. **Confiabilidad de tools**: success rate por conector + latencia P95.
4. **Costo unitario**: costo por ejecución y por tenant.
5. **Tiempo de onboarding**: horas/días para activar un tenant con flujo funcional.

---

## 6) Riesgos principales y mitigación

### Riesgo 1: Drift entre motores (SK vs MAF)
- Mitigación: pruebas de contrato del brain + golden prompts + champion/challenger.

### Riesgo 2: Superficie de ataque vía MCP
- Mitigación: allowlist por tenant, credenciales rotadas, policy gates obligatorias, masking.

### Riesgo 3: Conectores sin estándares
- Mitigación: Tool Factory + DoD obligatorio + revisión técnica previa a release.

### Riesgo 4: “Pipeline existe pero no cierra negocio”
- Mitigación: foco en 1 golden flow comercialmente relevante y KPI de cierre E2E.

---

## 7) Backlog inmediato (siguiente semana)

1. Seleccionar y congelar el **golden flow** (input, tools, output esperado).
2. Implementar/ajustar conector MCP crítico del flujo.
3. Definir y exponer `Execution Verdict` en API/UI.
4. Crear plantilla de nuevo conector en ToolSDK.
5. Automatizar smoke test de onboarding por tenant.

---

## 8) Resultado esperado

Al completar este playbook, AgentFlow pasa de “plataforma técnicamente robusta” a “producto operable y vendible con evidencia”, con una ruta clara para escalar integraciones sin perder compliance.
