# Intent Routing & Intelligent Traffic Controller — Enterprise Architecture

> **Estado**: 🚧 Design Phase — Unicorn-Grade Strategy  
> **Fecha**: 2026-05-18  
> **Owner**: Orchestrator Agent  
> **Prioridad**: 🔴 CRÍTICA — Core Platform Component  

---

## 🎯 Visión Estratégica

El módulo de **Intenciones y Routing Inteligente** debe evolucionar de una simple lista CRUD a un **AI Traffic Controller** enterprise-grade que garantice:

- **99% de precisión** en clasificación de intenciones
- **0% de colisiones** entre agentes AI compitiendo
- **0 conversaciones perdidas** (fallback intelligence)
- **Trazabilidad completa** de decisiones de routing
- **Ownership conversacional** estricto y determinístico
- **Testing automatizado** con benchmarks continuos

### ¿Por qué es crítico?

> **Si el Router falla, toda la plataforma falla.**

Este módulo es el **núcleo de coordinación** entre:
- Mensajes entrantes (multi-canal)
- Workflows y agentes
- Contexto conversacional
- Decisiones de ownership
- Observabilidad del sistema

---

## 📊 Análisis del Estado Actual

### ✅ Lo que existe hoy

1. **Modelo de datos básico** (`IntentRoutingRule`):
   - IntentKey (slug único)
   - IntentDescription (descripción para LLM)
   - ExamplePhrases (frases de entrenamiento)
   - SourceAgentId/TargetAgentId (routing)
   - WorkflowDefinitionId (workflow a disparar)
   - Priority, Enabled, Channel

2. **Persistencia MongoDB** (`MongoIntentRoutingStore`):
   - CRUD básico de reglas
   - Filtrado por canal
   - Simulación simple de routing

3. **API REST** (`IntentRoutingController`):
   - GET/POST/PUT/PATCH para reglas
   - Endpoint de simulación básica

4. **Herramientas MCP para Router**:
   - `af_list_workflows`: listar workflows disponibles
   - `af_trigger_workflow`: disparar workflow por evento

5. **Auditoría de decisiones**:
   - Registro de `RoutingDecision` cuando Router completa
   - Guardado de workflow disparado

### ❌ Gaps críticos identificados

#### 1. **Clasificación Inteligente**
- ❌ No hay motor de clasificación semántica (embeddings)
- ❌ No hay confidence scores
- ❌ No hay ranking de intenciones candidatas
- ❌ No hay detección de ambigüedad
- ❌ No hay explicabilidad de decisiones

#### 2. **Catálogo Base**
- ❌ Sistema inicia vacío (sin calibración)
- ❌ No hay intenciones preconfiguradas del producto
- ❌ No hay validación contra benchmarks
- ❌ No hay versionamiento de intenciones

#### 3. **Ownership Conversacional**
- ❌ No hay control de concurrencia de agentes
- ❌ Múltiples workflows pueden dispararse simultáneamente
- ❌ No hay locking conversacional
- ❌ No hay prevención de race conditions
- ❌ No hay handoff explícito entre agentes

#### 4. **Fallback Intelligence**
- ❌ No hay manejo de "No Match"
- ❌ No hay estados de clasificación (Low Confidence, Pending Review)
- ❌ No hay escalación automática a humanos
- ❌ Conversaciones sin match se pierden

#### 5. **Observabilidad**
- ❌ No hay métricas de precisión
- ❌ No hay detección de false positives/negatives
- ❌ No hay alertas de degradación
- ❌ No hay dashboards operacionales

#### 6. **UI/UX**
- ❌ No existe pantalla de Intenciones en frontend
- ❌ No hay Playground de testing
- ❌ No hay AI Assistant para creación de intenciones
- ❌ No hay Inbox inteligente con estados

#### 7. **Testing Automatizado**
- ❌ No hay suite de regresión
- ❌ No hay validación de happy paths
- ❌ No hay benchmarks continuos
- ❌ No hay simulación masiva

---

## 🏗️ Arquitectura Objetivo

