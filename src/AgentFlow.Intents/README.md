# AgentFlow.Intents

> **Intent Routing & Semantic Classification Engine**  
> Enterprise-grade AI-powered intent matching and conversational traffic control for AgentFlow.

## 📋 Overview

This module provides the **Semantic Intent Matcher** — a critical component for intelligent routing of customer messages to the appropriate agent workflows. It uses vector embeddings and Qdrant vector database for high-precision intent classification.

## 🎯 Key Features

- **Hybrid Scoring Engine**: Combines semantic + keyword + priority for final intent classification
- **Semantic Matching**: Vector similarity search using embeddings (OpenAI, Azure, or local models)
- **Keyword Matching**: Fast deterministic matching using exact phrases, n-grams, and synonyms
- **Confidence Levels**: High/Medium/Low/NoMatch with automatic human review flags
- **Multi-tenant Isolation**: Per-tenant collections with strict security boundaries
- **Channel Filtering**: Route intents based on channel (WhatsApp, Web, etc.)
- **Audit-Ready**: Full JSON explanation of every decision for compliance
- **Enterprise-Grade**: Thread-safe, scalable, and production-ready

## 🏗️ Architecture

### Components Implemented

1. **IIntentScoringEngine** - Hybrid scoring engine (semantic + keyword + priority)
2. **IntentScoringEngine** - Production implementation with audit trail
3. **ISemanticIntentMatcher** - Interface for semantic intent classification
4. **QdrantSemanticIntentMatcher** - Qdrant-backed implementation
5. **IKeywordIntentMatcher** - Interface for keyword-based intent classification
6. **KeywordIntentMatcher** - Deterministic rule-based implementation
7. **IRoutingOrchestrator** - Core routing decision component
8. **RoutingOrchestrator** - Coordinates classification, ownership, and routing decisions
9. **IConversationOwnershipManager** - Enforces single-agent-per-conversation rule
10. **ConversationOwnershipManager** - Redis-backed distributed lock implementation
11. **IIntentCatalogService** - Manages base intent catalog
12. **IntentVectorIndexer** - Indexes intents into Qdrant with embeddings
13. **IntentBootstrapService** - Automatic startup loading of base intents (30+)
14. **IEmbeddingGenerator** - Abstraction for embedding models (implementation pending)
15. **IntentMatch** - Result model with similarity scoring and routing metadata
16. **IntentClassificationResult** - Final classification result with confidence and explanation
17. **RoutingDecision** - Final routing decision with action and audit metadata
18. **ConfidenceLevel** - Enum for High/Medium/Low/NoMatch confidence

### Data Flow

```
User Message
    ↓
[Hybrid Scoring Engine] ← IIntentScoringEngine
    ↓
    ├─→ [Semantic Matcher] → Qdrant Vector Search → Top 10 Candidates (70% weight)
    │
    └─→ [Keyword Matcher] → Rule-based Matching → All Candidates (20% weight)
    ↓
[Combine Scores by IntentKey]
    ↓
[Add Priority Score] (10% weight)
    ↓
FinalScore = 0.7×Semantic + 0.2×Keyword + 0.1×Priority
    ↓
[Determine Confidence Level]
    ↓
IntentClassificationResult
    ↓
[Routing Orchestrator] ← IRoutingOrchestrator
    ↓
    ├─→ Validate Confidence
    ├─→ Check Workflow Configuration
    ├─→ Verify Ownership State
    └─→ Acquire Lock (if needed)
    ↓
RoutingDecision
├── Action: Route | Queue | Reject | Fallback
├── WorkflowDefinitionId
├── TargetAgentId
├── ReasonCode
├── ExplanationJson
└── LockId (if acquired)
```

## 🚦 Routing Orchestrator

The **Routing Orchestrator** is the core decision-making component that coordinates message routing based on intent classification and conversation ownership.

### Routing Actions

1. **Route** - Execute workflow immediately (high/medium confidence + lock acquired)
2. **Queue** - Enqueue for human review (low confidence or no workflow)
3. **Reject** - Reject due to agent conflict (another agent owns conversation)
4. **Fallback** - Send to fallback handler (no viable match)

### Decision Matrix

| Scenario | Action | Next Step |
|----------|--------|-----------|
| High/Medium confidence + workflow + lock acquired | **Route** | Trigger workflow execution |
| Low confidence (0.50-0.74) | **Queue** | Human review required |
| No workflow configured | **Queue** | Configuration needed |
| Another agent owns conversation | **Reject** | Conflict detected |
| Lock acquisition failed | **Reject** | Cannot acquire ownership |
| No match (< 0.50) | **Fallback** | Default handler |

### Components Implemented

