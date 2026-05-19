# Intent Routing — Quick Start Guide for Developers

> **Para**: Equipo de desarrollo que implementará el módulo  
> **Fecha**: 2026-05-18  
> **Fase Actual**: Preparación para Fase 1 (Foundation)

---

## 🎯 Objetivo de Este Documento

Este checklist te ayudará a **empezar rápidamente** con la implementación del módulo de Intent Routing. Sigue estos pasos en orden.

---

## ✅ Pre-requisitos

Antes de empezar, asegúrate de que tienes:

- [ ] Visual Studio 2022 o VS Code con extensiones de C#
- [ ] .NET 9 SDK instalado
- [ ] Docker Desktop corriendo (para Qdrant, Redis, MongoDB)
- [ ] Acceso al repositorio AgentFlow
- [ ] Branch actualizado: `git pull origin main`

---

## 📚 Lectura Obligatoria (30 minutos)

Lee estos documentos **en orden** antes de escribir código:

1. [ ] **[INTENT-ROUTING-EXECUTIVE-SUMMARY.md](./INTENT-ROUTING-EXECUTIVE-SUMMARY.md)** (10 min)  
   → Entender el problema y la solución de alto nivel

2. [ ] **[INTENT-ROUTING-ARCHITECTURE.md](./INTENT-ROUTING-ARCHITECTURE.md)** — Secciones críticas (15 min):
   - Componentes del Sistema (página 10-15)
   - Módulos Técnicos Detallados (página 15-40)
   - Testing Strategy (página 60-70)

3. [ ] **[INTENT-ROUTING-IMPLEMENTATION-PLAN.md](./INTENT-ROUTING-IMPLEMENTATION-PLAN.md)** (5 min)  
   → Entender tu fase asignada y dependencias

---

## 🚀 Setup Inicial (15 minutos)

### 1. Verificar Infraestructura Local

```bash
# Verificar que Docker está corriendo
docker ps

# Levantar stack completo de AgentFlow (incluye Qdrant, Redis, MongoDB)
cd c:\labs\aiagents
docker-compose -f docker-compose.local.yml up -d

# Verificar que Qdrant responde
curl http://localhost:6333/health
# Esperado: {"title":"qdrant","version":"..."}

# Verificar que Redis responde
docker exec -it agentflow-redis redis-cli PING
# Esperado: PONG

# Verificar que MongoDB responde
docker exec -it agentflow-mongo mongosh --eval "db.adminCommand('ping')"
# Esperado: { ok: 1 }
```

### 2. Crear Directorio del Módulo

```bash
# Crear estructura de carpetas
mkdir -p src/AgentFlow.Intents/Classification
mkdir -p src/AgentFlow.Intents/Routing
mkdir -p src/AgentFlow.Intents/Ownership
mkdir -p src/AgentFlow.Intents/Inbox
mkdir -p src/AgentFlow.Intents/Catalog
mkdir -p src/AgentFlow.Intents/Indexing
mkdir -p src/AgentFlow.Intents/Models

# Copiar archivo base-intents.yaml (ya existe)
# Ubicación: src/AgentFlow.Intents/Catalog/base-intents.yaml
```

### 3. Crear Proyecto .NET

```bash
cd src

# Crear proyecto de librería
dotnet new classlib -n AgentFlow.Intents -f net9.0

# Agregar al solution
dotnet sln ../AgentFlow.sln add AgentFlow.Intents/AgentFlow.Intents.csproj

# Agregar referencias necesarias
cd AgentFlow.Intents
dotnet add reference ../AgentFlow.Abstractions/AgentFlow.Abstractions.csproj
dotnet add reference ../AgentFlow.Domain/AgentFlow.Domain.csproj
dotnet add reference ../AgentFlow.Infrastructure/AgentFlow.Infrastructure.csproj

# Agregar paquetes NuGet
dotnet add package MongoDB.Driver
dotnet add package StackExchange.Redis
dotnet add package YamlDotNet
dotnet add package Microsoft.Extensions.Logging
```

---

## 📝 Fase 1: Foundation — Task Breakdown

### Semana 1: Matchers

#### Task 1.1: Semantic Matcher (Asignado a: core-engine)

**Duración estimada**: 2 días

**Archivos a crear**:
```
src/AgentFlow.Intents/Classification/
├── ISemanticIntentMatcher.cs
├── QdrantSemanticIntentMatcher.cs
└── Models/
    └── IntentMatch.cs
```

**Checklist**:
- [ ] Crear interface `ISemanticIntentMatcher`
- [ ] Implementar `QdrantSemanticIntentMatcher`
- [ ] Usar `IVectorMemory` existente para búsqueda
- [ ] Implementar generación de embeddings (via ModelRouting)
- [ ] Tests unitarios con mocks (≥ 80% coverage)
- [ ] Tests de integración con Qdrant real (3-5 casos)