### Componentes del Sistema

```
┌────────────────────────────────────────────────────────────────────┐
│                    INTELLIGENT ROUTING LAYER                       │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │              1. INTENT CLASSIFICATION ENGINE                │  │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │  │
│  │  │   Semantic   │  │   Keyword    │  │   Scoring    │      │  │
│  │  │   Matcher    │  │   Matcher    │  │   Engine     │      │  │
│  │  │  (Embeddings)│  │   (Rules)    │  │ (Confidence) │      │  │
│  │  └──────────────┘  └──────────────┘  └──────────────┘      │  │
│  └─────────────────────────────────────────────────────────────┘  │
│                              ↓                                     │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │              2. ROUTING ORCHESTRATOR                        │  │
│  │  • Workflow Selection                                       │  │
│  │  • Agent Arbitration                                        │  │
│  │  • Priority Resolution                                      │  │
│  │  • Conflict Detection                                       │  │
│  └─────────────────────────────────────────────────────────────┘  │
│                              ↓                                     │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │              3. OWNERSHIP MANAGER                           │  │
│  │  • Conversation Locking                                     │  │
│  │  • Agent Ownership Tracking                                 │  │
│  │  • Handoff Coordination                                     │  │
│  │  • Timeout & Release                                        │  │
│  └─────────────────────────────────────────────────────────────┘  │
│                              ↓                                     │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │              4. FALLBACK INTELLIGENCE                       │  │
│  │  • No Match Detection                                       │  │
│  │  • Low Confidence Handling                                  │  │
│  │  • Human Escalation                                         │  │
│  │  • Inbox Queue Management                                   │  │
│  └─────────────────────────────────────────────────────────────┘  │
│                              ↓                                     │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │              5. OBSERVABILITY & METRICS                     │  │
│  │  • Classification Accuracy                                  │  │
│  │  • False Positives/Negatives                                │  │
│  │  • Agent Conflicts                                          │  │
│  │  • Response Times                                           │  │
│  └─────────────────────────────────────────────────────────────┘  │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

---

## 🧩 Módulos Técnicos Detallados

### 1. Intent Classification Engine

#### 1.1 Semantic Matcher (Embeddings)

**Propósito**: Matching semántico de alta precisión usando vectores.

**Tecnología**:
- Vector Store: **Qdrant** (ya existente en AgentFlow)
- Embedding Model: `text-embedding-3-small` (OpenAI) o `all-MiniLM-L6-v2` (local)
- Similarity: Cosine similarity

**Flujo**:
1. Mensaje entrante → Embedding
2. Vector search en catálogo de intenciones
3. Top-K candidatos con similarity score
4. Threshold mínimo: 0.75 (configurable)

**Contratos**:
```csharp
public interface ISemanticIntentMatcher
{
    Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        int topK = 5,
        CancellationToken ct = default);
}

public sealed record IntentMatch
{
    public required string IntentKey { get; init; }
    public required float SimilarityScore { get; init; }
    public required string MatchedVia { get; init; } // "semantic" | "keyword" | "rule"
    public required IntentRoutingRule Rule { get; init; }
}
```

#### 1.2 Keyword Matcher (Rules)

**Propósito**: Matching determinístico por palabras clave y reglas explícitas.

**Estrategias**:
- Regex patterns
- Exact keyword matching
- N-gram matching
- Negation rules ("no quiero" → exclude intent)

**Contratos**:
```csharp
public interface IKeywordIntentMatcher
{
    Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default);
}
```

#### 1.3 Hybrid Scoring Engine

**Propósito**: Combinar semantic + keyword + priority para producir ranking final.

**Algoritmo**:
```
FinalScore = (0.7 × SemanticScore) + (0.2 × KeywordScore) + (0.1 × PriorityScore)
```

**Thresholds**:
- **High Confidence**: ≥ 0.90 → Auto-route
- **Medium Confidence**: 0.75 - 0.89 → Auto-route con logging
- **Low Confidence**: 0.50 - 0.74 → Marcar para revisión
- **No Match**: < 0.50 → Fallback queue

**Contratos**:
```csharp
public interface IIntentScoringEngine
{
    Task<IntentClassificationResult> ClassifyAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default);
}

