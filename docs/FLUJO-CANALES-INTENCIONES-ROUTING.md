# Flujo Actual de Mensajes, Intenciones y Routing (Canales)

Este documento describe el comportamiento actual implementado en AgentFlow para mensajes entrantes por canal, clasificación por intenciones y enrutamiento entre agentes.

## 1) Entrada del cliente al canal

1. El cliente escribe por un canal (WhatsApp, WebChat, API, etc.).
2. El mensaje llega a `POST /api/v1/tenants/{tenantId}/channels/{channelId}/messages`.
3. `ChannelsController` valida tenant/canal y obtiene o crea sesión del canal con el handler.
4. Se crea `ChannelMessage` de entrada y se envía a `ChannelGateway.ProcessMessageAsync(...)`.

Referencias:
- `src/AgentFlow.Api/Controllers/ChannelsController.cs`
- `src/AgentFlow.Core.Engine/ChannelGateway.cs`

## 2) Resolución inicial del agente que procesa

En `ChannelGateway` se resuelve el agente a ejecutar para esa sesión:

1. Si la sesión ya tiene `AgentId`, se reutiliza (conversación ya enrutada).
2. Si no tiene, se usa configuración del canal (`RouterAgentId` o fallback configurado).
3. La sesión queda vinculada al agente elegido (`session.LinkAgent(...)`).

Esto garantiza continuidad: una conversación no “salta” de agente en cada mensaje.

## 3) Catálogo de intenciones usado por el Router

Cuando el agente que ejecuta es el router del canal:

1. `ChannelGateway` llama `IIntentRoutingStore.GetRulesByChannelAsync(tenantId, channel)`.
2. Arma `IntentCatalog` (JSON) con `intentKey`, descripción, ejemplos, `targetAgentId`, `workflowId`.
3. Inyecta ese catálogo en `ContextJson` del `AgentExecutionRequest`.

El Router usa ese contexto para decidir handoff/routing.

Referencia:
- `src/AgentFlow.Core.Engine/ChannelGateway.cs`

## 4) Cómo se cargan esas reglas de intención por canal

Actualmente hay dos formas:

1. Manual por API/UI de intent-routing (`/intent-routing/rules`).
2. Nuevo flujo por canal: botón **Cargar intenciones** en Channels:
   - `GET /channels/{channelId}/intents/catalog` (muestra catálogo base + seleccionadas).
   - `POST /channels/{channelId}/intents/apply` (crea/actualiza/elimina reglas gestionadas para ese canal).

Nota: `base-intents.yaml` es catálogo base; la operación en runtime se hace con reglas persistidas en `intent_rules`.

Referencias:
- `src/AgentFlow.Api/Controllers/ChannelsController.cs`
- `src/AgentFlow.Infrastructure/Repositories/MongoIntentRoutingStore.cs`
- `src/AgentFlow.Intents/Catalog/base-intents.yaml`

## 5) Qué pasa si hay match de intención

Cuando el Router detecta intención y decide enrutamiento:

1. Emite directiva de handoff/routing (incluye agente destino y/o workflow).
2. `ChannelGateway` interpreta la directiva.
3. Hace handoff al `WorkflowBrain` o agente objetivo.
4. Actualiza metadata/sesión para que próximos mensajes continúen en el dueño actual.

Resultado: la conversación sale del Router y continúa en el agente especializado.

## 6) Qué pasa si NO hay match

Si no hay match viable o la confianza es baja:

1. El motor de routing cae en acción de fallback/no_match.
2. Puede quedar en Router (pregunta aclaratoria) o ir al camino de revisión según políticas.
3. No se ejecuta handoff fuerte a workflow especializado hasta tener señal suficiente.

Referencias:
- `src/AgentFlow.Intents/Routing/RoutingOrchestrator.cs`
- `src/AgentFlow.Intents/Classification/IntentScoringEngine` (vía interfaz)

## 7) Qué pasa con un segundo mensaje del mismo cliente

Si la conversación ya fue enrutada:

1. La sesión ya tiene `AgentId` asignado.
2. `ChannelGateway` reutiliza ese `AgentId`.
3. El mensaje nuevo va directo al agente dueño actual de la conversación.
4. No se reclasifica desde cero en cada turno, salvo lógica explícita de handoff/reasignación.

Esto evita “rebotes” y mantiene contexto conversacional.

## 8) Resumen operacional

1. Mensaje entra por canal -> `ChannelsController` -> `ChannelGateway`.
2. `ChannelGateway` decide agente inicial/actual según sesión.
3. Si ejecuta Router, inyecta catálogo de intenciones del canal.
4. Router clasifica:
   - Con match: handoff a agente/workflow objetivo.
   - Sin match: fallback/clarificación/revisión.
5. Mensajes siguientes de la misma sesión siguen con el agente ya asignado.

## 9) Dónde observar en tiempo real

- Estado y salud del canal: `GET /channels/{channelId}/status`
- Reglas activas del routing: `GET /intent-routing/rules`
- Conversaciones en bandeja intent-routing: `GET /intent-routing/conversations`
- Estadísticas de clasificación: `GET /intent-routing/stats`

## 10) Aclaración clave de arquitectura

- El Router Agent sigue siendo parte central del flujo.
- “Cargar intenciones” por canal no reemplaza al Router; alimenta las reglas que el Router usa para decidir.
- El enrutamiento final depende de reglas por tenant+canal y del agente configurado en el canal.
