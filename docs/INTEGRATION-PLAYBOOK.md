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

## 12) Avances de ejecución (bitácora exacta)

### 2026-02-25/26 — Arranque operativo

#### Validaciones ejecutadas
1. **Backend tests (intentado)**
   - Comando: `dotnet test AgentFlow.sln -v minimal`
   - Resultado: **FAIL** por permisos de archivos generados con `sudo`.
   - Error observado: `Access to the path ... is denied` sobre `src/AgentFlow.Policy/obj/...tmp`.

2. **Frontend discovery**
   - Se detectaron dos apps:
     - `frontend/aiagent_flow/package.json`
     - `frontend/designer/package.json`

3. **Frontend aiagent_flow**
   - `npm ci` ✅
   - `npm run build` ✅
   - `npm run lint` ✅
   - `npm test` ❌ (no existe script `test` en package.json)

4. **Frontend designer**
   - `npm ci` ✅
   - `npm run build` ✅
   - `npm run lint` ❌ (7 errores, 2 warnings)
   - Hallazgos principales:
     - uso de `any` (`@typescript-eslint/no-explicit-any`)
     - variable no usada (`no-unused-vars`)
     - warnings de dependencias en `useEffect`

#### Bloqueadores actuales
- **B1 (Crítico):** Artefactos root-owned por ejecución con `sudo dotnet ...` impiden correr tests como usuario normal. ✅ **Resuelto** (se corrigieron permisos).
- **B2 (Medio):** `frontend/designer` no pasa lint. ✅ **Resuelto** (0 errores, quedan 2 warnings de hooks).
- **B3 (Bajo):** No hay scripts `test` en frontends (solo build/lint). ⏳ Pendiente.

#### Acciones inmediatas (siguiente ciclo)
1. Definir estrategia mínima de tests frontend (Vitest/Jest + smoke tests).
2. Reparar `tests/AgentFlow.Tests.Integration` (actualmente desalineados del contrato en `AgentFlow.Abstractions`).
3. Re-ejecutar matriz de verificación completa y registrar evidencia.

### 2026-02-26 — Ejecución tras desbloqueo

#### Validaciones ejecutadas
1. **Backend unit tests**
   - Comando: `dotnet test AgentFlow.sln -v minimal`
   - Resultado: ✅ `Passed: 168, Failed: 0` en `AgentFlow.Tests.Unit`.

2. **Backend integration tests**
   - Comando: `dotnet test tests/AgentFlow.Tests.Integration/AgentFlow.Tests.Integration.csproj -v minimal`
   - Resultado (estado anterior): ❌ **No compilaba** por contratos obsoletos.
   - Acción aplicada: se reescribió `LoanOfficerDemoTests` contra contratos vigentes (`ExecutionStatus`, `FinalResponse`, `ToolSDK.ToolResult.Output`, etc.).
   - Resultado actual: ✅ **Passed: 3, Failed: 0**.

3. **Frontend aiagent_flow**
   - `npm run lint` ✅
   - `npm run build` ✅
   - Nota: warning de bundles grandes (>500kB) en build.

4. **Frontend designer**
   - `npm run lint` ✅ sin errores (2 warnings `react-hooks/exhaustive-deps`).
   - `npm run build` ✅.

#### Estado operativo actual
- Backend unitario: **verde** (168/168).
- Backend integración: **verde** (3/3).
- Frontend build/lint: **verde** en ambas apps (con warnings no bloqueantes).
- Frontend tests automáticos: **pendiente** (scripts `test` no definidos).
- `AgentFlow.Tests.Integration` ya fue agregado a `AgentFlow.sln`; `dotnet test AgentFlow.sln` ejecuta unit + integration.

### 2026-02-26 — Descubrimiento crítico: canales e integraciones aún en mock

#### Hallazgos directos en código
1. **WhatsApp client en mock**
   - `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppClient.cs`
   - Evidencia: mensajes y QR mock (`mock_wamid`, `mock-qr-code`, "mock send").

2. **Tools demo en mock**
   - `src/AgentFlow.Extensions/Tools/BureauAPIPlugin.cs`
   - `src/AgentFlow.Extensions/Tools/FinancialModelPlugin.cs`
   - `src/AgentFlow.Extensions/Tools/EmailNotificationPlugin.cs`

3. **Model routing con stub provider**
   - `src/AgentFlow.ModelRouting/ModelRouter.cs` (provider `stub`, respuesta `Stub response`).