1. **IIntentScoringEngine** - Hybrid scoring engine (semantic + keyword + priority)
2. **IntentScoringEngine** - Production implementation with audit trail
3. **ISemanticIntentMatcher** - Interface for semantic intent classification
4. **QdrantSemanticIntentMatcher** - Qdrant-backed implementation
5. **IKeywordIntentMatcher** - Interface for keyword-based intent classification
6. **KeywordIntentMatcher** - Deterministic rule-based implementation
7. **IRoutingOrchestrator** - Core routing decision component
8. **RoutingOrchestrator** - Coordinates classification, ownership, and routing decisions
9. **IConversationOwnershipManager** - Enforces single-agent-per-conversation rule
10. **ConversationOwnershipManager** - Redis-backed distributed lock implementation
11. **IConversationInboxService** - Manages conversations requiring human review
12. **ConversationInboxService** - MongoDB-backed inbox for low confidence/no match conversations
13. **IIntentCatalogService** - Manages base intent catalog
14. **IntentVectorIndexer** - Indexes intents into Qdrant with embeddings
15. **IntentBootstrapService** - Automatic startup loading of base intents (30+)
16. **IEmbeddingGenerator** - Abstraction for embedding models (implementation pending)
17. **IntentMatch** - Result model with similarity scoring and routing metadata
18. **IntentClassificationResult** - Final classification result with confidence and explanation
19. **RoutingDecision** - Final routing decision with action and audit metadata
20. **ConfidenceLevel** - Enum for High/Medium/Low/NoMatch confidence
21. **ConversationState** - Enum for inbox conversation lifecycle states

### Data Flow

```
User Message
    ↓
[Hybrid Scoring Engine] ← IIntentScoringEngine
    ↓
    ├─→ [Semantic Matcher] → Qdrant Vector Search → Top 10 Candidates (70% weight)
    │
    └─→ [Keyword Matcher] → Rule-based Matching → All Candidates (20% weight)
    ↓
[Combine Scores by IntentKey]
    ↓
[Add Priority Score] (10% weight)
    ↓
FinalScore = 0.7×Semantic + 0.2×Keyword + 0.1×Priority
    ↓
[Determine Confidence Level]
    ↓
IntentClassificationResult
├── BestMatch (IntentMatch)
// Use the extension method to register all services
services.AddIntentRouting();

// Also register the embedding generator (implementation required)
services.AddSingleton<IEmbeddingGenerator, YourEmbeddingImplementation>();
```

This registers:
- `IIntentScoringEngine` → `IntentScoringEngine` (Hybrid scoring)
- `ISemanticIntentMatcher` → `QdrantSemanticIntentMatcher`
- `IKeywordIntentMatcher` → `KeywordIntentMatcher`

### 2. Hybrid Classification (Recommended)

**This is the primary way to classify intents in production:**

```csharp
var scoringEngine = serviceProvider.GetRequiredService<IIntentScoringEngine>();

var result = await scoringEngine.ClassifyAsync(
    message: "Quiero solicitar un préstamo personal",
    tenantId: "banco-xyz",
    channel: "whatsapp"
);

// Check the result
Console.WriteLine($"Message: {result.Message}");
Console.WriteLine($"Best Intent: {result.BestMatch?.IntentKey ?? "NO MATCH"}");
Console.WriteLine($"Confidence: {result.Confidence}"); // High, Medium, Low, NoMatch
Console.WriteLine($"Score: {result.BestScore:P}"); // e.g., "92.5%"
Console.WriteLine($"Requires Human Review: {result.RequiresHumanReview}");
Console.WriteLine();

// Show all candidates
Console.WriteLine("All Candidates:");
foreach (var candidate in result.AllCandidates)
{
    Console.WriteLine($"  - {candidate.IntentKey}: {candidate.SimilarityScore:F3}");
}

// F4. Keyword Intent Matching (Low-Level)
Console.WriteLine("\nExplanation (Audit Trail):");
Console.WriteLine(result.ExplanationJson);

// Example Output:
// {
//   "message": "Quiero solicitar un préstamo personal",
//   "best_match": {
//     "intent_key": "loan_application",
//     "final_score": 0.92,
//     "semantic_score": 0.95,
//     "keyword_score": 0.80,
//     "priority_score": 0.50,
//     "confidence": "High",
//     "matched_via": ["semantic", "keyword"]
//   },
//   "all_candidates": [
//     { "intent_key": "loan_application", "score": 0.92 },
//     { "intent_key": "product_inquiry", "score": 0.65 }
//   ],
//   "decision": "auto_route",
//   "requires_review": false,
//   "timestamp": "2026-05-18T10:30:45.123Z"
// }

// Make routing decision
if (result.Confidence >= ConfidenceLevel.Medium)
{
    // Auto-route with confidence
    var targetAgent = result.BestMatch!.Rule.TargetAgentId;
    var workflowId = result.BestMatch.Rule.WorkflowDefinitionId;
    
    Console.WriteLine($"✅ Auto-routing to: {targetAgent}");
    if (workflowId != null)
    {
        Console.WriteLine($"   Triggering workflow: {workflowId}");
    }
}
else
{
    // Requires human review
    Console.WriteLine("⚠️ Low confidence - routing to human agent for review");
}
```

### 3. Full Routing Pipeline (Recommended for Production)

**This is the end-to-end routing flow with ownership management:**