**Testing**:
```csharp
[TestMethod]
public async Task SemanticMatcher_ShouldFindCandidates()
{
    var matcher = new QdrantSemanticIntentMatcher(_vectorMemory, _embeddingGenerator, _logger);
    
    var candidates = await matcher.FindCandidatesAsync(
        message: "Quiero solicitar un préstamo",
        tenantId: "test-tenant",
        topK: 5);
    
    Assert.IsTrue(candidates.Count > 0);
    Assert.IsTrue(candidates.Any(c => c.IntentKey == "loan_application"));
    Assert.IsTrue(candidates.First().SimilarityScore >= 0.80f);
}
```

---

#### Task 1.2: Keyword Matcher (Asignado a: core-engine)

**Duración estimada**: 1 día

**Archivos a crear**:
```
src/AgentFlow.Intents/Classification/
├── IKeywordIntentMatcher.cs
└── KeywordIntentMatcher.cs
```

**Checklist**:
- [ ] Crear interface `IKeywordIntentMatcher`
- [ ] Implementar `KeywordIntentMatcher`
- [ ] Implementar `CalculateKeywordScore` (exact match + n-gram overlap)
- [ ] Implementar `Tokenize` helper
- [ ] Tests unitarios (≥ 90% coverage)

---

#### Task 1.3: Hybrid Scoring Engine (Asignado a: core-engine)

**Duración estimada**: 2 días

**Archivos a crear**:
```
src/AgentFlow.Intents/Classification/
├── IIntentScoringEngine.cs
├── IntentScoringEngine.cs
└── Models/
    ├── IntentClassificationResult.cs
    └── ConfidenceLevel.cs
```

**Checklist**:
- [ ] Crear interface `IIntentScoringEngine`
- [ ] Implementar `IntentScoringEngine`
- [ ] Implementar `CombineScores` (70% semantic + 20% keyword + 10% priority)
- [ ] Implementar `DetermineConfidence` (thresholds)
- [ ] Implementar `BuildExplanation` (explicabilidad)
- [ ] Tests unitarios (≥ 85% coverage)
- [ ] Tests de integración E2E (clasificación completa)

---

### Semana 2: Ownership

#### Task 1.4: Ownership Manager (Asignado a: core-engine)

**Duración estimada**: 2 días

**Archivos a crear**:
```
src/AgentFlow.Intents/Ownership/
├── IConversationOwnershipManager.cs
├── ConversationOwnershipManager.cs
└── Models/
    ├── OwnershipLock.cs
    └── ConversationOwnershipState.cs
```

**Checklist**:
- [ ] Crear interface `IConversationOwnershipManager`
- [ ] Implementar `ConversationOwnershipManager` usando Redis locks
- [ ] Implementar `TryAcquireLockAsync` con `IDistributedLockService`
- [ ] Implementar `StoreOwnershipMetadataAsync` en Redis
- [ ] Implementar `GetStateAsync`
- [ ] Tests de concurrencia (simular 2 agentes simultáneos)

**Testing crítico**:
```csharp
[TestMethod]
public async Task OwnershipManager_ShouldPreventConcurrentLocks()
{
    // Agent A adquiere lock
    var lockA = await _ownershipManager.TryAcquireLockAsync(
        "tenant-123", "conv-456", "agent-a", TimeSpan.FromMinutes(5));
    
    Assert.IsNotNull(lockA);
    
    // Agent B intenta adquirir el mismo lock (debe fallar)
    var lockB = await _ownershipManager.TryAcquireLockAsync(
        "tenant-123", "conv-456", "agent-b", TimeSpan.FromMinutes(5));
    
    Assert.IsNull(lockB, "Second agent should NOT acquire lock");
}
```

---

#### Task 1.5: Vector Indexing (Asignado a: data-expert)

**Duración estimada**: 2 días

**Archivos a crear**:
```
src/AgentFlow.Intents/Indexing/
├── IntentVectorIndexer.cs
└── IntentBootstrapService.cs
```

**Checklist**:
- [ ] Implementar `IntentVectorIndexer`
- [ ] Implementar `RebuildIndexAsync` (indexar base + custom intents)
- [ ] Implementar `BuildIntentText` (descripción + ejemplos + sinónimos)
- [ ] Implementar `IntentBootstrapService` (IHostedService)
- [ ] Registrar en `Program.cs`
- [ ] Tests de indexing completo

---

## 🧪 Testing Guidelines

### Unit Tests

**Ubicación**: `tests/AgentFlow.Tests.Unit/IntentRouting/`

**Cobertura mínima**: 85% por clase