#### Implicación
El runtime y pruebas están verdes, pero **el criterio comercial “canal real + integración real” sigue pendiente**. No puede declararse DONE de producto hasta retirar/aislar mocks de runtime productivo.

#### Próximas acciones (en curso)
1. Crear plan de reemplazo de mocks por adaptadores reales (canales + tools + model provider).
2. Definir `feature flags` explícitos para que mock solo viva en entornos dev/test.
3. Ejecutar primer E2E de canal real con evidencia auditable (10/10 corridas).

### 2026-02-26 — WhatsApp: doble vía (Business + QR) con contrato de transporte

#### Cambio aplicado
Se introdujo arquitectura de transporte para soportar dos modos:

1. `IWhatsAppTransport` (nuevo contrato)
   - Archivo: `src/AgentFlow.Infrastructure/Channels/WhatsApp/IWhatsAppTransport.cs`
   - Define `ConnectAsync`, `DisconnectAsync`, `IsConnectedAsync`, `SendTextMessageAsync`.

2. `WhatsAppBusinessApiTransport` (real)
   - Archivo: `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppBusinessApiTransport.cs`
   - Valida credenciales en Graph API.
   - Envía mensajes reales `POST {BaseUrl}/{PhoneNumberId}/messages`.
   - Devuelve `messages[0].id` real.

3. `WhatsAppWebQrTransport` (placeholder controlado)
   - Archivo: `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppWebQrTransport.cs`
   - No finge éxito: responde Fail explícito con mensaje de no implementado.

4. `WhatsAppClient` refactorizado
   - Archivo: `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppClient.cs`
   - Delega por proveedor de transporte:
     - `ConnectWithBusinessApiAsync` usa Business transport.
     - `ConnectWithQrAsync` usa QR transport.

#### Verificación
- `dotnet build src/AgentFlow.Infrastructure/AgentFlow.Infrastructure.csproj -v minimal` ✅
- `dotnet test tests/AgentFlow.Tests.Integration/AgentFlow.Tests.Integration.csproj -v minimal` ✅ (3/3)

#### Estado
- WhatsApp Business API: **implementación real activa** para envío.
- WhatsApp QR: **bridge runtime implementado** para pruebas internas (session start/qr/status/send/disconnect).
- Gateway/API: build validado (`AgentFlow.Api` compila) y webhook QR/Meta disponibles en `WhatsAppWebhookController`.
- Pendiente para DONE real de canal:
  - ejecución E2E 10/10,
  - evidencia auditada por ejecución,
  - decisión formal de compliance para producción (Business API recomendado).

### 2026-02-26 — QR Bridge operativo para pruebas sin cuenta Business

#### Cambio aplicado
- Nuevo módulo: `tools/whatsapp-qr-bridge`
  - `server.js` (Express + whatsapp-web.js)
  - `package.json`
  - `README.md`
- Capacidades:
  - iniciar sesión QR por `channelId`,
  - exponer QR (`/session/qr`),
  - estado de conexión (`/session/status`),
  - enviar mensajes (`/messages/send`),
  - desconectar (`/session/disconnect`),
  - forward inbound al gateway de AgentFlow (`/webhooks/whatsapp/qr`).
- Infraestructura WhatsApp en .NET:
  - `WhatsAppWebQrTransport` ahora consume bridge real vía HTTP.
  - `WhatsAppOptions` agrega `QrBridgeBaseUrl` y `QrBridgeApiKey`.

#### Verificación
- `dotnet build src/AgentFlow.Api/AgentFlow.Api.csproj -v minimal` ✅

### 2026-02-26 — Habilitación de pruebas completas desde Web UI (QR)

#### Cambios aplicados
1. **Endpoint QR en API de canales**
   - Archivo: `src/AgentFlow.Api/Controllers/ChannelsController.cs`
   - Nuevo endpoint: `GET /api/v1/tenants/{tenantId}/channels/{channelId}/qr`
   - Retorna `qrCode` real si está disponible.

2. **Canal WhatsApp expone QR actual**
   - Archivo: `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppChannelHandler.cs`
   - Nuevo método: `GetQrCodeAsync()`.

3. **Endpoint de estado para operación web**
   - Archivo: `src/AgentFlow.Api/Controllers/ChannelsController.cs`
   - Nuevo: `GET /channels/{channelId}/status`
   - Incluye `healthy`, `message`, `checkedAt`, y `qrAvailable` para WhatsApp QR.

