# Testing Guide: Intent Routing Integration

## ✅ Checklist de Verificación

### Pre-requisitos

- [ ] Solución compila exitosamente
- [ ] MongoDB corriendo (intents + audit)
- [ ] Redis corriendo (locks + cache)
- [ ] Catálogo de intents cargado (`base-intents.yaml`)
- [ ] Embeddings generados para intents

### Tests Básicos

- [ ] Router Agent detectado correctamente
- [ ] Clasificación se ejecuta antes del LLM
- [ ] Routing decision registrada en audit
- [ ] Fallback a LLM funciona si Intent Routing falla

---

## 🧪 Test Cases

### Test 1: Route - High Confidence

**Input**:
```json
{
  "tenantId": "tenant-test",
  "agentKey": "router-agent",
  "userId": "test-user",
  "userMessage": "Quiero solicitar un préstamo personal"
}
```

**Expected Output**:
```json
{
  "status": "Completed",
  "finalResponse": "✅ Mensaje clasificado como 'loan_application' y enrutado a workflow...",
  "totalSteps": 2,
  "totalTokensUsed": 0,
  "durationMs": < 500
}
```

**Verificaciones**:
- [ ] Intent clasificado como `loan_application`
- [ ] Confidence = `High` (score ≥ 0.90)
- [ ] Routing action = `Route`
- [ ] Workflow ID presente
- [ ] Auditoría registrada en MongoDB

---

### Test 2: Queue - Low Confidence

**Input**:
```json
{
  "tenantId": "tenant-test",
  "agentKey": "router-agent",
  "userId": "test-user",
  "userMessage": "Hola, tengo una duda"
}
```

**Expected Output**:
```json
{
  "status": "Completed",
  "finalResponse": "⚠️ Tu mensaje ha sido agregado a la cola de revisión...",
  "totalSteps": 2,
  "totalTokensUsed": 0,
  "durationMs": < 300
}
```

**Verificaciones**:
- [ ] Confidence = `Low` o `Medium` (score < threshold)
- [ ] Routing action = `Queue`
- [ ] Reason code = `low_confidence`
- [ ] Auditoría registrada

---

### Test 3: Fallback - No Match

**Input**:
```json
{
  "tenantId": "tenant-test",
  "agentKey": "router-agent",
  "userId": "test-user",
  "userMessage": "asdfghjkl random text xyz"
}
```

**Expected Output**:
```json
{
  "status": "Completed",
  "finalResponse": "⚠️ No se pudo identificar la intención de tu mensaje...",
  "totalSteps": 2,
  "totalTokensUsed": 0,
  "durationMs": < 300
}
```

**Verificaciones**:
- [ ] Confidence = `NoMatch` (score < 0.50)
- [ ] Routing action = `Fallback`
- [ ] Reason code = `no_match`
- [ ] Auditoría registrada

---

### Test 4: Reject - Agent Conflict

**Setup**:
1. Crear conversación activa con lock de otro agente
2. Intentar enrutar nuevo mensaje a agente diferente

**Input**:
```json
{
  "tenantId": "tenant-test",
  "agentKey": "router-agent",
  "userId": "test-user",
  "userMessage": "Quiero reportar un fraude",
  "sessionContext": {
    "sessionId": "session-with-active-lock"
  }
}
```

**Expected Output**:
```json
{
  "status": "Failed",
  "errorCode": "AgentConflict",
  "finalResponse": "🚫 Conflicto: otro agente está gestionando esta conversación...",
  "totalSteps": 2,
  "totalTokensUsed": 0,
  "durationMs": < 200
}
```

**Verificaciones**:
- [ ] Routing action = `Reject`
- [ ] Reason code = `agent_conflict`
- [ ] Lock NO adquirido
- [ ] Auditoría registrada con conflicto

---

### Test 5: Fallback to LLM - Intent Routing Disabled

**Setup**: No inyectar `IIntentScoringEngine` o `IRoutingOrchestrator`

**Input**:
```json
{
  "tenantId": "tenant-test",
  "agentKey": "router-agent",
  "userId": "test-user",
  "userMessage": "Quiero solicitar un préstamo"
}
```

**Expected Output**:
- Router Agent ejecuta flujo LLM tradicional
- Usa tools MCP (`af_trigger_workflow`)
- `totalTokensUsed` > 0
- `durationMs` > 1000

**Verificaciones**:
- [ ] Intent Routing NO se ejecutó (log indica fallback)
- [ ] LLM fue llamado
- [ ] Tool `af_trigger_workflow` ejecutado
- [ ] Resultado correcto

---

### Test 6: Exception Handling

**Setup**: Simular error en clasificación (e.g., Redis down)

**Expected Behavior**:
- [ ] Exception capturada
- [ ] Log de error registrado
- [ ] Sistema continúa con flujo LLM
- [ ] No se rompe la ejecución

---

## 📊 Métricas a Validar

### Performance

| Métrica | Objetivo | Medición |
|---------|----------|----------|
| Latencia total (Route) | < 500ms | Usar `DurationMs` en result |
| Latencia clasificación | < 300ms | Ver logs de `IIntentScoringEngine` |
| Latencia routing | < 200ms | Ver logs de `IRoutingOrchestrator` |
| Tokens LLM usados | 0 | Verificar `TotalTokensUsed == 0` |