```csharp
var scoringEngine = serviceProvider.GetRequiredService<IIntentScoringEngine>();
var orchestrator = serviceProvider.GetRequiredService<IRoutingOrchestrator>();

// Step 1: Classify the message
var classification = await scoringEngine.ClassifyAsync(
    message: "Quiero solicitar un préstamo personal",
    tenantId: "banco-xyz",
    channel: "whatsapp"
);

Console.WriteLine($"Classified as: {classification.BestMatch?.IntentKey ?? "NO MATCH"}");
Console.WriteLine($"Confidence: {classification.Confidence} ({classification.BestScore:P})");

// Step 2: Make routing decision with ownership validation
var decision = await orchestrator.RouteMessageAsync(
    classification,
    new ConversationContext
    {
        ConversationId = "conv-456",
        TenantId = "banco-xyz",
        Channel = "whatsapp",
        UserIdentifier = "+50581143874"
    }
);

Console.WriteLine($"\nRouting Decision: {decision.Action}");
Console.WriteLine($"Reason: {decision.ReasonCode}");
Console.WriteLine($"Explanation:\n{decision.ExplanationJson}");

// Step 3: Act on the decision
switch (decision.Action)
{
    case RoutingAction.Route:
        // High confidence - execute workflow
        Console.WriteLine($"✅ Executing workflow: {decision.WorkflowDefinitionId}");
        Console.WriteLine($"   Agent: {decision.TargetAgentId}");
        Console.WriteLine($"   Lock acquired: {decision.LockId}");
        
        try
        {
            // Trigger workflow execution
            await workflowEngine.ExecuteAsync(
                decision.WorkflowDefinitionId,
                new ExecutionContext
                {
                    ConversationId = "conv-456",
                    TenantId = "banco-xyz",
                    InitialMessage = classification.Message
                }
            );
        }
        finally
        {
            // Always release lock
            if (decision.LockId != null)
            {
                await ownershipManager.ReleaseLockAsync(decision.LockId);
            }
        }
        break;

    case RoutingAction.Queue:
        // Low confidence or no workflow - human review
        Console.WriteLine($"⚠️ Queuing for human review");
        Console.WriteLine($"   Reason: {decision.ReasonCode}");
        await humanReviewQueue.EnqueueAsync(decision);
        break;

    case RoutingAction.Reject:
        // Agent conflict detected
        Console.WriteLine($"❌ Routing rejected: {decision.ReasonCode}");
        Console.WriteLine($"   Another agent owns this conversation");
        // Return conflict response to user
        return Results.Conflict(decision.ExplanationJson);

    case RoutingAction.Fallback:
        // No match - send to default handler
        Console.WriteLine($"🔄 No match found - using fallback");
        await fallbackHandler.HandleAsync(classification.Message);
        break;
}
```

### 4. Semantic Intent Matching (Low-Level)

// Make routing decision
if (result.Confidence >= ConfidenceLevel.Medium)
{
    // Auto-route with confidence
    var targetAgent = result.BestMatch!.Rule.TargetAgentId;
    var workflowId = result.BestMatch.Rule.WorkflowDefinitionId;
    
    Console.WriteLine($"✅ Auto-routing to: {targetAgent}");
    if (workflowId != null)
    {
        Console.WriteLine($"   Triggering workflow: {workflowId}");
    }
}
else
{
    // Requires human review
    Console.WriteLine("⚠️ Low confidence - routing to human agent for review");
}
```

### 3. Semantic Intent Matching (Low-Level)

## 🔧 Usage

### 1. Register Services

```csharp
services.AddSingleton<ISemanticIntentMatcher, QdrantSemanticIntentMatcher>();
services.AddSingleton<IKeywordIntentMatcher, KeywordIntentMatcher>();
services.AddSingleton<IEmbeddingGenerator, YourEmbeddingImplementation>(); // To be implemented
```

### 2. Semantic Intent Matching

```csharp
var semanticMatcher = serviceProvider.GetRequiredService<ISemanticIntentMatcher>();

var candidates = await semanticMatcher.FindCandidatesAsync(
    message: "I need to apply for a personal loan",
    tenantId: "bank-abc",
    channel: "whatsapp",
    topK: 5
);

foreach (var match in candidates)
{
    Console.WriteLine($"Intent: {match.IntentKey}");
    Console.WriteLine($"Score: {match.SimilarityScore:F3}");
    Console.WriteLine($"Matched Via: {match.MatchedVia}"); // "semantic"
    Console.WriteLine($"Target Agent: {match.Rule.TargetAgentId}");
    Console.WriteLine($"Workflow: {match.Rule.WorkflowName ?? "N/A"}");
    Console.WriteLine();
}
```

### 3. Keyword Intent Matching

```csharp
var keywordMatcher = serviceProvider.GetRequiredService<IKeywordIntentMatcher>();

var keywordCandidates = await keywordMatcher.FindCandidatesAsync(
    message: "Quiero solicitar un préstamo personal",
    tenantId: "banco-xyz",
    channel: "whatsapp"
);