4. **Cliente/transport QR con lectura de QR real**
   - Archivos:
     - `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppClient.cs`
     - `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppWebQrTransport.cs`
   - Soporte para consultar `qrCode` desde el bridge (`/session/qr`).

4. **Web UI consume QR real**
   - Archivo: `frontend/aiagent_flow/src/aiagentflow/pages/channels/ChannelsPage.tsx`
   - Al activar canal WhatsApp en modo QR:
     - consulta endpoint real de QR (poll corto),
     - muestra QR real en diálogo,
     - botón `Refresh QR` para reintentar.

#### Validación ejecutada
- `dotnet build src/AgentFlow.Infrastructure/AgentFlow.Infrastructure.csproj -v minimal` ✅
- `dotnet build src/AgentFlow.Api/AgentFlow.Api.csproj -v minimal` ✅
- `npm run lint` en `frontend/aiagent_flow` ✅
- `npm run build` en `frontend/aiagent_flow` ✅

#### Preflight para prueba E2E desde web
1. Levantar API AgentFlow.
2. Levantar bridge QR:
   - `cd tools/whatsapp-qr-bridge && npm install && npm start` (con `AGENTFLOW_BASE_URL` y `TENANT_ID`).
3. Configurar `WhatsAppOptions`:
   - `QrBridgeBaseUrl`
   - `QrBridgeApiKey` (si aplica)
4. En Web UI:
   - crear canal WhatsApp con `AuthMode=qr`,
   - activar canal,
   - escanear QR mostrado,
   - enviar mensaje real por WhatsApp.

### 2026-02-26 — Evidencia operativa por sesión/canal

#### Cambios aplicados
1. **Mensajes de sesión con IDs de trazabilidad**
   - Archivo: `src/AgentFlow.Api/Controllers/ChannelSessionsController.cs`
   - `ChannelMessageDto` ahora incluye:
     - `AgentExecutionId`
     - `ChannelMessageIdIn`
     - `ChannelMessageIdOut`
     - `Metadata`

2. **WhatsApp outbound guarda message id real en metadata**
   - Archivo: `src/AgentFlow.Infrastructure/Channels/WhatsApp/WhatsAppChannelHandler.cs`
   - Se persiste `wa_message_id_out` al enviar respuesta.

3. **Estado de canal con readiness QR desde API**
   - Archivo: `src/AgentFlow.Api/Controllers/ChannelsController.cs`
   - Nuevo endpoint `GET /channels/{channelId}/status` devuelve `qrAvailable` para modo QR.

#### Verificación
- `dotnet build src/AgentFlow.Api/AgentFlow.Api.csproj -v minimal` ✅
- `npm run lint` en `frontend/aiagent_flow` ✅

### 2026-02-26 — Hardening inicial del QR bridge

#### Cambios aplicados
- Archivo: `tools/whatsapp-qr-bridge/server.js`
- Mejoras:
  - reintentos con backoff al forward inbound hacia AgentFlow,
  - endpoint `GET /health` con estado de sesiones,
  - `GET /session/status` enriquecido (`lastSeenAt`, `lastForwardAt`, `lastError`),
  - rate limit básico en `/messages/send` (20 msg/min por `channelId+to`),
  - tracking de errores/actividad por sesión.

#### Objetivo cubierto
- Mayor estabilidad para corrida 10/10 y troubleshooting desde operación.

### 2026-02-26 — Mejora de performance frontend (aiagent_flow)

#### Objetivo
Reducir tamaño de chunks iniciales y eliminar warning de bundle >500kB.

#### Cambio aplicado
- Archivo: `frontend/aiagent_flow/vite.config.ts`
- Se agregó `build.rollupOptions.output.manualChunks` con partición explícita:
  - `vendor-react`
  - `vendor-mui`
  - `vendor-grid`
  - `vendor-dates`
  - `vendor-utils`
  - `vendor-misc`

#### Resultado medido (build)
- **Antes**:
  - `index-*.js`: **1,128.94 kB** (gzip 366.37 kB)
  - warning de chunks >500kB: **presente**
- **Después**:
  - `index-*.js`: **340.79 kB** (gzip 102.62 kB)
  - chunks principales segmentados (`vendor-grid` 310.03 kB, `vendor-mui` 394.51 kB)
  - warning de chunks >500kB: **eliminado**

---

## 13) Resultado esperado

Al completar este plan, AgentFlow pasa de “plataforma técnicamente robusta” a “producto operable y vendible con evidencia”, con una ruta clara para escalar integraciones sin perder compliance.