public sealed record IntentClassificationResult
{
    public required string Message { get; init; }
    public required IntentMatch? BestMatch { get; init; }
    public required IReadOnlyList<IntentMatch> AllCandidates { get; init; }
    public required float BestScore { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required string ExplanationJson { get; init; }
}

public enum ConfidenceLevel
{
    NoMatch,
    Low,
    Medium,
    High
}
```

---

### 2. Routing Orchestrator

**Propósito**: Decidir QUÉ workflow disparar y QUÉ agente debe tomar ownership.

**Responsabilidades**:
1. Seleccionar workflow basado en IntentKey
2. Verificar si hay conflictos de agentes activos
3. Aplicar reglas de prioridad
4. Decidir si requiere lock conversacional
5. Ejecutar o encolar el routing

**Reglas de Routing**:
- **1 Agente AI Activo máximo** por conversación
- **Workflows sin agentes AI** pueden correr en paralelo
- **Workflows background** (sin agentes) no bloquean

**Contratos**:
```csharp
public interface IRoutingOrchestrator
{
    Task<RoutingDecision> RouteMessageAsync(
        IntentClassificationResult classification,
        ConversationContext context,
        CancellationToken ct = default);
}

public sealed record RoutingDecision
{
    public required string IntentKey { get; init; }
    public required string? WorkflowDefinitionId { get; init; }
    public required string? TargetAgentId { get; init; }
    public required RoutingAction Action { get; init; }
    public required string ReasonCode { get; init; }
    public required string ExplanationJson { get; init; }
    public required DateTimeOffset DecidedAt { get; init; }
}

public enum RoutingAction
{
    Route,           // Ejecutar workflow
    Queue,           // Encolar para revisión humana
    Reject,          // Rechazar (conflict detectado)
    Fallback         // Enviar a fallback queue
}

public sealed record ConversationContext
{
    public required string ConversationId { get; init; }
    public required string TenantId { get; init; }
    public required string Channel { get; init; }
    public required string UserIdentifier { get; init; }
    public string? CurrentOwnerAgentId { get; init; }
    public bool IsLocked { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
}
```

---

### 3. Ownership Manager

**Propósito**: Garantizar que solo 1 agente AI tenga control activo de una conversación.

**Mecanismo**:
- **Distributed Lock** usando Redis (ya existente en `RedisDistributedLockService`)
- Lock Key: `conversation:lock:{tenantId}:{conversationId}`
- TTL: 5 minutos (renovable)
- Owner metadata: AgentId, WorkflowExecutionId, LockedAt

**Flujo**:
1. Antes de disparar workflow con agente AI → intentar adquirir lock
2. Si lock exitoso → proceder
3. Si lock fallido → verificar si es el mismo agente (idempotencia) o conflicto
4. Si conflicto → marcar mensaje para revisión humana

**Contratos**:
```csharp
public interface IConversationOwnershipManager
{
    Task<OwnershipLock?> TryAcquireLockAsync(
        string tenantId,
        string conversationId,
        string agentId,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<bool> RenewLockAsync(
        string lockId,
        TimeSpan additionalTtl,
        CancellationToken ct = default);

    Task ReleaseLockAsync(string lockId, CancellationToken ct = default);

    Task<ConversationOwnershipState> GetStateAsync(
        string tenantId,
        string conversationId,
        CancellationToken ct = default);
}

public sealed record OwnershipLock
{
    public required string LockId { get; init; }
    public required string ConversationId { get; init; }
    public required string OwnerAgentId { get; init; }
    public required DateTimeOffset AcquiredAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record ConversationOwnershipState
{
    public required string ConversationId { get; init; }
    public required bool IsLocked { get; init; }
    public string? CurrentOwnerAgentId { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
    public string? WorkflowExecutionId { get; init; }
}
```

---

### 4. Fallback Intelligence

**Propósito**: Garantizar que NINGÚN mensaje se pierda, incluso si no hay match.

**Estados de Conversación**:
```csharp
public enum ConversationState
{
    Matched,              // Intent detectado con high confidence
    LowConfidence,        // Intent detectado pero confidence < threshold
    NoMatch,              // No se encontró intent
    Routed,               // Workflow disparado exitosamente
    PendingHumanReview,   // Marcado para revisión humana
    AiResponded,          // AI ya respondió
    HumanResponded,       // Humano ya respondió
    Escalated,            // Escalado a supervisor
    Ignored,              // Marcado como spam/irrelevante
    ConflictDetected      // Conflicto de ownership detectado
}
```

**Inbox Conversacional**:
- Vista centralizada de conversaciones pendientes
- Filtros por estado, confidence, canal, agente
- Acciones: Reasignar intent, Aprobar routing, Responder manualmente

**Contratos**:
```csharp
public interface IConversationInboxService
{
    Task<InboxConversation> CreateOrUpdateAsync(
        InboxConversation conversation,
        CancellationToken ct = default);

    Task<PagedResult<InboxConversation>> GetPendingAsync(
        string tenantId,
        InboxFilter filter,
        CancellationToken ct = default);

    Task<InboxConversation?> GetByIdAsync(
        string tenantId,
        string conversationId,
        CancellationToken ct = default);

    Task<bool> UpdateStateAsync(
        string tenantId,
        string conversationId,
        ConversationState newState,
        string? notes = null,
        CancellationToken ct = default);
}

public sealed record InboxConversation
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string Channel { get; init; }
    public required string UserIdentifier { get; init; }
    public required string LastMessage { get; init; }
    public required ConversationState State { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public string? DetectedIntentKey { get; init; }
    public string? AssignedAgentId { get; init; }
    public string? WorkflowExecutionId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public string? ReviewNotes { get; init; }
}
```

---

### 5. Intent Catalog Manager

**Propósito**: Gestionar catálogo de intenciones oficiales + custom del tenant.

**Catálogo Base** (Built-in Intents):
```yaml
# Archivo: src/AgentFlow.Intents/Catalog/base-intents.yaml
version: "1.0"
intents:
  - key: greeting
    name: "Saludo"
    description: "Cliente inicia conversación con saludo"
    category: "General"
    examples:
      - "Hola"
      - "Buenos días"
      - "Hey"
      - "Qué tal"
    synonyms: ["hi", "hello", "hola", "buenas"]
    confidence_threshold: 0.85
    priority: 100
    
  - key: document_rejected
    name: "Documento Rechazado"
    description: "Cliente consulta por qué su documento fue rechazado"
    category: "Verification"
    examples:
      - "Por qué rechazaron mi documento"
      - "Mi ID no fue aprobada"
      - "Dice que la foto está borrosa"
      - "Why was my document rejected"
    synonyms: ["documento rechazado", "ID rechazado", "foto rechazada"]
    confidence_threshold: 0.90
    priority: 200
    suggested_workflow: "verification.document_review"
    
  - key: payment_status
    name: "Consulta de Pago"
    description: "Cliente pregunta por el estado de un pago"
    category: "Payments"
    examples:
      - "Cuándo se procesará mi pago"
      - "Mi pago no aparece"
      - "Ya pagué pero no se refleja"
    confidence_threshold: 0.88
    priority: 150
    suggested_workflow: "payments.status_check"
    
  # ... más intenciones base
```

**Contratos**:
```csharp
public interface IIntentCatalogService
{
    Task<IReadOnlyList<IntentDefinition>> GetBaseIntentsAsync(CancellationToken ct = default);
    
    Task<IReadOnlyList<IntentDefinition>> GetTenantIntentsAsync(
        string tenantId,
        CancellationToken ct = default);
    
    Task<IntentDefinition> CreateCustomIntentAsync(
        string tenantId,
        IntentDefinition intent,
        CancellationToken ct = default);
    
    Task RebuildVectorIndexAsync(
        string tenantId,
        CancellationToken ct = default);
}

public sealed record IntentDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required IReadOnlyList<string> Examples { get; init; }
    public required IReadOnlyList<string> Synonyms { get; init; }
    public required float ConfidenceThreshold { get; init; }
    public required int Priority { get; init; }
    public string? SuggestedWorkflow { get; init; }
    public required bool IsBaseIntent { get; init; }
    public required int Version { get; init; }
}
```

---

## 🧪 Testing Automatizado & Validación

### Testing Strategy

#### 1. Happy Path Testing

**Objetivo**: Validar que cada intención base funciona correctamente.

**Ubicación**: `tests/AgentFlow.Tests.Integration/IntentRouting/`

**Estructura**:
```csharp
[TestClass]
public class IntentClassificationHappyPathTests
{
    [TestMethod]
    [DataRow("Hola, buenos días", "greeting", 0.90)]
    [DataRow("Por qué rechazaron mi documento", "document_rejected", 0.92)]
    [DataRow("Cuándo se procesará mi pago", "payment_status", 0.88)]
    public async Task ShouldClassifyIntentCorrectly(
        string message,
        string expectedIntent,
        float minConfidence)
    {
        var result = await _classifier.ClassifyAsync(message, _tenantId);
        
        Assert.IsNotNull(result.BestMatch);
        Assert.AreEqual(expectedIntent, result.BestMatch.IntentKey);
        Assert.IsTrue(result.BestScore >= minConfidence);
        Assert.AreEqual(ConfidenceLevel.High, result.Confidence);
    }
}
```

#### 2. Regression Testing

**Objetivo**: Garantizar que cambios no degraden precisión.

**Mecanismo**:
1. Ejecutar suite completa en cada cambio
2. Comparar métricas contra baseline
3. Fallar build si accuracy < 99%

**Pipeline**:
```yaml
# scripts/quality/intent-routing-regression.yml
name: Intent Routing Regression
on: [pull_request]
jobs:
  regression:
    runs-on: ubuntu-latest
    steps:
      - name: Run Intent Classification Tests
        run: dotnet test --filter Category=IntentRouting
      - name: Validate Accuracy Benchmark
        run: |
          accuracy=$(dotnet test --logger "json" | jq '.accuracy')
          if (( $(echo "$accuracy < 0.99" | bc -l) )); then
            echo "Accuracy degradation detected: $accuracy < 0.99"
            exit 1
          fi
```

#### 3. Routing Validation Suite

**Objetivo**: Validar cadena completa: Intent → Workflow → Agent → Ownership

**Tests**:
```csharp
[TestClass]
public class RoutingOrchestrationTests
{
    [TestMethod]
    public async Task ShouldRouteToCorrectWorkflow()
    {
        var message = "Quiero solicitar un préstamo";
        var result = await _orchestrator.RouteMessageAsync(message, _context);
        
        Assert.AreEqual("loan.application.started", result.WorkflowDefinitionId);
        Assert.AreEqual(RoutingAction.Route, result.Action);
    }
    