// Keyword matcher returns ALL candidates with score > 0, ordered by score
var topKeywordMatch = keywordCandidates.FirstOrDefault();
if (topKeywordMatch != null)
{
    Console.WriteLine($"Keyword Match: {topKeywordMatch.IntentKey}");
    Console.WriteLine($"Score: {topKeywordMatch.SimilarityScore:F3}");
    Console.WriteLine($"Matched Via: {topKeywordMatch.MatchedVia}"); // "keyword"
}
```

### 4. Hybrid Matching (Recommended)

⚠️ **DEPRECATED**: Use `IIntentScoringEngine.ClassifyAsync()` instead (see section 2 above).

This low-level approach is shown for reference only:

Combine both matchers for maximum precision:

```csharp
// Get candidates from both matchers
var semanticTask = semanticMatcher.FindCandidatesAsync(message, tenantId, channel);
var keywordTask = keywordMatcher.FindCandidatesAsync(message, tenantId, channel);

await Task.WhenAll(semanticTask, keywordTask);

var semanticMatches = await semanticTask;
var keywordMatches = await keywordTask;

// Merge and boost scores when both matchers agree
var allCandidates = semanticMatches
    .Concat(keywordMatches)
    .GroupBy(m => m.IntentKey)
    .Select(g => new
    {
        IntentKey = g.Key,
        BoostedScore = g.Sum(m => m.SimilarityScore) / 2, // Average boost
        MatchedVia = string.Join("+", g.Select(m => m.MatchedVia).Distinct()),
        Rule = g.First().Rule
    })
    .OrderByDescending(c => c.BoostedScore)
    .ToList();

var bestMatch = allCandidates.FirstOrDefault();
Console.WriteLine($"Best Match: {bestMatch?.IntentKey}");
Console.WriteLine($"Boosted Score: {bestMatch?.BoostedScore:F3}");
Console.WriteLine($"Matched Via: {bestMatch?.MatchedVia}"); // e.g., "semantic+keyword"
```

### 2. Find Intent Candidates

```csharp
var matcher = serviceProvider.GetRequiredService<ISemanticIntentMatcher>();

var candidates = await matcher.FindCandidatesAsync(
    message: "I need to apply for a personal loan",
    tenantId: "bank-abc",
    channel: "whatsapp",
    topK: 5
);

foreach (var match in candidates)
{
    Console.WriteLine($"Intent: {match.IntentKey}");
    Console.WriteLine($"Score: {match.SimilarityScore:F3}");
    Console.WriteLine($"Target Agent: {match.Rule.TargetAgentId}");
    Console.WriteLine($"Workflow: {match.Rule.WorkflowName ?? "N/A"}");
    Console.WriteLine();
}
```

## 📊 Confidence Thresholds

| Confidence Level | Score Range | Action | Human Review |
|------------------|-------------|--------|--------------|
| **High** | ≥ 0.90 | Auto-route immediately | ❌ No |
| **Medium** | 0.75 - 0.89 | Auto-route with logging | ❌ No |
| **Low** | 0.50 - 0.74 | Flag for review before routing | ✅ Yes |
| **NoMatch** | < 0.50 | Route to fallback handler | ✅ Yes |

### Hybrid Scoring Formula

```
FinalScore = (0.7 × SemanticScore) + (0.2 × KeywordScore) + (0.1 × PriorityScore)
```

**Where:**
- **SemanticScore**: Vector similarity from embedding search (0.0 - 1.0)
- **KeywordScore**: Deterministic rule matching score (0.0 - 1.0)
- **PriorityScore**: Normalized priority value (Priority / 1000, capped at 1.0)

**Example:**
```
Semantic: 0.95 (high similarity)
Keyword:  0.80 (strong phrase match)
Priority: 500 → 0.50

FinalScore = (0.7 × 0.95) + (0.2 × 0.80) + (0.1 × 0.50)
           = 0.665 + 0.160 + 0.050
           = 0.875 (Medium Confidence)
