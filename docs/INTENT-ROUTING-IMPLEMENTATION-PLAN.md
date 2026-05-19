# Intent Routing — Implementation Plan & Task Breakdown

> **Documento**: Plan de Ejecución Detallado  
> **Fecha**: 2026-05-18  
> **Owner**: Orchestrator Agent  
> **Arquitectura Base**: [INTENT-ROUTING-ARCHITECTURE.md](./INTENT-ROUTING-ARCHITECTURE.md)

---

## 🎯 Estrategia de Ejecución

Este documento desglosa la arquitectura del módulo de Intenciones y Routing en **tareas específicas** asignadas a cada agente especializado según su expertise.

**Principio de Coordinación**: Cada agente es responsable de su dominio, pero el Orchestrator coordina integración y garantiza cumplimiento con Unicorn Strategy.

---

## 📋 Fase 1: Foundation — CORE ENGINE

**Duración Estimada**: 2 semanas  
**Objetivo**: Implementar motor de clasificación inteligente y ownership conversacional  
**Estado**: 🔴 PENDIENTE

### 1.1 Semantic Intent Matcher (core-engine + data-expert)

#### Responsable Principal: `core-engine`

**Contratos e Interfaces**:
```csharp
// Ubicación: src/AgentFlow.Intents/Classification/ISemanticIntentMatcher.cs
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

**Implementación**:
```csharp
// Ubicación: src/AgentFlow.Intents/Classification/QdrantSemanticIntentMatcher.cs
public sealed class QdrantSemanticIntentMatcher : ISemanticIntentMatcher
{
    private readonly IVectorMemory _vectorMemory; // Ya existe en AgentFlow
    private readonly ILogger<QdrantSemanticIntentMatcher> _logger;

    public async Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        int topK = 5,
        CancellationToken ct = default)
    {
        // 1. Generar embedding del mensaje
        var embedding = await GenerateEmbeddingAsync(message, ct);
        
        // 2. Buscar en Qdrant con filtros de tenant y channel
        var collectionName = $"intents_{tenantId}";
        var searchResults = await _vectorMemory.SearchAsync(
            collectionName,
            embedding,
            topK,
            filters: new Dictionary<string, object>
            {
                ["tenant_id"] = tenantId,
                ["enabled"] = true,
                ["channel"] = channel ?? "*"
            },
            ct);
        
        // 3. Mapear a IntentMatch con similarity scores
        return searchResults.Select(r => new IntentMatch
        {
            IntentKey = r.Metadata["intent_key"].ToString(),
            SimilarityScore = r.Score,
            MatchedVia = "semantic",
            Rule = DeserializeRule(r.Metadata)
        }).ToList();
    }
}
```

**Tareas**:
- [ ] Crear directorio `src/AgentFlow.Intents/`
- [ ] Implementar `ISemanticIntentMatcher` interface
- [ ] Implementar `QdrantSemanticIntentMatcher` usando `IVectorMemory` existente
- [ ] Implementar generación de embeddings (usando ModelRouting)
- [ ] Tests unitarios con mocks
- [ ] Tests de integración con Qdrant real

**Dependencias**:
- ✅ Qdrant ya existente (`IVectorMemory`)
- ✅ ModelRouting para embeddings ya existente

---

#### Responsable Principal: `data-expert`

**Vector Indexing de Intenciones**:

**Tarea**: Implementar servicio que indexa intenciones en Qdrant.

```csharp
// Ubicación: src/AgentFlow.Intents/Indexing/IntentVectorIndexer.cs
public sealed class IntentVectorIndexer
{
    private readonly IVectorMemory _vectorMemory;
    private readonly IIntentCatalogService _catalog;
    private readonly IEmbeddingGenerator _embeddingGenerator;

    public async Task RebuildIndexAsync(string tenantId, CancellationToken ct = default)
    {
        // 1. Obtener todas las intenciones (base + custom)
        var baseIntents = await _catalog.GetBaseIntentsAsync(ct);
        var tenantIntents = await _catalog.GetTenantIntentsAsync(tenantId, ct);
        var allIntents = baseIntents.Concat(tenantIntents).ToList();

        // 2. Generar embeddings para cada intención
        var collectionName = $"intents_{tenantId}";
        await _vectorMemory.CreateCollectionAsync(collectionName, 1536, ct); // 1536 = OpenAI embedding dim

        foreach (var intent in allIntents)
        {
            // Crear texto compuesto para embedding
            var textToEmbed = BuildIntentText(intent);
            var embedding = await _embeddingGenerator.GenerateAsync(textToEmbed, ct);

            // Almacenar en Qdrant
            await _vectorMemory.StoreAsync(
                collectionName,
                id: intent.Key,
                embedding: embedding,
                metadata: new Dictionary<string, object>
                {
                    ["intent_key"] = intent.Key,
                    ["tenant_id"] = tenantId,
                    ["enabled"] = true,
                    ["category"] = intent.Category,
                    ["priority"] = intent.Priority,
                    ["confidence_threshold"] = intent.ConfidenceThreshold,
                    ["rule_json"] = JsonSerializer.Serialize(intent)
                },
                ct);
        }
    }