### Precisión

| Escenario | Precisión Esperada |
|-----------|--------------------|
| Frases de entrenamiento exactas | 100% (score ≥ 0.95) |
| Variaciones de intenciones conocidas | > 95% (score ≥ 0.85) |
| Mensajes ambiguos | Queue (requiere revisión) |
| Mensajes sin sentido | Fallback (no match) |

---

## 🔍 Debugging

### Ver Logs de Clasificación

```bash
# Filtrar por Intent Routing
docker logs agentflow-api | grep "Intent"

# Ejemplo de salida:
# [INFO] Router agent detected - using Intent Routing system
# [INFO] Intent classified: loan_application with confidence 92.00% (High) in 245ms
# [INFO] Routing decision: Action=Route, Reason=matched, Duration=387ms
```

### Verificar Auditoría en MongoDB

```javascript
// MongoDB shell
use agentflow;

db.audit_log.find({
  eventType: "RoutingDecision",
  tenantId: "tenant-test"
}).sort({ timestamp: -1 }).limit(10).pretty();

// Ejemplo de documento:
{
  "_id": ObjectId("..."),
  "executionId": "exec-123",
  "agentId": "router-agent",
  "eventType": "RoutingDecision",
  "eventJson": {
    "intentKey": "loan_application",
    "action": "Route",
    "workflowId": "wf-loan-123",
    "confidence": "High",
    "score": 0.92,
    "durationMs": 387
  },
  "timestamp": ISODate("2026-05-18T10:23:45Z")
}
```

### Verificar Locks en Redis

```bash
# Redis CLI
redis-cli

# Ver locks activos
KEYS "agentflow:conversation:lock:*"

# Ver detalles de un lock
GET "agentflow:conversation:lock:tenant-test:session-456"

# Ejemplo de valor:
{
  "agentId": "agent-loan-officer",
  "acquiredAt": "2026-05-18T10:23:45Z",
  "expiresAt": "2026-05-18T10:28:45Z"
}
```

---

## 🛠️ Comandos de Testing

### Ejecutar Tests Unitarios

```bash
# Tests de AgentExecutionEngine
dotnet test tests/AgentFlow.Core.Engine.Tests/ --filter "Category=IntentRouting"

# Tests de IIntentScoringEngine
dotnet test tests/AgentFlow.Intents.Tests/ --filter "FullyQualifiedName~IntentScoringEngine"

# Tests de IRoutingOrchestrator
dotnet test tests/AgentFlow.Intents.Tests/ --filter "FullyQualifiedName~RoutingOrchestrator"
```

### Testing Manual con Curl

```bash
# Configurar variables
TENANT_ID="tenant-test"
AGENT_KEY="router-agent"
API_URL="http://localhost:5000"

# Test 1: High Confidence Route
curl -X POST "$API_URL/api/agents/execute" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "'$TENANT_ID'",
    "agentKey": "'$AGENT_KEY'",
    "userId": "test-user",
    "userMessage": "Quiero solicitar un préstamo personal"
  }' | jq

# Test 2: Low Confidence Queue
curl -X POST "$API_URL/api/agents/execute" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "'$TENANT_ID'",
    "agentKey": "'$AGENT_KEY'",
    "userId": "test-user",
    "userMessage": "Hola, tengo una duda"
  }' | jq

# Test 3: No Match Fallback
curl -X POST "$API_URL/api/agents/execute" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "'$TENANT_ID'",
    "agentKey": "'$AGENT_KEY'",
    "userId": "test-user",
    "userMessage": "xyz random text 123"
  }' | jq
```

---

## 📈 Dashboard de Métricas

### Grafana Queries (OpenTelemetry)

```promql
# Latencia promedio de Intent Routing
avg(intent_classification_duration{tenant_id="$tenant"})

# Tasa de éxito de routing
sum(rate(routing_action{action="Route"}[5m])) / sum(rate(routing_action[5m]))

# Porcentaje de mensajes que requieren revisión
sum(rate(routing_action{action=~"Queue|Fallback"}[5m])) / sum(rate(routing_action[5m]))

# Llamadas LLM evitadas
sum(rate(llm_call_avoided{value="true"}[5m]))
```

---

## ✅ Criteria de Aceptación Final

- [ ] **Performance**: 95% de clasificaciones < 500ms
- [ ] **Precisión**: 99% de intenciones conocidas clasificadas correctamente
- [ ] **Reliability**: 0 excepciones no manejadas
- [ ] **Observability**: Auditoría completa en MongoDB
- [ ] **Backward Compatibility**: Agentes no-Router siguen funcionando
- [ ] **Cost Reduction**: 100% de clasificaciones sin tokens LLM

---

**Próximos Tests (Fase 2.3)**:
- [ ] Inbox Service: Almacenamiento de conversaciones encoladas
- [ ] API Endpoints: Consulta y resolución de inbox

**Próximos Tests (Fase 3)**:
- [ ] Workflow Engine: Disparo automático de workflows
- [ ] End-to-End: Mensaje → Clasificación → Routing → Workflow → Respuesta