    [TestMethod]
    public async Task ShouldPreventAgentCollision()
    {
        // Agent A ya tiene lock
        await _ownership.TryAcquireLockAsync(_tenantId, _convId, "agent-a", TimeSpan.FromMinutes(5));
        
        // Agent B intenta routing
        var result = await _orchestrator.RouteMessageAsync(message, _context);
        
        Assert.AreEqual(RoutingAction.Queue, result.Action);
        Assert.AreEqual("agent_conflict", result.ReasonCode);
    }
    
    [TestMethod]
    public async Task ShouldFallbackOnNoMatch()
    {
        var message = "asdfghjkl random nonsense";
        var result = await _orchestrator.RouteMessageAsync(message, _context);
        
        Assert.AreEqual(RoutingAction.Fallback, result.Action);
        Assert.IsTrue(result.RequiresHumanReview);
    }
}
```

#### 4. Benchmarks Continuos

**Objetivo**: Medir y alertar sobre degradación de precisión.

**Métricas**:
- Accuracy: TP / (TP + FP + FN)
- Precision: TP / (TP + FP)
- Recall: TP / (TP + FN)
- F1 Score: 2 × (Precision × Recall) / (Precision + Recall)

**Umbral mínimo**:
- Accuracy: ≥ 99%
- False Positive Rate: < 1%
- Agent Collision Rate: 0%
- Unanswered Conversations: 0%

**Almacenamiento**:
```csharp
public sealed record IntentRoutingMetrics
{
    public required string TenantId { get; init; }
    public required DateTimeOffset MeasuredAt { get; init; }
    public required int TotalMessages { get; init; }
    public required int TruePositives { get; init; }
    public required int FalsePositives { get; init; }
    public required int FalseNegatives { get; init; }
    public required int TrueNegatives { get; init; }
    public required float Accuracy { get; init; }
    public required float Precision { get; init; }
    public required float Recall { get; init; }
    public required float F1Score { get; init; }
    public required int AgentCollisions { get; init; }
    public required int UnansweredConversations { get; init; }
}
```

---

## 🎨 Frontend (UI/UX)

### 1. Pantalla Principal: Intent Management

**Ruta**: `/dashboard/intents`

**Componentes**:
- **Intent List Table**:
  - Columnas: Name, Key, Category, Confidence Threshold, Avg Success Rate, Workflows, Status
  - Filtros: Category, Enabled/Disabled, Base/Custom
  - Búsqueda: por nombre, key, ejemplos
  - Acciones: Editar, Deshabilitar, Eliminar (solo custom)

- **Create/Edit Intent Dialog**:
  - Formulario completo de IntentDefinition
  - Sugerencias AI de ejemplos
  - Detección de duplicados
  - Preview de scoring

### 2. Playground / Testing Lab

**Ruta**: `/dashboard/intents/playground`

**Funcionalidad**:
- Input de mensaje de prueba
- Clasificación en vivo
- Visualización de:
  - Best match con confidence score
  - Top-5 candidatos con ranking
  - Explicación de por qué cada match fue seleccionado
  - Workflow que se dispararía
  - Agente que tomaría ownership
- Botón "Run Full Simulation" (simula routing completo sin ejecutar)

**UI**:
```tsx
<PlaygroundPanel>
  <MessageInput 
    value={testMessage} 
    onChange={setTestMessage}
    placeholder="Escribe un mensaje de prueba..."
  />
  <Button onClick={handleClassify}>Clasificar</Button>
  