```

## 🗂️ Collection Structure

### Qdrant Collection Naming
- **Format**: `intents_{tenantId}`
- **Example**: `intents_bank-abc`

### Vector Metadata Schema

```json
{
  "intent_key": "loan_application",
  "tenant_id": "bank-abc",
  "channel": "whatsapp",
  "enabled": true,
  "rule_json": "{...full IntentRoutingRule...}"
}
```

## 🔐 Security & Compliance

- **Multi-tenancy**: Strict tenant filtering (no cross-tenant leaks)
- **Audit Trail**: Every match decision is logged with rationale
- **Collection Isolation**: Each tenant has isolated vector collections
- **Safety Checks**: 
  - Tenant ID validation on every search
  - Channel filtering to prevent cross-channel routing
  - Enabled flag enforcement

## 📦 Dependencies

- `AgentFlow.Abstractions` - Core contracts
- `AgentFlow.Application` - `IVectorMemory` interface
- `AgentFlow.Security` - `IntentRoutingRule` model

## 🎲 Keyword Matching Algorithm

The `KeywordIntentMatcher` uses a multi-criteria scoring algorithm:

### Scoring Components

1. **Exact Match** (weight: 0.3)
   - Checks if the message contains any complete example phrase
   - Case-insensitive comparison
   - Example: "préstamo personal" in message → +0.3

2. **N-gram Overlap** (weight: 0.5)
   - Tokenizes both message and example phrases
   - Calculates intersection of tokens
   - Formula: `overlap_count / max(message_tokens, example_tokens)`
   - Removes stopwords and short tokens (< 3 chars)
   - Example: ["quiero", "préstamo"] ∩ ["solicitar", "préstamo", "personal"] → 1/3 = 0.167 × 0.5 = 0.083

3. **Synonym Match** (weight: 0.2)
   - Checks if intent description contains tokens from the message
   - Simplified synonym detection (can be enhanced with external dictionary)
   - Example: "crédito" in description, "préstamo" in message → +0.2 (if configured)

### Final Score

```
FinalScore = min(ExactMatch + NgramOverlap + SynonymMatch, 1.0)
```

### Tokenization Rules

- Lowercase normalization
- Split by whitespace and punctuation (regex: `[^\w]+`)
- Filter tokens with length < 3 characters
- Remove stopwords (Spanish + English: "el", "la", "de", "the", "is", etc.)

### Performance

- Target: < 100ms for 100 rules
- Optimized for deterministic, in-memory matching
- No external service calls

## 🚧 Pending Implementation

- [ ] `IEmbeddingGenerator` concrete implementations (OpenAI, Azure, local)
- [ ] Intent indexing pipeline (for populating Qdrant collections)
- [ ] Batch matching for performance optimization
- [ ] Advanced synonym dictionary for KeywordIntentMatcher
- [ ] Unit tests for both matchers
- [ ] Integration tests with real Qdrant instance
- [ ] Benchmark suite for accuracy and performance

## 📝 Example Scenario

```csharp
// Customer message arrives
var message = "Quiero solicitar un crédito hipotecario";

// Find matching intents
var matches = await matcher.FindCandidatesAsync(message, "banco-xyz", "whatsapp", topK: 3);

// Best match
var bestMatch = matches.FirstOrDefault();
if (bestMatch != null && bestMatch.SimilarityScore >= 0.75)
{
    // Trigger workflow
    await workflowEngine.TriggerAsync(
        workflowId: bestMatch.Rule.WorkflowDefinitionId,
        tenantId: "banco-xyz",
        initialMessage: message
    );
    
    _logger.LogInformation(
        "Intent matched: {IntentKey} with confidence {Score:F3}. Triggered workflow {WorkflowName}",
        bestMatch.IntentKey,
        bestMatch.SimilarityScore,
        bestMatch.Rule.WorkflowName
    );
}
else
{
    // Fallback to default router
    _logger.LogWarning("No high-confidence intent match. Using fallback router.");
}
```

## 🔗 Related Documentation

- [Intent Routing Architecture](../../docs/INTENT-ROUTING-ARCHITECTURE.md)
- [Implementation Plan](../../docs/INTENT-ROUTING-IMPLEMENTATION-PLAN.md)
- [Quickstart Guide](../../docs/INTENT-ROUTING-QUICKSTART.md)

## 📄 License

Proprietary - AgentFlow Platform — Intent Routing & Classification Module

> **Enterprise-Grade AI Traffic Controller**  
> **Version**: 1.0  
> **Status**: 🚧 Under Development

---

## 📖 Overview

El módulo **AgentFlow.Intents** implementa el **Intent Routing & Intelligent Traffic Controller** de la plataforma AgentFlow. Este componente es **crítico** para el funcionamiento completo del sistema, ya que coordina:

- 🎯 Clasificación inteligente de intenciones (semantic + keyword + hybrid scoring)
- 🚦 Routing determinístico hacia workflows y agentes
- 🔒 Ownership conversacional (prevención de colisiones entre agentes AI)
- 📥 Fallback intelligence (0 conversaciones perdidas)
- 📊 Observabilidad completa de decisiones de routing

---

## 🏗️ Architecture

### Components

```
AgentFlow.Intents/
├── Classification/
│   ├── ISemanticIntentMatcher.cs          # Semantic matching con embeddings
│   ├── QdrantSemanticIntentMatcher.cs     # Implementación con Qdrant
│   ├── IKeywordIntentMatcher.cs           # Keyword/regex matching
│   ├── KeywordIntentMatcher.cs            # Implementación determinística
│   ├── IIntentScoringEngine.cs            # Hybrid scoring engine
│   └── IntentScoringEngine.cs             # Combina semantic + keyword + priority
│
├── Routing/
│   ├── IRoutingOrchestrator.cs            # Orchestrator principal
│   ├── RoutingOrchestrator.cs             # Decisión de workflow/agente
│   └── RoutingDecision.cs                 # Modelo de decisión
│
├── Ownership/
│   ├── IConversationOwnershipManager.cs   # Gestión de locks conversacionales
│   ├── ConversationOwnershipManager.cs    # Implementación con Redis
│   └── OwnershipLock.cs                   # Modelo de lock
│
├── Inbox/
│   ├── IConversationInboxService.cs       # Servicio de inbox para HITL
│   ├── ConversationInboxService.cs        # Implementación con MongoDB
│   └── InboxConversation.cs               # Modelo de conversación pendiente
│
├── Catalog/
│   ├── IIntentCatalogService.cs           # Gestión de catálogo de intenciones
│   ├── IntentCatalogService.cs            # Carga de base + custom intents
│   ├── base-intents.yaml                  # Catálogo de 30+ intenciones base
│   └── IntentDefinition.cs                # Modelo de intención
│
├── Indexing/
│   ├── IntentVectorIndexer.cs             # Indexing en Qdrant
│   └── IntentBootstrapService.cs          # Bootstrap automático en startup
│
└── Models/
    ├── IntentMatch.cs                      # Resultado de clasificación
    ├── IntentClassificationResult.cs       # Resultado completo con explicabilidad
    ├── ConfidenceLevel.cs                  # High, Medium, Low, NoMatch
    └── ConversationState.cs                # Estados de conversación (Matched, Pending, etc.)
