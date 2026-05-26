# Workflow vs Subflow (Agente) Guidelines

## Objetivo
Definir una regla operativa clara para plataformas multi-agente (text, voice y video/voice futuro) para evitar duplicidad, loops y pérdida de contexto.

## Regla principal
Usa **workflow principal** para orquestación y control de negocio.  
Usa **subflujo de agente (`ai.agent`)** para razonamiento conversacional especializado y reusable.

## Qué va en workflow principal
- Enrutamiento entre etapas (`discover -> qualify -> offer -> close`).
- Integraciones y herramientas (`http`, `storage`, `handoff`, `endCall`).
- Decisiones determinísticas por estado/slots completos.
- Reglas de guardrail (ej. no más de N aclaraciones, escalar a humano).
- Auditoría y métricas del proceso.

## Qué va en subflujo de agente
- Pregunta única por turno.
- Micro-razonamiento contextual por etapa.
- Extracción/normalización de variables del turno.
- Manejo de tono/estilo del canal.
- Resumen de salida estructurada para el workflow.

## Cuándo NO crear subflujo
- Si el nodo solo ejecuta una acción técnica.
- Si la lógica es totalmente determinística y no requiere inferencia.
- Si el comportamiento no se reutilizará en más de un flujo.

## Contrato mínimo entre workflow y subflujo
- Entrada: `input`, `context`, `externalContextRefs`, `conversationState`.
- Estado canónico: `intent`, `slots`, `stage`, `handoff`, `attachments`, `externalContextRefs`.
- Salida: `response`, `updatedSlots`, `nextStageSuggestion`, `confidence`.

## Guardrails obligatorios
- No repreguntar slots ya llenos.
- Si hay 2 fallas seguidas de clasificación/enrutado: escalar.
- Si `producto + modalidad_pago + cantidad` están completos: avanzar a cierre/cotización.
- Registrar en auditoría eventos de loop y bloqueo de repregunta.

## Voice y video/voice (fase futura)
- Mantener el mismo `ConversationState`; solo cambia transporte/media.
- Separar proveedor por componente (`reasoning`, `transcriber`, `voice`).
- Validar compatibilidad idioma/modelo por canal antes de ejecutar.
