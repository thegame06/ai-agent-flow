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
7. **No mocks en features de producto**: para canales e integraciones externas, solo se considera “hecho” con conectividad real en entorno controlado.

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
- Canales/integraciones validadas con proveedores reales (no mocks como criterio de cierre).

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
- [ ] Prueba de integración real contra servicio/proveedor real (entorno sandbox o staging), con evidencia.
- [ ] Prohibido cerrar ticket como “done” si solo existe mock/stub.

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

## 7) Canales e integración real (estado y faltantes)

> Regla operativa: **mock ≠ done**. Esta matriz define qué está realmente listo y qué falta para cerrar producto.

| Canal/Entrada | Estado actual | Evidencia real requerida | Bloqueadores/Faltantes | Prioridad |
|---|---|---|---|---|
| WhatsApp | Parcial (depende de entorno/config) | Mensaje inbound/outbound real + webhook estable + traza auditada | Estandarizar setup por tenant y pruebas de resiliencia | Alta |
| Telegram | Pendiente/variable | Bot real activo + conversación E2E + policy checkpoints verificados | Runbook de onboarding + validación de auth/secret rotation | Media |
| WebChat | Parcial | Sesión real multi-turno + persistencia thread + métricas de latencia | Endurecer manejo de sesión, reconexión y límites por tenant | Alta |
| API (directa) | Más avanzado | Ejecución real desde cliente externo + auditoría completa | Contract tests externos y control de cuotas por tenant | Alta |
| MCP Tools | Parcial (arquitectura definida) | Invocación real a servidor MCP + policy gating + masking evidencia | Gateway MCP productivo, allowlists por tenant, contract tests | Crítica |
| Agent-as-Tool (A2A/MAF) | Parcial | Flujo manager→especialista real + trazabilidad completa | Validación estable SK/MAF en paralelo + criterios de promoción | Alta |

### Criterio de cierre por canal
Un canal se marca **DONE** solo si cumple todo:
- [ ] Conectividad real con proveedor/servicio externo (sandbox o staging real).
- [ ] Flujo E2E reproducible (10/10 corridas exitosas).
- [ ] Policies activas y verificadas (sin bypass).
- [ ] Auditoría completa (request/response + decisión final + executionId).
- [ ] Runbook de operación para onboarding tenant + troubleshooting.

---

## 8) Backlog inmediato (siguiente semana)

1. Seleccionar y congelar el **golden flow** (input, tools, output esperado).
2. Implementar/ajustar conector MCP crítico del flujo.
3. Definir y exponer `Execution Verdict` en API/UI.
4. Crear plantilla de nuevo conector en ToolSDK.
5. Automatizar smoke test de onboarding por tenant.
6. Cerrar al menos 1 canal en estado **DONE real** (sin mocks).

---

## 9) Plan global de ejecución (6 semanas)

> Objetivo: cerrar brecha entre arquitectura sólida y operación comercial repetible por tenant.

### Semana 1 — Baseline y control
**Objetivo**: congelar alcance y medir estado real.

- Congelar definición del **golden flow** (caso de negocio + criterios de éxito).
- Crear tablero único de estado (canales, tools, policies, tenants).
- Definir owners por frente: Runtime, Integrations, Channels, QA, Security.
- Activar reporte diario de:
  - ejecuciones totales,
  - success rate,
  - bloqueos por policy,
  - fallos por tool/canal.

**Salida de semana**:
- Golden flow aprobado.
- Matriz de canales validada por ingeniería + producto.

### Semana 2 — Canal crítico en DONE real
**Objetivo**: cerrar 1 canal en producción controlada (sin mocks).

- Elegir canal prioritario (recomendado: WhatsApp o API directa).
- Endurecer onboarding por tenant (credenciales, webhook, health checks).
- Ejecutar prueba 10/10 E2E con auditoría completa.
- Publicar runbook operativo del canal.

**Salida de semana**:
- 1 canal en estado DONE real.

### Semana 3 — MCP gateway productivo
**Objetivo**: pasar MCP de arquitectura a operación estable.

- Implementar/validar allowlist por tenant para servidores MCP.
- Añadir contract tests de I/O MCP.
- Validar políticas en PreTool/PostTool con masking en evidencia.
- Medir latencia y error rate por servidor MCP.

**Salida de semana**:
- 1 integración MCP crítica funcionando E2E en golden flow.

### Semana 4 — Tool Integration Factory
**Objetivo**: que integrar nuevas tools sea repetible.

- Publicar template oficial de conector (REST/SQL/MCP).
- Crear checklist DoD obligatorio en PR template.
- Automatizar suite mínima: unit + integration + contract.
- Definir criterios de versionado y compatibilidad de schemas.

**Salida de semana**:
- Factory operativa + primer conector creado con template.

### Semana 5 — A2A/MAF operable con governance
**Objetivo**: validar colaboración de agentes de forma trazable.

- Ejecutar flujo manager→especialista (Agent-as-Tool) con trazabilidad completa.
- Definir criterios de promoción SK/MAF por segmento (champion/challenger).
- Validar fallback y manejo de error entre agentes.

**Salida de semana**:
- A2A funcional en escenario real con evidencia de compliance.

### Semana 6 — Productización por tenant
**Objetivo**: convertir la capacidad técnica en proceso comercial repetible.

- Automatizar smoke test de onboarding por tenant.
- Exponer `Execution Verdict` en API/UI para operación de soporte.
- Definir SLO iniciales (success rate, p95 latency, recovery time).
- Cerrar documentación de go-live y handoff a operación.

**Salida de semana**:
- Playbook de onboarding + operación listo para escalar clientes.

---

## 10) RACI sugerido (roles)

| Frente | Responsible | Accountable | Consulted | Informed |
|---|---|---|---|---|
| Runtime/Engine | Backend Lead | CTO | Security, QA | Producto |
| Canales | Integrations Lead | CTO | Backend, QA | Soporte |
| MCP/Tools | Tooling Lead | CTO | Security, Backend | Producto |
| Compliance/Policies | Security Lead | CTO | Backend, Legal/Compliance | Soporte |
| E2E/QA | QA Lead | CTO | Backend, Integrations | Producto |

---

## 11) Cadencia de seguimiento

- **Daily 15 min**: bloqueadores y métricas del día.
- **Review semanal (60 min)**:
  - estado por frente,
  - desvíos vs objetivos semanales,
  - decisión de foco para la semana siguiente.
- **Demo semanal**: evidencia real (no slides, no mocks).

---

## 12) Resultado esperado

Al completar este plan, AgentFlow pasa de “plataforma técnicamente robusta” a “producto operable y vendible con evidencia”, con una ruta clara para escalar integraciones sin perder compliance.