```

---

## 🎯 Key Features

### 1. Intelligent Classification

- **Semantic Matching**: Vector similarity usando Qdrant
- **Keyword Matching**: Reglas determinísticas y regex
- **Hybrid Scoring**: `0.7 × Semantic + 0.2 × Keyword + 0.1 × Priority`
- **Confidence Thresholds**: 
  - High: ≥ 0.90 → Auto-route
  - Medium: 0.75-0.89 → Auto-route con logging
  - Low: 0.50-0.74 → Marcar para revisión
  - No Match: < 0.50 → Fallback queue

### 2. Conversation Ownership

**Regla crítica**: Solo 1 agente AI activo por conversación.

- Distributed locks con Redis
- TTL configurable (default: 5 minutos)
- Handoff explícito entre agentes
- Prevención de race conditions
- Timeout automático

### 3. Fallback Intelligence

**Garantía**: 0 conversaciones perdidas.

- Inbox conversacional para Low Confidence y No Match
- Estados: Matched, LowConfidence, NoMatch, PendingHumanReview, etc.
- Human-in-the-loop (HITL) integrado
- Reasignación manual de intenciones

### 4. Base Intent Catalog

Sistema nunca inicia vacío. Incluye **30+ intenciones preconfiguradas**:

**General**: greeting, farewell, human_agent_request  
**Verification**: document_rejected, upload_document, verification_status  
**Payments**: payment_status, payment_method, payment_confirmation, payment_failure  
**Support**: technical_issue, account_access, general_support  
**Sales**: loan_application, product_inquiry, lead_followup  
**Scheduling**: schedule_appointment, reschedule_appointment, cancel_appointment  
**Complaints**: complaint, service_feedback  
**Information**: business_hours, contact_information, faq  

---

## 🚀 Usage

### Basic Classification

```csharp
var classifier = serviceProvider.GetRequiredService<IIntentScoringEngine>();

var result = await classifier.ClassifyAsync(
    message: "Quiero solicitar un préstamo",
    tenantId: "tenant-123",
    channel: "whatsapp");

Console.WriteLine($"Best Intent: {result.BestMatch.IntentKey}");
Console.WriteLine($"Confidence: {result.BestScore:P}");
Console.WriteLine($"Requires Review: {result.RequiresHumanReview}");
```

### Full Routing

```csharp
var orchestrator = serviceProvider.GetRequiredService<IRoutingOrchestrator>();

var decision = await orchestrator.RouteMessageAsync(
    classification: result,
    context: new ConversationContext
    {
        ConversationId = "conv-456",
        TenantId = "tenant-123",
        Channel = "whatsapp",
        UserIdentifier = "+50581143874"
    });

if (decision.Action == RoutingAction.Route)
{
    // Disparar workflow
    await TriggerWorkflowAsync(decision.WorkflowDefinitionId);
}
else if (decision.Action == RoutingAction.Queue)
{
    // Encolar para revisión humana
    await EnqueueForHumanReviewAsync(decision);
}
```

### Ownership Management

```csharp
var ownershipManager = serviceProvider.GetRequiredService<IConversationOwnershipManager>();

// Intentar adquirir lock
var ownershipLock = await ownershipManager.TryAcquireLockAsync(
    tenantId: "tenant-123",
    conversationId: "conv-456",
    agentId: "workflow-brain-agent",
    ttl: TimeSpan.FromMinutes(5));

if (ownershipLock != null)
{
    try
    {
        // Proceder con workflow
        await ExecuteWorkflowAsync();
        
        // Renovar lock si necesario (operaciones largas)
        await ownershipManager.RenewLockAsync(ownershipLock.LockId, TimeSpan.FromMinutes(3));
    }
    finally
    {
        // SIEMPRE liberar lock (idempotent)
        await ownershipManager.ReleaseLockAsync(ownershipLock.LockId);
    }
}
else
{
    // Conflicto detectado - otro agente posee la conversación
    var state = await ownershipManager.GetStateAsync("tenant-123", "conv-456");
    
    _logger.LogWarning(
        "Conversation locked by another agent: owner={Owner}, expiresAt={ExpiresAt}",
        state.CurrentOwnerAgentId,
        state.LockedUntil);
}
```

### Advanced Ownership Scenarios

**Scenario 1: Handoff Between Agents**

```csharp
// Agent A libera ownership
await ownershipManager.ReleaseLockAsync(lockA.LockId);