    private string BuildIntentText(IntentDefinition intent)
    {
        // Combinar description + examples + synonyms para mejor matching
        return $"{intent.Description}\n" +
               $"Examples: {string.Join(", ", intent.Examples)}\n" +
               $"Synonyms: {string.Join(", ", intent.Synonyms)}";
    }
}
```

**Tareas**:
- [ ] Implementar `IntentVectorIndexer`
- [ ] Crear endpoint API para rebuild index: `POST /api/v1/tenants/{tenantId}/intent-routing/rebuild-index`
- [ ] Implementar bootstrap automático en startup (cargar base intents)
- [ ] Tests de indexing completo

---

### 1.2 Keyword Intent Matcher (core-engine)

**Responsable**: `core-engine`

**Propósito**: Matching determinístico por keywords, regex y reglas.

```csharp
// Ubicación: src/AgentFlow.Intents/Classification/KeywordIntentMatcher.cs
public sealed class KeywordIntentMatcher : IKeywordIntentMatcher
{
    public async Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default)
    {
        var rules = await _ruleStore.GetRulesByChannelAsync(tenantId, channel, ct);
        var matches = new List<IntentMatch>();

        foreach (var rule in rules)
        {
            var score = CalculateKeywordScore(message, rule);
            if (score > 0)
            {
                matches.Add(new IntentMatch
                {
                    IntentKey = rule.IntentKey,
                    SimilarityScore = score,
                    MatchedVia = "keyword",
                    Rule = rule
                });
            }
        }

        return matches.OrderByDescending(m => m.SimilarityScore).ToList();
    }

    private float CalculateKeywordScore(string message, IntentRoutingRule rule)
    {
        var messageLower = message.ToLowerInvariant();
        float score = 0f;

        // 1. Exact keyword matching
        foreach (var example in rule.ExamplePhrases)
        {
            if (messageLower.Contains(example.ToLowerInvariant()))
            {
                score += 0.3f;
            }
        }

        // 2. N-gram overlap
        var messageTokens = Tokenize(messageLower);
        var exampleTokens = rule.ExamplePhrases.SelectMany(Tokenize).Distinct().ToList();
        var overlap = messageTokens.Intersect(exampleTokens).Count();
        score += (float)overlap / Math.Max(messageTokens.Count, exampleTokens.Count);

        return Math.Min(score, 1.0f);
    }

    private List<string> Tokenize(string text)
    {
        return text.Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(t => t.Trim())
                   .Where(t => t.Length > 2) // ignorar palabras muy cortas
                   .ToList();
    }
}
```

**Tareas**:
- [ ] Implementar `IKeywordIntentMatcher`
- [ ] Implementar `KeywordIntentMatcher` con scoring
- [ ] Tests unitarios

---

### 1.3 Hybrid Scoring Engine (core-engine)

**Responsable**: `core-engine`

**Propósito**: Combinar semantic + keyword + priority para scoring final.

```csharp
// Ubicación: src/AgentFlow.Intents/Classification/IntentScoringEngine.cs
public sealed class IntentScoringEngine : IIntentScoringEngine
{
    private readonly ISemanticIntentMatcher _semanticMatcher;
    private readonly IKeywordIntentMatcher _keywordMatcher;

    public async Task<IntentClassificationResult> ClassifyAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default)
    {
        // 1. Obtener candidatos de ambos matchers
        var semanticCandidates = await _semanticMatcher.FindCandidatesAsync(message, tenantId, channel, 10, ct);
        var keywordCandidates = await _keywordMatcher.FindCandidatesAsync(message, tenantId, channel, ct);

        // 2. Combinar scores
        var combinedScores = CombineScores(semanticCandidates, keywordCandidates);

        // 3. Ordenar por score final
        var ranked = combinedScores.OrderByDescending(c => c.FinalScore).ToList();

        var bestMatch = ranked.FirstOrDefault();
        var confidence = DetermineConfidence(bestMatch?.FinalScore ?? 0);

        // 4. Explicabilidad
        var explanation = BuildExplanation(ranked, message);

        return new IntentClassificationResult
        {
            Message = message,
            BestMatch = bestMatch,
            AllCandidates = ranked.Take(5).ToList(),
            BestScore = bestMatch?.FinalScore ?? 0,
            Confidence = confidence,
            RequiresHumanReview = confidence <= ConfidenceLevel.Low,
            ExplanationJson = JsonSerializer.Serialize(explanation)
        };
    }

    private List<ScoredIntent> CombineScores(
        IReadOnlyList<IntentMatch> semantic,
        IReadOnlyList<IntentMatch> keyword)
    {
        var combined = new Dictionary<string, ScoredIntent>();

        // Semantic (peso 70%)
        foreach (var match in semantic)
        {
            if (!combined.ContainsKey(match.IntentKey))
            {
                combined[match.IntentKey] = new ScoredIntent { IntentKey = match.IntentKey, Rule = match.Rule };
            }
            combined[match.IntentKey].SemanticScore = match.SimilarityScore;
        }

        // Keyword (peso 20%)
        foreach (var match in keyword)
        {
            if (!combined.ContainsKey(match.IntentKey))
            {
                combined[match.IntentKey] = new ScoredIntent { IntentKey = match.IntentKey, Rule = match.Rule };
            }
            combined[match.IntentKey].KeywordScore = match.SimilarityScore;
        }

        // Calcular score final
        foreach (var item in combined.Values)
        {
            var priority = item.Rule.Priority;
            var priorityScore = Math.Min(priority / 1000f, 1.0f); // normalizar priority a [0, 1]

            item.FinalScore = (0.7f * item.SemanticScore) +
                              (0.2f * item.KeywordScore) +
                              (0.1f * priorityScore);
        }

        return combined.Values.ToList();
    }

    private ConfidenceLevel DetermineConfidence(float score)
    {
        return score switch
        {
            >= 0.90f => ConfidenceLevel.High,
            >= 0.75f => ConfidenceLevel.Medium,
            >= 0.50f => ConfidenceLevel.Low,
            _ => ConfidenceLevel.NoMatch
        };
    }
}