**Ejemplo**:
```csharp
[TestClass]
public class IntentScoringEngineTests
{
    [TestMethod]
    public async Task ClassifyAsync_ShouldReturnHighConfidence_ForClearMatch()
    {
        // Arrange
        var semanticMatcher = new Mock<ISemanticIntentMatcher>();
        semanticMatcher.Setup(m => m.FindCandidatesAsync(It.IsAny<string>(), It.IsAny<string>(), null, 10, default))
            .ReturnsAsync(new List<IntentMatch>
            {
                new IntentMatch { IntentKey = "loan_application", SimilarityScore = 0.95f, MatchedVia = "semantic" }
            });
        
        var scoringEngine = new IntentScoringEngine(semanticMatcher.Object, _keywordMatcher, _logger);
        
        // Act
        var result = await scoringEngine.ClassifyAsync("Quiero solicitar un préstamo", "tenant-123");
        
        // Assert
        Assert.AreEqual("loan_application", result.BestMatch.IntentKey);
        Assert.AreEqual(ConfidenceLevel.High, result.Confidence);
        Assert.IsFalse(result.RequiresHumanReview);
    }
}
```

### Integration Tests

**Ubicación**: `tests/AgentFlow.Tests.Integration/IntentRouting/`

**Dependencias**: Requiere Docker (Qdrant, Redis, MongoDB corriendo)

**Ejemplo**:
```csharp
[TestClass]
public class IntentClassificationIntegrationTests
{
    [TestInitialize]
    public async Task Setup()
    {
        // Setup real infrastructure
        _classifier = TestContext.Services.GetRequiredService<IIntentScoringEngine>();
        await SeedTestDataAsync();
    }
    
    [TestMethod]
    public async Task E2E_Classification_ShouldWorkWithRealQdrant()
    {
        var result = await _classifier.ClassifyAsync("Mi documento fue rechazado", "test-tenant");
        
        Assert.IsNotNull(result.BestMatch);
        Assert.AreEqual("document_rejected", result.BestMatch.IntentKey);
    }
}
```

---

## 🐛 Debugging Tips

### 1. Verificar Embeddings

```csharp
var embedding = await _embeddingGenerator.GenerateAsync("test message");
Console.WriteLine($"Embedding dimension: {embedding.Length}");
// Esperado: 1536 (OpenAI) o 384 (local)
```

### 2. Verificar Qdrant Collection

```bash
# Listar colecciones
curl http://localhost:6333/collections

# Ver puntos en colección
curl http://localhost:6333/collections/intents_test-tenant/points
```

### 3. Verificar Redis Locks

```bash
# Conectar a Redis
docker exec -it agentflow-redis redis-cli

# Listar locks
KEYS "lock:conversation:*"

# Ver valor de lock
GET "lock:conversation:tenant-123:conv-456"

# TTL restante
TTL "lock:conversation:tenant-123:conv-456"
```

---

## 📊 Definition of Done

Una tarea está **completa** cuando:

- [x] Código implementado y compila sin errores
- [x] Tests unitarios escritos (coverage ≥ 85%)
- [x] Tests de integración escritos (≥ 2 casos)
- [x] Código revisado por Orchestrator o peer
- [x] Documentación XML en métodos públicos
- [x] Sin warnings de compilador
- [x] Sin deuda técnica crítica
- [x] Integrado en branch principal

---

## 🚨 Common Pitfalls

### ❌ Error 1: No usar IVectorMemory existente

**Incorrecto**:
```csharp
// NO crear nuevo cliente de Qdrant manualmente
var qdrantClient = new QdrantClient("http://localhost:6333");
```

**Correcto**:
```csharp
// Usar IVectorMemory existente del sistema
var vectorMemory = serviceProvider.GetRequiredService<IVectorMemory>();
```

---

### ❌ Error 2: No incluir tenantId en lock key

**Incorrecto**:
```csharp
var lockKey = $"conversation:lock:{conversationId}";
```

**Correcto**:
```csharp
var lockKey = $"conversation:lock:{tenantId}:{conversationId}";
```

---

### ❌ Error 3: No liberar locks en finally

**Incorrecto**:
```csharp
var lock = await _ownership.TryAcquireLockAsync(...);
await DoWorkAsync();
await _ownership.ReleaseLockAsync(lock.LockId);
```

**Correcto**:
```csharp
var lock = await _ownership.TryAcquireLockAsync(...);
try
{
    await DoWorkAsync();
}
finally
{
    if (lock != null)
    {
        await _ownership.ReleaseLockAsync(lock.LockId);
    }
}
```

---

## 📞 Getting Help

### Problemas de Arquitectura
→ Consultar con **Orchestrator Agent**

### Problemas con Qdrant/Redis/MongoDB
→ Consultar con **data-expert Agent**

### Problemas con Semantic Kernel
→ Consultar con **core-engine Agent**

### Problemas de Frontend
→ Consultar con **frontend Agent**

---

## 🎯 Next Steps

1. **Leer documentación completa** (30 min)
2. **Setup infraestructura local** (15 min)
3. **Crear proyecto AgentFlow.Intents** (10 min)
4. **Empezar con Task 1.1** (Semantic Matcher)
5. **Daily sync** con Orchestrator para desbloquear

---

**¡Éxito en la implementación!** 🚀

Este módulo es **crítico** para AgentFlow. Cualquier duda, consulta con el equipo.