  {result && (
    <ResultPanel>
      <BestMatch 
        intent={result.bestMatch}
        confidence={result.bestScore}
        level={result.confidence}
      />
      <CandidatesList candidates={result.allCandidates} />
      <ExplanationCard json={result.explanationJson} />
      <RoutingPreview 
        workflow={result.suggestedWorkflow}
        agent={result.targetAgent}
      />
    </ResultPanel>
  )}
</PlaygroundPanel>
```

### 3. AI Assistant para Creación de Intenciones

**Funcionalidad**:
- Chat interface para crear intenciones guiadas por AI
- Prompt: "Describe la intención que quieres crear"
- AI sugiere:
  - Name, Key, Description
  - Ejemplos de frases
  - Sinónimos
  - Threshold recomendado
  - Workflows relacionados
- Usuario valida y ajusta
- Detección automática de overlap con intenciones existentes

**Implementación**:
- Herramienta MCP: `af_suggest_intent`
- Backend: `IIntentSuggestionService` usando LLM

### 4. Inbox Conversacional

**Ruta**: `/dashboard/inbox`

**Vista**:
- Tabla de conversaciones pendientes
- Columnas: User, Channel, Last Message, State, Confidence, Intent, Time, Actions
- Filtros: State, Confidence Level, Channel, Agent
- Estados visuales:
  - 🔴 No Match
  - 🟡 Low Confidence
  - 🟢 Matched
  - 👤 Pending Human Review
  - ⚠️ Conflict Detected

**Acciones por conversación**:
- View Full Thread
- Reassign Intent
- Approve Routing
- Respond Manually
- Mark as Resolved

---

## 📈 Observabilidad & Métricas

### Dashboard Operacional

**Métricas en tiempo real**:
1. **Classification Performance**:
   - Accuracy (último día, semana, mes)
   - False Positive Rate
   - False Negative Rate
   - Avg Confidence Score

2. **Routing Health**:
   - Mensajes procesados / hora
   - Mensajes encolados para revisión
   - Agent conflicts detectados
   - Fallback rate

3. **Conversation States**:
   - Matched: X
   - Low Confidence: Y
   - No Match: Z
   - Pending Human Review: W
   - Resolved: R

4. **Agent Ownership**:
   - Conversaciones activas por agente
   - Avg lock duration
   - Handoffs realizados
   - Timeouts/releases

### Alertas

**Condiciones de alerta**:
- Accuracy < 95% (Warning)
- Accuracy < 90% (Critical)
- False Positive Rate > 2% (Warning)
- Agent Collision Rate > 0% (Critical)
- Unanswered Conversations > 10 (Warning)
- Fallback Rate > 10% (Warning)

**Canales**:
- Observability dashboard
- Slack/Teams notification
- Email alert
- PagerDuty (para critical)

---

## 🚀 Plan de Implementación por Fases

### Fase 1: Foundation (Semana 1-2) — CORE ENGINE

**Objetivo**: Implementar motor de clasificación y ownership.

**Entregables**:
1. ✅ `ISemanticIntentMatcher` + implementación con Qdrant
2. ✅ `IKeywordIntentMatcher` + implementación
3. ✅ `IIntentScoringEngine` + hybrid scoring
4. ✅ `IConversationOwnershipManager` + Redis locking
5. ✅ Tests unitarios de cada componente

**Agentes responsables**:
- **core-engine**: Clasificación y scoring
- **data-expert**: Integración con Qdrant y Redis

---

### Fase 2: Routing & Fallback (Semana 3-4) — ORCHESTRATION

**Objetivo**: Implementar orchestrator y fallback intelligence.

**Entregables**:
1. ✅ `IRoutingOrchestrator` + reglas de ownership
2. ✅ `IConversationInboxService` + estados
3. ✅ API endpoints para Inbox
4. ✅ Integración con AgentExecutionEngine
5. ✅ Tests de routing completo

**Agentes responsables**:
- **core-engine**: Orchestrator
- **data-expert**: Inbox persistence

---

### Fase 3: Intent Catalog (Semana 5) — BASE INTENTS

**Objetivo**: Crear catálogo base de intenciones preconfiguradas.

**Entregables**:
1. ✅ Archivo `base-intents.yaml` con 20+ intenciones oficiales
2. ✅ `IIntentCatalogService` + carga de catálogo
3. ✅ Bootstrap automático en startup
4. ✅ Vector indexing de intenciones base
5. ✅ Documentación de cada intención

**Agentes responsables**:
- **orchestrator**: Diseño del catálogo
- **data-expert**: Vector indexing

---

### Fase 4: Frontend MVP (Semana 6-7) — UI/UX

**Objetivo**: Pantalla de intenciones + Playground.

**Entregables**:
1. ✅ Intent Management Page (lista, CRUD)
2. ✅ Intent Playground (testing en vivo)
3. ✅ Inbox Conversacional (vista básica)
4. ✅ Integración con API de intenciones
5. ✅ Componentes MUI v6

**Agentes responsables**:
- **frontend**: Implementación completa

---

### Fase 5: Testing & Validation (Semana 8) — QUALITY

**Objetivo**: Suite de testing automatizado.

**Entregables**:
1. ✅ Happy path tests (20+ casos)
2. ✅ Regression tests
3. ✅ Routing validation suite
4. ✅ Benchmarks continuos
5. ✅ CI/CD integration

**Agentes responsables**:
- **evaluation**: Diseño e implementación

---

### Fase 6: AI Assistant & Advanced Features (Semana 9-10) — INTELLIGENCE

**Objetivo**: Features avanzadas + AI Assistant.

**Entregables**:
1. ✅ AI Assistant para creación de intenciones
2. ✅ Auto-detection de duplicados
3. ✅ Sugerencias de mejora
4. ✅ Learning continuo (feedback loop)
5. ✅ Metrics dashboard

**Agentes responsables**:
- **frontend**: UI del Assistant
- **core-engine**: Lógica de sugerencias
- **evaluation**: Learning pipeline

---

### Fase 7: Observability & Production Hardening (Semana 11-12) — OPS

**Objetivo**: Dashboard operacional + alertas + documentación.

**Entregables**:
1. ✅ Metrics dashboard completo
2. ✅ Alerting configurado
3. ✅ Runbooks operacionales
4. ✅ Documentación de usuario final
5. ✅ Capacity planning

**Agentes responsables**:
- **orchestrator**: Coordinación general
- **frontend**: Dashboard
- **governance-security**: Auditoría y compliance

---

## 🔐 Consideraciones de Seguridad & Governance

### 1. Policy Enforcement

El Router debe respetar todas las políticas del tenant:
- PII Redaction en intenciones
- Rate limiting por usuario
- Blacklist de intenciones prohibidas
- Approval workflow para intenciones custom

### 2. Auditoría Inmutable

Cada decisión de routing debe quedar registrada:
- Intent detectado
- Confidence score
- Workflow disparado
- Agente asignado
- Ownership lock ID
- Razón de la decisión

### 3. Multi-Tenant Isolation

- Intenciones custom son privadas por tenant
- Vector embeddings separados por tenant
- Redis locks incluyen tenantId en key
- Inbox es filtrado estrictamente por tenant

---

## 📚 Referencias Técnicas

### Documentos Relacionados

- [Channel Gateway Architecture](./CHANNEL-GATEWAY-ARCHITECTURE.md)
- [Unicorn Strategy](./UNICORN-STRATEGY.md)
- [Product Architecture](./PRODUCT-ARCHITECTURE.md)
- [MongoDB Data Model](./mongodb-data-model.md)

### Tecnologías Utilizadas

- **Vector Store**: Qdrant (ya integrado)
- **Distributed Lock**: Redis (ya integrado)
- **Embeddings**: OpenAI `text-embedding-3-small` (vía ModelRouting)
- **LLM para Clasificación**: GPT-4o (Router Agent)
- **Persistence**: MongoDB (ya integrado)

---

## ✅ Criterios de Éxito

### Objetivos Cuantitativos

- ✅ **Accuracy ≥ 99%** en clasificación de intenciones base
- ✅ **False Positive Rate < 1%**
- ✅ **Agent Collision Rate = 0%** (cero conflictos de ownership)
- ✅ **Unanswered Conversations = 0%** (fallback garantizado)
- ✅ **Response Time < 500ms** para clasificación
- ✅ **Test Coverage ≥ 90%** en módulos core

### Objetivos Cualitativos

- ✅ **Enterprise-Grade Reliability**: Nunca perder conversaciones
- ✅ **Full Observability**: Trazabilidad completa de decisiones
- ✅ **Developer Experience**: Testing fácil con Playground
- ✅ **Operator Experience**: Inbox intuitivo para HITL
- ✅ **Business Confidence**: Benchmarks publicados y validados

---

## 🎯 Conclusión

Este rediseño transforma el módulo de Intenciones y Routing de un CRUD básico a un **AI Traffic Controller** de nivel empresarial, cumpliendo con los estándares de la **Unicorn Strategy** de AgentFlow.

**Principios Rectores**:
1. **Seguridad Operacional > Automatización**: Nunca responder incorrectamente
2. **Ownership Estricto**: 1 agente AI activo por conversación
3. **Fallback Garantizado**: 0 conversaciones perdidas
4. **Testing Continuo**: Validación automática de precisión
5. **Observabilidad Total**: Explicabilidad de cada decisión

Este módulo es **crítico para el negocio** y debe implementarse con rigor enterprise-grade.

---

**Next Steps**: Ejecutar Fase 1 con `core-engine` y `data-expert` agents.