internal sealed class ScoredIntent
{
    public required string IntentKey { get; init; }
    public required IntentRoutingRule Rule { get; init; }
    public float SemanticScore { get; set; }
    public float KeywordScore { get; set; }
    public float FinalScore { get; set; }
}
```

**Tareas**:
- [ ] Implementar `IIntentScoringEngine`
- [ ] Implementar `IntentScoringEngine` con hybrid scoring
- [ ] Implementar `BuildExplanation` para explicabilidad
- [ ] Tests con diferentes escenarios de scoring

---

### 1.4 Conversation Ownership Manager (core-engine + data-expert)

#### Responsable Principal: `core-engine`

**Propósito**: Gestionar locks conversacionales para prevenir colisiones de agentes.

```csharp
// Ubicación: src/AgentFlow.Intents/Ownership/ConversationOwnershipManager.cs
public sealed class ConversationOwnershipManager : IConversationOwnershipManager
{
    private readonly IDistributedLockService _lockService; // Ya existe en Redis
    private readonly ILogger<ConversationOwnershipManager> _logger;

    public async Task<OwnershipLock?> TryAcquireLockAsync(
        string tenantId,
        string conversationId,
        string agentId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var lockKey = $"conversation:lock:{tenantId}:{conversationId}";
        
        var acquired = await _lockService.TryAcquireLockAsync(lockKey, ttl, ct);
        
        if (acquired == null)
        {
            _logger.LogWarning("Failed to acquire lock for conversation {ConvId} by agent {AgentId}", 
                conversationId, agentId);
            return null;
        }

        var ownershipLock = new OwnershipLock
        {
            LockId = acquired.LockId,
            ConversationId = conversationId,
            OwnerAgentId = agentId,
            AcquiredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };

        // Almacenar metadata del lock en Redis
        await StoreOwnershipMetadataAsync(tenantId, conversationId, ownershipLock, ct);

        return ownershipLock;
    }

    public async Task<ConversationOwnershipState> GetStateAsync(
        string tenantId,
        string conversationId,
        CancellationToken ct = default)
    {
        var metadata = await GetOwnershipMetadataAsync(tenantId, conversationId, ct);
        
        return new ConversationOwnershipState
        {
            ConversationId = conversationId,
            IsLocked = metadata != null && metadata.ExpiresAt > DateTimeOffset.UtcNow,
            CurrentOwnerAgentId = metadata?.OwnerAgentId,
            LockedUntil = metadata?.ExpiresAt,
            WorkflowExecutionId = metadata?.WorkflowExecutionId
        };
    }
}
```

**Tareas**:
- [ ] Implementar `IConversationOwnershipManager`
- [ ] Implementar `ConversationOwnershipManager` usando Redis locks
- [ ] Implementar metadata storage en Redis
- [ ] Tests de concurrencia (simular 2 agentes intentando lock simultáneo)

---

## 📋 Fase 2: Routing & Fallback — ORCHESTRATION

**Duración Estimada**: 2 semanas  
**Objetivo**: Implementar orchestrator y fallback intelligence  
**Estado**: 🔴 PENDIENTE

### 2.1 Routing Orchestrator (core-engine)

**Responsable**: `core-engine`

```csharp
// Ubicación: src/AgentFlow.Intents/Routing/RoutingOrchestrator.cs
public sealed class RoutingOrchestrator : IRoutingOrchestrator
{
    private readonly IIntentScoringEngine _scoringEngine;
    private readonly IConversationOwnershipManager _ownershipManager;
    private readonly IIntentRoutingStore _routingStore;
    private readonly IAuditMemory _audit;

