# AgentFlow — Progress, Roadmap y colaboración

> Última actualización: 2026-02-23

## Estado actual

El proyecto ya cuenta con una base sólida para construir agentes de IA empresariales:
- motor de ejecución multi-step,
- políticas y guardrails,
- evaluación continua (incluyendo shadow),
- capacidad HITL,
- y un frontend operativo tipo command center.

## Hitos recientes completados

### [2026-02-23] — UX/Backend alignment for Model Routing, Tools y Chat Threads

- **Model Routing API operativa (básica)**:
  - nuevos endpoints en `ModelRoutingController` para:
    - listar providers,
    - listar modelos por provider,
    - registrar modelo (`POST /model-routing/models`),
    - test de salud (`POST /model-routing/models/{id}/test`),
    - promover a primary (`POST /model-routing/models/{id}/set-primary`),
    - remover modelo (`DELETE /model-routing/models/{id}`).
- **Registry de modelos expandido**:
  - `IModelRegistry` ahora soporta listado de providers y remoción de modelos.
  - `StubModelProvider` admite `ProviderId` configurable.
- **Designer mejor conectado al runtime**:
  - pestaña **Tools** en Agent Designer con bind/unbind real desde `/extensions/tools`.
  - configuración de modelo ahora incluye `provider` (ya no hardcodeado).
  - Canvas actualiza conexiones al estado de pasos (ya no TODO).
- **Chat con historial real por thread**:
  - carga de thread existente por agente,
  - carga de historial desde `/threads/{id}/history`,
  - opción para iniciar nuevo thread desde UI.

### [2026-02-23] — Enterprise Resilience & Sovereign Guardrails

- **Self-Healing Brain**:
  - Implementación de limpieza automática de respuestas Markdown/JSON en `SemanticKernelBrain`.
  - Mecanismo de fallback resiliente para evitar fallos de ejecución ante anomalías de formato del LLM.
- **Sovereign PII Redaction**:
  - Nuevo `PiiRedactionEvaluator` para detección proactiva de datos sensibles (Emails, CC, SSN).
  - Integración en el Pipeline de Gobernanza bajo los checkpoints `PostLLM`, `PreTool` y `PreResponse`.
- **Arquitectura de Resiliencia**:
  - Documentación técnica detallada de las defensas core en `docs/RESILIENCE-AND-SECURITY-UPGRADE.md`.

### [2026-02-21] — Fase 2/3: Core Logic & Enterprise Governance

- **HITL**:
  - estado `PausedForReview` en `AgentExecution`,
  - almacenamiento persistente de checkpoints,
  - endpoint para aprobar/rechazar decisiones pausadas.
- **Políticas por segmento**:
  - contexto de evaluación con `UserSegments`,
  - matching automático por perfil/segmento.
- **Evaluación shadow**:
  - soporte champion/challenger con `ShadowAgentId`,
  - ejecución paralela para comparación sin impacto en salida principal.
- **Prompting persistente**:
  - perfiles versionados de prompt,
  - render dinámico en el loop de ejecución.
- **Frontend MVP (Command Center)**:
  - dashboard, cola de revisión HITL y vista de decision trace.

## Prioridades actuales (Q1–Q2 2026)

1. **Estabilidad y hardening de producción**
   - mejorar manejo de errores transitorios,
   - reforzar circuit breakers y políticas de retry,
   - ampliar tests de integración end-to-end.

2. **Experiencia de desarrollo (DX)**
   - plantillas de agentes y herramientas listas para usar,
   - documentación de onboarding más guiada,
   - ejemplos reproducibles por dominio (soporte, riesgo, ops).

3. **Observabilidad avanzada**
   - dashboards operativos más detallados,
   - trazas distribuidas y correlación cross-service,
   - reportes comparativos para champion/challenger.

## Validación estratégica reciente

- [2026-02-23] Se agregó evaluación formal de migración de **Semantic Kernel** a **Microsoft Agent Framework (MAF + MCP)** con impactos, riesgos, y plan por fases en `docs/MAF-MIGRATION-ASSESSMENT.md`.
- [2026-02-23] Se agregó blueprint de arquitectura modular para coexistencia **SK + MAF** con **MCP como estándar** y estrategia `Add/Use` por extensiones en `docs/MCP-SK-MAF-MODULAR-ARCHITECTURE.md`.

## Qué falta (backlog priorizado)

- Flujos de migración/versionado de DSL más automáticos.
- Set extendido de plugins de referencia (CRM, ERP, colas).
- Mayor cobertura de pruebas de regresión para frontend.
- Guías de despliegue en cloud (Kubernetes/Azure/AWS).

## ¿Cómo puede ayudar un contribuidor externo?

### Aportes de alto impacto
- agregar pruebas unitarias/integración en módulos críticos,
- crear plugins de herramientas bajo `src/AgentFlow.Extensions` o `src/AgentFlow.ToolSDK`,
- mejorar documentación y quickstarts por caso de uso,
- reportar bugs con pasos de reproducción claros.

### Flujo recomendado de contribución
1. Abrir issue con contexto de problema/propuesta.
2. Proponer enfoque técnico mínimo (alcance + archivos).
3. Enviar PR pequeño y testeable.
4. Incluir evidencia de pruebas (`dotnet test`, build, screenshots si aplica UI).

## Señales de avance esperadas en próximos sprints

- Incremento de cobertura en tests de integración.
- Menor tiempo de onboarding para correr demo local.
- Mejor visibilidad de métricas de calidad y costo por ejecución.