// Agent B adquiere inmediatamente
var lockB = await ownershipManager.TryAcquireLockAsync(
    tenantId: "tenant-123",
    conversationId: "conv-456",
    agentId: "human-agent",
    ttl: TimeSpan.FromMinutes(15));
```

**Scenario 2: Check Ownership Before Action**

```csharp
var state = await ownershipManager.GetStateAsync("tenant-123", "conv-456");

if (!state.IsLocked)
{
    // Conversación disponible, adquirir lock
    var lockResult = await ownershipManager.TryAcquireLockAsync(
        "tenant-123", "conv-456", "agent-xyz", TimeSpan.FromMinutes(5));
}
else if (state.CurrentOwnerAgentId == "my-agent-id")
{
    // Ya somos los dueños, continuar
    await ProcessMessageAsync();
}
else
{
    // Otro agente posee la conversación, encolar mensaje
    await EnqueueMessageForLaterAsync();
}
```

**Scenario 3: Automatic Timeout Recovery**

```csharp
// Si un agente crashea sin liberar el lock, TTL automático limpia
// Después de TTL, otro agente puede adquirir

var state = await ownershipManager.GetStateAsync("tenant-123", "conv-456");

if (state.IsLocked && state.LockedUntil < DateTimeOffset.UtcNow.AddMinutes(-1))
{
    _logger.LogWarning("Lock appears expired but not cleaned up. Metadata will auto-expire.");
    
    // Reintentar adquisición (si TTL expiró, lock estará disponible)
    var newLock = await ownershipManager.TryAcquireLockAsync(
        "tenant-123", "conv-456", "recovery-agent", TimeSpan.FromMinutes(5));
}
```

### Conversation Inbox Service

The **Conversation Inbox Service** stores and manages conversations that require human review. It's automatically integrated with the Routing Orchestrator to capture low confidence and no-match scenarios.

**Key Features:**
- ✅ MongoDB persistence for conversations awaiting review
- ✅ Paginated filtering by state, confidence, channel
- ✅ Real-time statistics for dashboard widgets
- ✅ State management (AwaitingClassification → Resolved)
- ✅ Audit trail with timestamps and review notes

**Common Usage Patterns:**

**1. Store Conversation for Review (automatically called by orchestrator):**

```csharp
var inboxService = serviceProvider.GetRequiredService<IConversationInboxService>();

// When classification returns Low confidence or NoMatch, store in inbox
var conversation = new InboxConversation
{
    Id = "conv-123",
    TenantId = "tenant-xyz",
    Channel = "whatsapp",
    UserIdentifier = "+50581143874",
    LastMessage = "Necesito ayuda con algo",
    State = ConversationState.LowConfidence,
    Confidence = ConfidenceLevel.Low,
    DetectedIntentKey = "general_support",
    RequiresHumanReview = true,
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow
};

await inboxService.CreateOrUpdateAsync(conversation);
```

**2. Retrieve Pending Conversations (for inbox UI):**

```csharp
// Get conversations requiring review
var filter = new InboxFilter
{
    RequiresReview = true,
    Page = 1,
    PageSize = 20
};

var result = await inboxService.GetPendingAsync("tenant-xyz", filter);

Console.WriteLine($"Total conversations requiring review: {result.Total}");
Console.WriteLine($"Page {result.Page} of {result.TotalPages}");

foreach (var conv in result.Items)
{
    Console.WriteLine($"  [{conv.State}] {conv.LastMessage} (Confidence: {conv.Confidence})");
}
```

**3. Filter by State or Confidence:**

```csharp
// Get all Low confidence conversations from WhatsApp
var filter = new InboxFilter
{
    State = ConversationState.LowConfidence,
    Channel = "whatsapp",
    Page = 1,
    PageSize = 50
};

var result = await inboxService.GetPendingAsync("tenant-xyz", filter);
```

**4. Update Conversation State (human review workflow):**

```csharp
// Human agent reviews conversation and approves classification
var success = await inboxService.UpdateStateAsync(
    tenantId: "tenant-xyz",
    conversationId: "conv-123",
    newState: ConversationState.InProgress,
    notes: "Classification verified by agent. Proceeding with workflow.");

if (success)
{
    // Trigger workflow execution
    await TriggerWorkflowAsync("conv-123");
}
```

**5. Get Inbox Statistics (for dashboard):**

```csharp
var stats = await inboxService.GetStatsAsync("tenant-xyz");

Console.WriteLine($"Total conversations: {stats.TotalConversations}");
Console.WriteLine($"Awaiting classification: {stats.AwaitingClassification}");
Console.WriteLine($"Requires review: {stats.RequiresReview}");
Console.WriteLine($"Resolved today: {stats.ResolvedToday}");
Console.WriteLine($"In progress: {stats.InProgress}");
Console.WriteLine($"No match: {stats.NoMatch}");

Console.WriteLine("\nBy State:");
foreach (var (state, count) in stats.ByState)
{
    Console.WriteLine($"  {state}: {count}");
}