    public async Task<RoutingDecision> RouteMessageAsync(
        IntentClassificationResult classification,
        ConversationContext context,
        CancellationToken ct = default)
    {
        // 1. Validar confidence
        if (classification.Confidence == ConfidenceLevel.NoMatch)
        {
            return BuildFallbackDecision("no_match", classification);
        }

        if (classification.RequiresHumanReview)
        {
            return BuildQueueDecision("low_confidence", classification);
        }

        var bestMatch = classification.BestMatch!;
        var rule = bestMatch.Rule;

        // 2. Verificar ownership conversacional
        var ownershipState = await _ownershipManager.GetStateAsync(
            context.TenantId,
            context.ConversationId,
            ct);

        if (ownershipState.IsLocked && ownershipState.CurrentOwnerAgentId != rule.TargetAgentId)
        {
            // Conflicto: otro agente tiene el lock
            return BuildRejectDecision("agent_conflict", classification, ownershipState);
        }

        // 3. Intentar adquirir lock (solo si el workflow tiene agente AI)
        if (rule.TargetAgentId != null)
        {
            var lockAcquired = await _ownershipManager.TryAcquireLockAsync(
                context.TenantId,
                context.ConversationId,
                rule.TargetAgentId,
                TimeSpan.FromMinutes(5),
                ct);

            if (lockAcquired == null)
            {
                return BuildRejectDecision("lock_failed", classification, ownershipState);
            }
        }

        // 4. Routing exitoso
        var decision = new RoutingDecision
        {
            IntentKey = rule.IntentKey,
            WorkflowDefinitionId = rule.WorkflowDefinitionId,
            TargetAgentId = rule.TargetAgentId,
            Action = RoutingAction.Route,
            ReasonCode = "matched",
            ExplanationJson = JsonSerializer.Serialize(new
            {
                intent = rule.IntentKey,
                confidence = classification.BestScore,
                workflow = rule.WorkflowName,
                agent = rule.TargetAgentId
            }),
            DecidedAt = DateTimeOffset.UtcNow
        };

        // 5. Auditar decisión
        await _audit.RecordAsync(new AuditEntry
        {
            TenantId = context.TenantId,
            EventType = AuditEventType.RoutingDecision,
            EventJson = JsonSerializer.Serialize(decision)
        }, ct);

        return decision;
    }
}
```

**Tareas**:
- [ ] Implementar `IRoutingOrchestrator`
- [ ] Implementar `RoutingOrchestrator` con reglas de ownership
- [ ] Implementar auditoría de decisiones
- [ ] Tests de routing completo (happy path + conflicts)

---

### 2.2 Conversation Inbox Service (data-expert)

**Responsable**: `data-expert`

**Propósito**: Gestionar inbox de conversaciones pendientes de revisión.

```csharp
// Ubicación: src/AgentFlow.Intents/Inbox/ConversationInboxService.cs
public sealed class ConversationInboxService : IConversationInboxService
{
    private readonly IMongoCollection<InboxConversationDocument> _collection;

