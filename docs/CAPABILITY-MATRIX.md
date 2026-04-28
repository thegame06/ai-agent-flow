# Capability Matrix — Studio / Connect / Control

## Objetivo

Definir qué hace cada suite, para quién está diseñada y qué KPI principal debe mover.

## Matriz ejecutiva

| Suite | Capacidades núcleo | Usuario primario | Usuario secundario | KPI norte |
|---|---|---|---|---|
| Studio | Diseño visual de agentes y flujos, prompts, evaluaciones, releases | Product Builder | AI Engineer | Tiempo a primer publish |
| Connect | Operación de canales/sesiones, live executions, incidentes, integraciones | Agent Operator | CX Operations | Tiempo a producción + MTTR |
| Control | Acceso/RBAC, runtime policies, auditoría, observabilidad, compliance | Platform Admin | Security/Compliance | Cobertura de auditoría + tasa de violaciones |

## Detalle de capacidades por suite

### Studio

**Qué hace**
- Construcción y versionado de agentes.
- Simulación/evaluación previa a producción.
- Publicación controlada de artefactos.

**Para quién**
- Equipos de producto IA.
- Equipos que iteran prompts y decisiones de flujo.

**Éxito de negocio**
- Menor tiempo de diseño a release.
- Mayor reutilización de componentes de agente.

### Connect

**Qué hace**
- Gestión de canales (por ejemplo, WhatsApp/API/Web).
- Operación de sesiones y conversaciones activas.
- Monitoreo de ejecuciones e incident response.

**Para quién**
- Operaciones de CX/Soporte.
- Equipos de confiabilidad operativa.

**Éxito de negocio**
- Menor tiempo para activar tráfico real.
- Menos interrupciones y recuperación más rápida.

### Control

**Qué hace**
- Gobierno de accesos y permisos por suite.
- Aplicación de políticas runtime y compliance.
- Auditoría y evidencia para revisiones internas/externas.

**Para quién**
- Administradores de plataforma.
- Equipos de seguridad y cumplimiento.

**Éxito de negocio**
- Menor riesgo operativo/regulatorio.
- Mayor trazabilidad de decisiones críticas.

## RACI simplificado

| Suite | Own | Approve | Consult | Inform |
|---|---|---|---|---|
| Studio | Product + AI Engineering | Tech Lead | Security | Ops |
| Connect | Operations | Ops Lead | Product | Compliance |
| Control | Platform + Security | Compliance Lead | Product/Ops | Leadership |