Console.WriteLine("\nBy Confidence:");
foreach (var (confidence, count) in stats.ByConfidence)
{
    Console.WriteLine($"  {confidence}: {count}");
}
```

**Conversation State Lifecycle:**

```
AwaitingClassification → Classified → InProgress → Resolved
                      ↘               ↗
                        LowConfidence → PendingHumanReview → Escalated
                      ↘               ↗
                        NoMatch -------→ Abandoned
                                    ↘
                                      ConflictDetected
```

**API Endpoints (via InboxController):**

```
GET    /api/v1/tenants/{tenantId}/inbox
GET    /api/v1/tenants/{tenantId}/inbox/{conversationId}
PUT    /api/v1/tenants/{tenantId}/inbox/{conversationId}/state
GET    /api/v1/tenants/{tenantId}/inbox/stats
```

---

## 🧪 Testing

### Run Unit Tests

```bash
dotnet test tests/AgentFlow.Tests.Unit/IntentRouting/
```

### Run Integration Tests

```bash
dotnet test tests/AgentFlow.Tests.Integration/IntentRouting/
```

### Run Benchmarks

```bash
dotnet test tests/AgentFlow.Tests.Integration/IntentRouting/BenchmarkTests.cs
```

**Expected Results**:
- Accuracy ≥ 99%
- False Positive Rate < 1%
- Agent Collision Rate = 0%
- Test Coverage ≥ 90%

---

## 📊 Metrics & Observability

### Key Metrics

- **Accuracy**: Proporción de clasificaciones correctas
- **Precision**: TP / (TP + FP)
- **Recall**: TP / (TP + FN)
- **F1 Score**: 2 × (Precision × Recall) / (Precision + Recall)
- **False Positive Rate**: FP / (FP + TN)
- **Agent Collision Rate**: Número de conflictos de ownership
- **Fallback Rate**: Proporción de mensajes sin match

### Alerting Thresholds

⚠️ **Warning**:
- Accuracy < 95%
- False Positive Rate > 1%
- Fallback Rate > 10%

🚨 **Critical**:
- Accuracy < 90%
- Agent Collision Rate > 0%

---

## 📚 Documentation

- **[Architecture](../../docs/INTENT-ROUTING-ARCHITECTURE.md)** — Arquitectura completa (80+ páginas)
- **[Implementation Plan](../../docs/INTENT-ROUTING-IMPLEMENTATION-PLAN.md)** — Plan de implementación por fases
- **[Executive Summary](../../docs/INTENT-ROUTING-EXECUTIVE-SUMMARY.md)** — Resumen para stakeholders
- **[Base Intents Catalog](./Catalog/base-intents.yaml)** — Catálogo de intenciones oficiales

---

## 🛠️ Dependencies

### Required
- **Qdrant**: Vector database para embeddings (ya existente en AgentFlow)
- **Redis**: Distributed locks (ya existente en AgentFlow)
- **MongoDB**: Persistencia de intenciones e inbox (ya existente en AgentFlow)
- **ModelRouting**: Generación de embeddings (ya existente en AgentFlow)

### Optional
- **Semantic Kernel**: Integración con LLM para AI Assistant

---

## 🚧 Development Status

### Phase 1: Foundation (Weeks 1-2)
- [ ] Semantic Matcher implementation
- [ ] Keyword Matcher implementation
- [ ] Hybrid Scoring Engine
- [ ] Ownership Manager
- [ ] Vector Indexing

### Phase 2: Routing & Fallback (Weeks 3-4)
- [x] Routing Orchestrator
- [x] Inbox Service
- [ ] Integration with AgentExecutionEngine

### Phase 3: Intent Catalog (Week 5)
- [x] Base intents YAML (30+ intents)
- [ ] Catalog Service
- [ ] Bootstrap Service

### Phase 4: Frontend MVP (Weeks 6-7)
- [ ] Intent Management Page
- [ ] Intent Playground
- [ ] Conversation Inbox

### Phase 5: Testing & Validation (Week 8)
- [ ] Happy Path Tests
- [ ] Regression Tests
- [ ] Benchmarks

### Phase 6: AI Assistant (Weeks 9-10)
- [ ] Intent Suggestion Tool
- [ ] Duplicate Detection
- [ ] Auto-improvement

### Phase 7: Observability (Weeks 11-12)
- [ ] Metrics Dashboard
- [ ] Alerting Configuration
- [ ] Runbooks

---

## 👥 Contributing

Este módulo es **crítico** para el sistema. Cualquier cambio debe:

1. ✅ Pasar todos los tests (coverage ≥ 90%)
2. ✅ Cumplir benchmarks (accuracy ≥ 99%)
3. ✅ Incluir tests de regresión
4. ✅ Documentar cambios en arquitectura
5. ✅ Obtener review del Orchestrator

---

## 📞 Contact

- **Owner**: Orchestrator Agent
- **Specialized Agents**: core-engine, data-expert, frontend, evaluation
- **Documentation**: `docs/INTENT-ROUTING-*.md`

---

**Status**: 🚧 Phase 1 (Foundation) in progress  
**Target Accuracy**: ≥ 99%  
**Target Completion**: 12 weeks from Phase 1 start