    public async Task<InboxConversation> CreateOrUpdateAsync(
        InboxConversation conversation,
        CancellationToken ct = default)
    {
        var doc = MapToDocument(conversation);
        
        await _collection.ReplaceOneAsync(
            x => x.TenantId == doc.TenantId && x.Id == doc.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return conversation;
    }

    public async Task<PagedResult<InboxConversation>> GetPendingAsync(
        string tenantId,
        InboxFilter filter,
        CancellationToken ct = default)
    {
        var query = BuildFilterQuery(tenantId, filter);
        
        var total = await _collection.CountDocumentsAsync(query, cancellationToken: ct);
        
        var docs = await _collection
            .Find(query)
            .Sort(Builders<InboxConversationDocument>.Sort.Descending(x => x.UpdatedAt))
            .Skip(filter.Skip)
            .Limit(filter.Take)
            .ToListAsync(ct);

        return new PagedResult<InboxConversation>
        {
            Items = docs.Select(MapToModel).ToList(),
            Total = (int)total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }
}
```

**Tareas**:
- [ ] Implementar `IConversationInboxService`
- [ ] Implementar `ConversationInboxService` con MongoDB
- [ ] Crear índices MongoDB para queries eficientes
- [ ] API endpoints para Inbox (`GET /inbox`, `PUT /inbox/{id}/state`)

---

### 2.3 Integración con AgentExecutionEngine (core-engine)

**Responsable**: `core-engine`

**Tarea**: Modificar `AgentExecutionEngine` para usar el nuevo sistema de routing.

**Ubicación**: `src/AgentFlow.Core.Engine/AgentExecutionEngine.cs`

**Cambios requeridos**:

1. Antes de ejecutar Router agent, clasificar intención:
```csharp
// En ExecuteAgentAsync, si es Router:
if (agentDef.SystemRole == AgentSystemRole.Router)
{
    // Clasificar intención ANTES de llamar al LLM
    var classification = await _scoringEngine.ClassifyAsync(
        request.UserMessage,
        request.TenantId,
        request.SessionContext?.Channel,
        ct);

    // Inyectar clasificación en el contexto del Router
    var enhancedContext = new Dictionary<string, object>(request.AdditionalContext ?? new())
    {
        ["intent_classification"] = classification
    };

    request = request with { AdditionalContext = enhancedContext };
}
```

2. Después de clasificación, llamar a Orchestrator:
```csharp
var conversationContext = new ConversationContext
{
    ConversationId = request.SessionContext?.SessionId ?? request.CorrelationId,
    TenantId = request.TenantId,
    Channel = request.SessionContext?.Channel ?? "api",
    UserIdentifier = request.UserId
};

var routingDecision = await _orchestrator.RouteMessageAsync(
    classification,
    conversationContext,
    ct);

// Si routing decision es Fallback o Queue, marcar en Inbox
if (routingDecision.Action == RoutingAction.Fallback || routingDecision.Action == RoutingAction.Queue)
{
    await _inboxService.CreateOrUpdateAsync(new InboxConversation
    {
        Id = conversationContext.ConversationId,
        TenantId = request.TenantId,
        Channel = conversationContext.Channel,
        UserIdentifier = request.UserId,
        LastMessage = request.UserMessage,
        State = routingDecision.Action == RoutingAction.Fallback 
            ? ConversationState.NoMatch 
            : ConversationState.LowConfidence,
        Confidence = classification.Confidence,
        DetectedIntentKey = classification.BestMatch?.IntentKey,
        RequiresHumanReview = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    }, ct);
}
```

**Tareas**:
- [ ] Modificar `AgentExecutionEngine` para integrar clasificación
- [ ] Modificar Router agent para usar decisión pre-clasificada
- [ ] Tests end-to-end completos

---

## 📋 Fase 3: Intent Catalog — BASE INTENTS

**Duración Estimada**: 1 semana  
**Objetivo**: Catálogo base + bootstrap automático  
**Estado**: 🟡 PARCIAL (YAML creado)

### 3.1 Intent Catalog Service (core-engine)

**Responsable**: `core-engine`

```csharp
// Ubicación: src/AgentFlow.Intents/Catalog/IntentCatalogService.cs
public sealed class IntentCatalogService : IIntentCatalogService
{
    private readonly IIntentCatalogStore _store;
    private readonly ILogger<IntentCatalogService> _logger;
    private static IReadOnlyList<IntentDefinition>? _cachedBaseIntents;

    public async Task<IReadOnlyList<IntentDefinition>> GetBaseIntentsAsync(CancellationToken ct = default)
    {
        if (_cachedBaseIntents != null) return _cachedBaseIntents;

        // Cargar desde archivo YAML embebido
        var yamlContent = await LoadEmbeddedYamlAsync("base-intents.yaml");
        var catalog = YamlDeserializer.Deserialize<IntentCatalog>(yamlContent);

        _cachedBaseIntents = catalog.Intents.Select(i => new IntentDefinition
        {
            Key = i.Key,
            Name = i.Name,
            Description = i.Description,
            Category = i.Category,
            Examples = i.Examples,
            Synonyms = i.Synonyms,
            ConfidenceThreshold = i.ConfidenceThreshold,
            Priority = i.Priority,
            SuggestedWorkflow = i.SuggestedWorkflow,
            IsBaseIntent = true,
            Version = 1
        }).ToList();

        return _cachedBaseIntents;
    }

    public async Task<IReadOnlyList<IntentDefinition>> GetTenantIntentsAsync(
        string tenantId,
        CancellationToken ct = default)
    {
        return await _store.GetCustomIntentsAsync(tenantId, ct);
    }
}
```

**Tareas**:
- [ ] Implementar `IIntentCatalogService`
- [ ] Implementar `IntentCatalogService` con carga de YAML
- [ ] Implementar store para custom intents
- [ ] Tests de carga de catálogo

---

### 3.2 Bootstrap Service (data-expert)

**Responsable**: `data-expert`

**Propósito**: Cargar automáticamente base intents en startup.

```csharp
// Ubicación: src/AgentFlow.Api/Startup/IntentBootstrapService.cs
public sealed class IntentBootstrapService : IHostedService
{
    private readonly IIntentCatalogService _catalog;
    private readonly IntentVectorIndexer _indexer;
    private readonly ILogger<IntentBootstrapService> _logger;

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Bootstrapping base intents...");

        try
        {
            // 1. Cargar base intents
            var baseIntents = await _catalog.GetBaseIntentsAsync(ct);
            _logger.LogInformation("Loaded {Count} base intents", baseIntents.Count);

            // 2. Indexar en Qdrant (global collection para base intents)
            await _indexer.RebuildGlobalIndexAsync(baseIntents, ct);
            _logger.LogInformation("Base intents indexed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bootstrap base intents");
            throw;
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**Tareas**:
- [ ] Implementar `IntentBootstrapService`
- [ ] Registrar en `Program.cs` como `IHostedService`
- [ ] Tests de bootstrap

---

## 📋 Fase 4: Frontend MVP — UI/UX

**Duración Estimada**: 2 semanas  
**Objetivo**: Pantallas de Intenciones + Playground + Inbox  
**Estado**: 🔴 PENDIENTE

### 4.1 Intent Management Page (frontend)

**Responsable**: `frontend`

**Ruta**: `/dashboard/intents`

**Componentes**:

```tsx
// frontend/aiagent_flow/src/aiagentflow/pages/intents/IntentsPage.tsx
export default function IntentsPage() {
  const [intents, setIntents] = useState<IntentDefinition[]>([]);
  const [filter, setFilter] = useState({ category: 'all', enabled: 'all' });
  const [searchQuery, setSearchQuery] = useState('');

  return (
    <Container maxWidth="xl">
      <Stack spacing={3}>
        <Stack direction="row" justifyContent="space-between">
          <Typography variant="h4">Intent Management</Typography>
          <Button variant="contained" startIcon={<Iconify icon="eva:plus-fill" />}>
            Create Intent
          </Button>
        </Stack>

        <IntentFilters filter={filter} onChange={setFilter} />
        <IntentSearchBar value={searchQuery} onChange={setSearchQuery} />
        <IntentsTable intents={filteredIntents} onEdit={handleEdit} onToggle={handleToggle} />
      </Stack>
    </Container>
  );
}
```

**Tareas**:
- [ ] Crear `IntentsPage.tsx`
- [ ] Crear `IntentsTable` component
- [ ] Crear `IntentFilters` component
- [ ] Crear `CreateIntentDialog` component
- [ ] Integrar con API `/api/v1/tenants/{tenantId}/intent-routing/rules`
- [ ] Agregar ruta en `paths.ts` y `nav-config-dashboard.tsx`

---

### 4.2 Intent Playground (frontend)

**Responsable**: `frontend`

**Ruta**: `/dashboard/intents/playground`

```tsx
// frontend/aiagent_flow/src/aiagentflow/pages/intents/PlaygroundPage.tsx
export default function PlaygroundPage() {
  const [testMessage, setTestMessage] = useState('');
  const [result, setResult] = useState<ClassificationResult | null>(null);
  const [loading, setLoading] = useState(false);

  const handleClassify = async () => {
    setLoading(true);
    try {
      const res = await axios.post(endpoints.agentflow.intents.classify(TENANT_ID), {
        message: testMessage
      });
      setResult(res.data);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Container maxWidth="lg">
      <Stack spacing={4}>
        <Typography variant="h4">Intent Classification Playground</Typography>

        <Card>
          <CardContent>
            <Stack spacing={2}>
              <TextField
                fullWidth
                multiline
                rows={3}
                label="Test Message"
                value={testMessage}
                onChange={(e) => setTestMessage(e.target.value)}
                placeholder="Type a message to classify..."
              />
              <Button
                variant="contained"
                onClick={handleClassify}
                loading={loading}
                disabled={!testMessage.trim()}
              >
                Classify Intent
              </Button>
            </Stack>
          </CardContent>
        </Card>

        {result && (
          <Stack spacing={3}>
            <BestMatchCard match={result.bestMatch} confidence={result.confidence} />
            <CandidatesListCard candidates={result.allCandidates} />
            <ExplanationCard explanation={JSON.parse(result.explanationJson)} />
          </Stack>
        )}
      </Stack>
    </Container>
  );
}
```

**Tareas**:
- [ ] Crear `PlaygroundPage.tsx`
- [ ] Crear `BestMatchCard` component
- [ ] Crear `CandidatesListCard` component
- [ ] Crear `ExplanationCard` component
- [ ] API endpoint: `POST /api/v1/tenants/{tenantId}/intent-routing/classify`

---

### 4.3 Conversation Inbox (frontend)

**Responsable**: `frontend`

**Ruta**: `/dashboard/inbox`

```tsx
// frontend/aiagent_flow/src/aiagentflow/pages/inbox/InboxPage.tsx
export default function InboxPage() {
  const [conversations, setConversations] = useState<InboxConversation[]>([]);
  const [filter, setFilter] = useState<InboxFilter>({ state: 'all', confidence: 'all' });

  return (
    <Container maxWidth="xl">
      <Stack spacing={3}>
        <Typography variant="h4">Conversation Inbox</Typography>

        <InboxFilters filter={filter} onChange={setFilter} />
        <InboxStatsCards stats={stats} />
        <InboxTable
          conversations={conversations}
          onView={handleView}
          onReassign={handleReassign}
          onResolve={handleResolve}
        />
      </Stack>
    </Container>
  );
}
```

**Tareas**:
- [ ] Crear `InboxPage.tsx`
- [ ] Crear `InboxTable` component con estados visuales
- [ ] Crear `InboxFilters` component
- [ ] Crear `InboxStatsCards` component (metrics)
- [ ] Crear `ConversationDetailDialog` (view full thread)
- [ ] API endpoints: `GET /inbox`, `PUT /inbox/{id}/state`

---

## 📋 Fase 5: Testing & Validation — QUALITY

**Duración Estimada**: 1 semana  
**Objetivo**: Suite completa de testing automatizado  
**Estado**: 🔴 PENDIENTE

### 5.1 Happy Path Tests (evaluation)

**Responsable**: `evaluation`

**Ubicación**: `tests/AgentFlow.Tests.Integration/IntentRouting/HappyPathTests.cs`

```csharp
[TestClass]
public class IntentClassificationHappyPathTests
{
    private IIntentScoringEngine _classifier;
    private string _tenantId = "test-tenant";

    [TestInitialize]
    public async Task Setup()
    {
        // Setup test infrastructure
        _classifier = TestContext.Services.GetRequiredService<IIntentScoringEngine>();
        await SeedBaseIntentsAsync();
    }

    [TestMethod]
    [DataRow("Hola, buenos días", "greeting", 0.90f)]
    [DataRow("Por qué rechazaron mi documento", "document_rejected", 0.92f)]
    [DataRow("Cuándo se procesará mi pago", "payment_status", 0.88f)]
    [DataRow("Quiero solicitar un préstamo", "loan_application", 0.93f)]
    [DataRow("Necesito cambiar mi cita", "reschedule_appointment", 0.91f)]
    public async Task ShouldClassifyIntentCorrectly(
        string message,
        string expectedIntent,
        float minConfidence)
    {
        var result = await _classifier.ClassifyAsync(message, _tenantId);

        Assert.IsNotNull(result.BestMatch, $"No match found for: {message}");
        Assert.AreEqual(expectedIntent, result.BestMatch.IntentKey, 
            $"Expected {expectedIntent}, got {result.BestMatch.IntentKey}");
        Assert.IsTrue(result.BestScore >= minConfidence, 
            $"Confidence {result.BestScore} below threshold {minConfidence}");
        Assert.AreEqual(ConfidenceLevel.High, result.Confidence);
    }
}
```

**Tareas**:
- [ ] Crear 20+ test cases para cada intención base
- [ ] Tests con variantes (sinónimos, errores ortográficos)
- [ ] Tests en inglés y español
- [ ] Tests de edge cases

---

### 5.2 Routing Validation Suite (evaluation)

**Responsable**: `evaluation`

```csharp
[TestClass]
public class RoutingOrchestrationTests
{
    [TestMethod]
    public async Task ShouldRouteToCorrectWorkflow()
    {
        var message = "Quiero solicitar un préstamo";
        var classification = await _classifier.ClassifyAsync(message, _tenantId);
        var decision = await _orchestrator.RouteMessageAsync(classification, _context);

        Assert.AreEqual("sales.loan_application", decision.WorkflowDefinitionId);
        Assert.AreEqual(RoutingAction.Route, decision.Action);
    }

    [TestMethod]
    public async Task ShouldPreventAgentCollision()
    {
        // Agent A ya tiene lock
        await _ownership.TryAcquireLockAsync(_tenantId, _convId, "agent-a", TimeSpan.FromMinutes(5));

        // Agent B intenta routing
        var decision = await _orchestrator.RouteMessageAsync(_classification, _context);

        Assert.AreEqual(RoutingAction.Queue, decision.Action);
        Assert.AreEqual("agent_conflict", decision.ReasonCode);
    }

    [TestMethod]
    public async Task ShouldFallbackOnNoMatch()
    {
        var message = "asdfghjkl random nonsense";
        var classification = await _classifier.ClassifyAsync(message, _tenantId);
        var decision = await _orchestrator.RouteMessageAsync(classification, _context);

        Assert.AreEqual(RoutingAction.Fallback, decision.Action);
        Assert.IsTrue(classification.RequiresHumanReview);
    }
}
```

**Tareas**:
- [ ] Tests de routing completo (intent → workflow → agent)
- [ ] Tests de ownership conflicts
- [ ] Tests de fallback scenarios
- [ ] Tests de escalation

---

### 5.3 Benchmarks Continuos (evaluation)

**Responsable**: `evaluation`

```csharp
[TestClass]
public class IntentRoutingBenchmarkTests
{
    [TestMethod]
    public async Task ShouldMeetAccuracyBenchmark()
    {
        var testSet = await LoadTestDatasetAsync(); // 1000+ mensajes etiquetados
        var metrics = await RunBenchmarkAsync(testSet);

        Assert.IsTrue(metrics.Accuracy >= 0.99f, 
            $"Accuracy {metrics.Accuracy:P} below 99% threshold");
        Assert.IsTrue(metrics.FalsePositiveRate < 0.01f, 
            $"FP rate {metrics.FalsePositiveRate:P} above 1% threshold");
        Assert.AreEqual(0, metrics.AgentCollisions, 
            "Agent collisions detected");
    }
}
```

**Tareas**:
- [ ] Crear dataset de testing (1000+ mensajes etiquetados)
- [ ] Implementar cálculo de métricas (Accuracy, Precision, Recall, F1)
- [ ] Implementar benchmark runner
- [ ] Integrar en CI/CD pipeline

---

## 📋 Fase 6: AI Assistant & Advanced (Semana 9-10)

**Duración Estimada**: 2 semanas  
**Objetivo**: Features avanzadas + AI Assistant  
**Estado**: 🔴 PENDIENTE

### 6.1 AI Assistant para Creación (frontend + core-engine)

**Responsables**: `frontend` + `core-engine`

**Backend** (core-engine):
```csharp
// Herramienta MCP: af_suggest_intent
public sealed class SuggestIntentTool : IAgentFlowMcpTool
{
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var description = req.GetParam("description");
        
        // Usar LLM para generar sugerencia
        var suggestion = await _llm.GenerateIntentSuggestionAsync(description, ct);
        
        // Detectar duplicados
        var existingIntents = await _catalog.GetTenantIntentsAsync(req.TenantId, ct);
        var duplicates = DetectDuplicates(suggestion, existingIntents);

        return McpInvokeResult.Success(Name, req.TenantId, new
        {
            suggestion,
            duplicates,
            warnings = duplicates.Any() ? "Possible duplicate intents detected" : null
        });
    }
}
```

**Frontend** (frontend):
```tsx
export default function IntentAssistantDialog({ open, onClose }) {
  const [description, setDescription] = useState('');
  const [suggestion, setSuggestion] = useState<IntentSuggestion | null>(null);

  const handleGenerate = async () => {
    const res = await axios.post(endpoints.agentflow.intents.suggest(TENANT_ID), {
      description
    });
    setSuggestion(res.data);
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>AI Intent Assistant</DialogTitle>
      <DialogContent>
        <Stack spacing={3}>
          <TextField
            fullWidth
            multiline
            rows={4}
            label="Describe the intent"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="e.g., Customer wants to report a lost card..."
          />
          <Button onClick={handleGenerate}>Generate Suggestion</Button>

          {suggestion && (
            <SuggestionCard suggestion={suggestion} onAccept={handleAccept} />
          )}
        </Stack>
      </DialogContent>
    </Dialog>
  );
}
```

**Tareas**:
- [ ] Backend: Implementar `SuggestIntentTool` MCP
- [ ] Backend: Implementar detección de duplicados
- [ ] Frontend: Crear `IntentAssistantDialog`
- [ ] Frontend: Integrar en Intent Management Page

---

## 📋 Fase 7: Observability & Ops (Semana 11-12)

**Duración Estimada**: 2 semanas  
**Objetivo**: Dashboards + alertas + docs  
**Estado**: 🔴 PENDIENTE

### 7.1 Metrics Dashboard (frontend)

**Responsable**: `frontend`

```tsx
export default function IntentMetricsDashboard() {
  const [metrics, setMetrics] = useState<IntentMetrics | null>(null);

  return (
    <Container maxWidth="xl">
      <Grid container spacing={3}>
        <Grid item xs={12} md={3}>
          <MetricCard title="Accuracy" value={`${metrics.accuracy}%`} />
        </Grid>
        <Grid item xs={12} md={3}>
          <MetricCard title="False Positives" value={`${metrics.falsePositiveRate}%`} />
        </Grid>
        <Grid item xs={12} md={3}>
          <MetricCard title="Agent Collisions" value={metrics.agentCollisions} />
        </Grid>
        <Grid item xs={12} md={3}>
          <MetricCard title="Pending Review" value={metrics.pendingReview} />
        </Grid>

        <Grid item xs={12}>
          <IntentPerformanceChart data={metrics.performanceHistory} />
        </Grid>

        <Grid item xs={12} md={6}>
          <TopIntentsCard intents={metrics.topIntents} />
        </Grid>

        <Grid item xs={12} md={6}>
          <FailedClassificationsCard failures={metrics.recentFailures} />
        </Grid>
      </Grid>
    </Container>
  );
}
```

**Tareas**:
- [ ] Crear dashboard page
- [ ] Implementar métricas cards
- [ ] Implementar charts (performance over time)
- [ ] API endpoint: `GET /api/v1/tenants/{tenantId}/intent-routing/metrics`

---

### 7.2 Alerting & Monitoring (orchestrator + governance-security)

**Responsables**: `orchestrator` + `governance-security`

**Alertas configuradas**:
```yaml
# observability/alerts/intent-routing.yml
alerts:
  - name: IntentAccuracyDegraded
    condition: accuracy < 0.95
    severity: warning
    notification: slack

  - name: IntentAccuracyCritical
    condition: accuracy < 0.90
    severity: critical
    notification: pagerduty

  - name: AgentCollisionDetected
    condition: agent_collisions > 0
    severity: critical
    notification: slack, pagerduty

  - name: HighFallbackRate
    condition: fallback_rate > 0.10
    severity: warning
    notification: slack
```

**Tareas**:
- [ ] Configurar alertas en observability layer
- [ ] Implementar métricas continuas
- [ ] Documentar runbooks de respuesta

---

## 📊 Matriz de Responsabilidades

| Fase | Componente | Agente Responsable | Dependencias |
|------|-----------|-------------------|--------------|
| 1 | Semantic Matcher | core-engine | Qdrant (data-expert) |
| 1 | Keyword Matcher | core-engine | - |
| 1 | Scoring Engine | core-engine | Matchers |
| 1 | Ownership Manager | core-engine | Redis locks |
| 1 | Vector Indexing | data-expert | Qdrant |
| 2 | Routing Orchestrator | core-engine | Scoring + Ownership |
| 2 | Inbox Service | data-expert | MongoDB |
| 2 | Engine Integration | core-engine | Orchestrator |
| 3 | Catalog Service | core-engine | YAML catalog |
| 3 | Bootstrap Service | data-expert | Catalog + Indexer |
| 4 | Intent Management UI | frontend | API |
| 4 | Playground UI | frontend | API |
| 4 | Inbox UI | frontend | API |
| 5 | Happy Path Tests | evaluation | All backend |
| 5 | Routing Tests | evaluation | Orchestrator |
| 5 | Benchmarks | evaluation | Test dataset |
| 6 | AI Assistant Backend | core-engine | LLM |
| 6 | AI Assistant UI | frontend | MCP tool |
| 7 | Metrics Dashboard | frontend | API |
| 7 | Alerting | governance-security | Observability |

---

## ✅ Checklist de Finalización

### Backend
- [ ] Todas las interfaces implementadas
- [ ] Tests unitarios ≥ 90% coverage
- [ ] Tests de integración pasando
- [ ] Benchmarks cumpliendo thresholds (Accuracy ≥ 99%)
- [ ] Documentación API completa
- [ ] Migrations de MongoDB ejecutadas

### Frontend
- [ ] Todas las pantallas implementadas
- [ ] Integración con API completa
- [ ] Tests E2E pasando
- [ ] Diseño responsive validado
- [ ] Componentes documentados

### Operaciones
- [ ] Observability configurada
- [ ] Alertas activas
- [ ] Runbooks documentados
- [ ] Capacity planning realizado
- [ ] Disaster recovery plan documentado

---

## 🚀 Orden de Ejecución Recomendado

1. **Semana 1-2**: Fase 1 (Foundation) — `core-engine` + `data-expert`
2. **Semana 3-4**: Fase 2 (Routing) — `core-engine` + `data-expert`
3. **Semana 5**: Fase 3 (Catalog) — `core-engine` + `data-expert`
4. **Semana 6-7**: Fase 4 (Frontend) — `frontend`
5. **Semana 8**: Fase 5 (Testing) — `evaluation`
6. **Semana 9-10**: Fase 6 (AI Assistant) — `frontend` + `core-engine`
7. **Semana 11-12**: Fase 7 (Observability) — `orchestrator` + `frontend`

---

## 📞 Coordinación

**Orchestrator** (yo) coordinaré:
- Revisión de código entre agentes
- Integración de componentes
- Validación de arquitectura
- Cumplimiento de Unicorn Strategy

**Daily Sync Points**:
- Fin de Fase 1: Validar Foundation
- Fin de Fase 2: Validar Routing end-to-end
- Fin de Fase 4: Validar Frontend MVP
- Fin de Fase 5: Validar Benchmarks

---

**Next Step**: Iniciar Fase 1 delegando tareas a `core-engine` y `data-expert`.
