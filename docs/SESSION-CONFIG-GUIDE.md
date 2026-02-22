# 🎛️ SESSION CONFIGURATION GUIDE

**Documento**: Configuración de Sesiones Multi-Turn por Agente  
**Fecha**: 2026-02-22  
**Status**: GUÍA TÉCNICA

---

## 🎯 RESUMEN EJECUTIVO

Cada **AgentDefinition** tiene un `SessionConfig` que controla:
- ✅ Si el agente soporta conversaciones multi-turn  
- ⏱️ Cuánto tiempo duran las sesiones (TTL)  
- 🔢 Máximo de turnos por sesión  
- 🧠 Cuánto contexto histórico enviar al LLM  

---

## 📐 ARQUITECTURA DE CAPAS

```
┌────────────────────────────────────────────────────────┐
│ Layer 1: AgentDefinition.SessionConfig                 │  ← Configuración por agente
│ ─────────────────────────────────────────────────────  │
│ • EnableThreads: true/false                            │
│ • DefaultThreadTtl: TimeSpan (1hr - 30 days)           │
│ • MaxTurnsPerThread: int (10-1000)                     │
│ • ContextWindowSize: int (5-50)                        │
└────────────────┬───────────────────────────────────────┘
                 │ Config cargada en runtime
                 ▼
┌────────────────────────────────────────────────────────┐
│ Layer 2: ConversationThread (MongoDB)                  │  ← Sesión persistente
│ ─────────────────────────────────────────────────────  │
│ • ThreadId: ObjectId                                   │
│ • AgentDefinitionId: string                            │
│ • Turns: List<ConversationTurn>                        │
│ • TokenStats, ExpiresAt (from SessionConfig.TTL)       │
└────────────────┬───────────────────────────────────────┘
                 │ Load recent N turns (ContextWindowSize)
                 ▼
┌────────────────────────────────────────────────────────┐
│ Layer 3: SemanticKernel ChatHistory (in-memory)        │  ← LLM context window
│ ─────────────────────────────────────────────────────  │
│ • SystemMessage(prompt)                                │
│ • UserMessage(turn 1)                                  │
│ • AssistantMessage(response 1)                         │
│ • ...                                                  │
│ • UserMessage(turn N - current)                        │
└────────────────┬───────────────────────────────────────┘
                 │ Send to LLM
                 ▼
             [GPT-4o / Claude]
```

---

## 🛠️ CONFIGURACIÓN POR TIPO DE AGENTE

### 1. Chatbot Support (Multi-Turn Habilitado)

```csharp
var supportAgent = AgentDefinition.Create(
    tenantId: "acme-corp",
    name: "Support Agent",
    description: "Customer support chatbot with conversation memory",
    brain: new BrainConfiguration { ModelId = "gpt-4o", Temperature = 0.7f },
    loopConfig: new AgentLoopConfig(),
    memory: new MemoryConfig { EnableWorkingMemory = true },
    session: new SessionConfig
    {
        EnableThreads = true,                       // ✅ Multi-turn enabled
        DefaultThreadTtl = TimeSpan.FromHours(2),   // 2 hours (support convo)
        MaxTurnsPerThread = 50,                     // Max 50 questions
        ContextWindowSize = 10,                     // Last 10 turns in LLM
        AutoCreateThread = true,                    // Auto-create on first msg
        ThreadKeyPattern = "support-{userId}-{date}"
    },
    ownerUserId: "admin@acme.com"
);
```

**Resultado**:
- Cada usuario automáticamente obtiene su propio thread
- Thread expira después de 2 horas de inactividad
- El agente "recuerda" las últimas 10 interacciones

---

### 2. Workflow Agent (Sin Multi-Turn)

```csharp
var approvalAgent = AgentDefinition.Create(
    tenantId: "acme-corp",
    name: "Invoice Approval Workflow",
    description: "One-shot invoice processing",
    brain: new BrainConfiguration { ModelId = "gpt-4o-mini", Temperature = 0.1f },
    loopConfig: new AgentLoopConfig(),
    memory: new MemoryConfig { EnableLongTermMemory = true },
    session: new SessionConfig
    {
        EnableThreads = false,           // ❌ Each execution is stateless
        AutoCreateThread = false
    },
    ownerUserId: "admin@acme.com"
);
```

**Resultado**:
- Cada ejecución es independiente
- No hay overhead de carga de threads
- Ideal para workflows determinísticos

---

### 3. Long-Running Research Agent

```csharp
var researchAgent = AgentDefinition.Create(
    tenantId: "research-lab",
    name: "Research Assistant",
    description: "Long-term research conversations",
    brain: new BrainConfiguration { ModelId = "gpt-4o", Temperature = 0.8f },
    loopConfig: new AgentLoopConfig(),
    memory: new MemoryConfig 
    { 
        EnableLongTermMemory = true,
        EnableVectorMemory = true,
        VectorCollectionName = "research_knowledge"
    },
    session: new SessionConfig
    {
        EnableThreads = true,                        
        DefaultThreadTtl = TimeSpan.FromDays(30),   // ✅ 30 days for research
        MaxTurnsPerThread = 500,                    // Very long conversations
        ContextWindowSize = 20,                     // Large context window
        EnableSummarization = true,                 // Summarize old turns
        ThreadKeyPattern = "research-{agentName}-{guid}"
    },
    ownerUserId: "researcher@lab.com"
);
```

**Resultado**:
- Sesiones de investigación pueden durar semanas
- Turns antiguos se resumen para ahorrar tokens
- Vector memory complementa el contexto conversacional

---

## 📊 TABLA DE CONFIGURACIONES RECOMENDADAS

| Use Case                  | EnableThreads | ThreadTtl      | MaxTurns | ContextWindow | Summarization |
|---------------------------|---------------|----------------|----------|---------------|---------------|
| **Customer Support**      | ✅ true       | 1-2 hours      | 50       | 10            | ❌ false      |
| **Sales Chatbot**         | ✅ true       | 24 hours       | 100      | 15            | ✅ true       |
| **Workflow Engine**       | ❌ false      | N/A            | N/A      | N/A           | ❌ false      |
| **Research Assistant**    | ✅ true       | 30 days        | 500      | 20            | ✅ true       |
| **Code Review Agent**     | ✅ true       | 7 days         | 200      | 15            | ✅ true       |
| **Data Analysis**         | ❌ false      | N/A            | N/A      | N/A           | ❌ false      |
| **Training/Tutoring**     | ✅ true       | 7 days         | 100      | 12            | ❌ false      |
| **Agent-as-Tool**         | ❌ false      | Inherits       | Inherits | Inherits      | ❌ false      |

---

## 🔄 FLUJO DE EJECUCIÓN

### Escenario 1: Primera ejecución (sin ThreadId)

```csharp
// 1. User envía mensaje sin ThreadId
POST /api/v1/tenants/acme/agents/support-agent/execute
{
  "userMessage": "I need help with my order",
  "threadId": null  // ← No hay thread
}

// 2. Engine verifica SessionConfig
var agentDef = await _agentRepo.GetByIdAsync("support-agent", "acme");
if (agentDef.Session.EnableThreads && agentDef.Session.AutoCreateThread)
{
    // 3. Crea nuevo thread
    var thread = ConversationThread.Create(
        tenantId: "acme",
        threadKey: "support-user123-2026-02-22",  // from pattern
        agentDefinitionId: agentDef.Id,
        userId: "user123",
        expiresIn: agentDef.Session.DefaultThreadTtl,  // ← 2 hours
        maxTurns: agentDef.Session.MaxTurnsPerThread    // ← 50
    );
    
    await _threadRepo.InsertAsync(thread);
}

// 4. Ejecuta agente normalmente
var execution = await _executor.ExecuteAsync(...);

// 5. Append result to thread
thread.AppendExecution(execution.Id, tokensUsed, userMessage, response);
await _threadRepo.UpdateAsync(thread);

// 6. Response incluye ThreadId para siguiente turno
return new ExecutionResult
{
    ExecutionId = execution.Id,
    ThreadId = thread.Id,  // ✅ Frontend guarda este ID
    Response = "I'm here to help! What's your order number?"
};
```

---

### Escenario 2: Continuación de conversación (con ThreadId)

```csharp
// 1. User envía segundo mensaje CON ThreadId
POST /api/v1/tenants/acme/agents/support-agent/execute
{
  "userMessage": "Order #12345",
  "threadId": "65f8a9b1c2d3e4f5a6b7c8d9"  // ← Thread existente
}

// 2. Engine carga thread
var thread = await _threadRepo.GetByIdAsync(request.ThreadId, "acme");

if (thread is null)
    return Error("Thread expired or not found");

// 3. Construye ChatHistory desde Thread
var snapshot = thread.GetChatHistory(agentDef.Session.ContextWindowSize);
// → Devuelve últimos 10 turns

// 4. SemanticKernelBrain usa snapshot
var history = new ChatHistory();
history.AddSystemMessage(systemPrompt);

foreach (var turn in snapshot.RecentTurns)
{
    history.AddUserMessage(turn.UserMessage);
    if (turn.AssistantResponse is not null)
        history.AddAssistantMessage(turn.AssistantResponse);
}

history.AddUserMessage(request.UserMessage);  // Current turn

// 5. Send to LLM con contexto completo
var response = await _chatCompletion.GetChatMessageContentAsync(history, ...);

// 6. Update thread con nuevo turn
thread.AppendExecution(...);
```

---

## 🔐 SEGURIDAD & GOBERNANZA

### 1. Validación de Ownership

```csharp
// Middleware: Verify user owns the thread
public async Task<ConversationThread?> GetByIdAsync(string threadId, string userId, string tenantId)
{
    var thread = await _collection.Find(x => 
        x.Id == threadId && 
        x.TenantId == tenantId &&
        x.UserId == userId &&  // ✅ Owner validation
        x.Deleted == false
    ).FirstOrDefaultAsync();
    
    return thread;
}
```

### 2. Auto-Archival en Expiración

```csharp
// MongoDB TTL Index (auto-delete threads)
_collection.Indexes.CreateOne(new CreateIndexModel<ConversationThread>(
    Builders<ConversationThread>.IndexKeys.Ascending(x => x.ExpiresAt),
    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }
));
```

### 3. GDPR Compliance

```csharp
// Hard delete on user request
public async Task<Result> DeleteUserThreadsAsync(string userId, string tenantId)
{
    // Physical deletion (not soft delete)
    await _collection.DeleteManyAsync(x => 
        x.UserId == userId && 
        x.TenantId == tenantId
    );
    
    // Also clear working memory
    await _memory.ClearAllThreadsAsync(userId);
    
    return Result.Success();
}
```

---

## 💡 EJEMPLOS DE USO - FRONTEND

### TypeScript: Ejecutar con Thread Continuity

```typescript
// components/ChatInterface.tsx
import { useState } from 'react';

interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

export function ChatInterface({ agentId }: { agentId: string }) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [threadId, setThreadId] = useState<string | null>(null);
  const [input, setInput] = useState('');

  const sendMessage = async () => {
    // 1. Add user message to UI
    const userMsg: ChatMessage = { role: 'user', content: input };
    setMessages([...messages, userMsg]);

    // 2. Call API with threadId (if exists)
    const response = await fetch(`/api/v1/tenants/acme/agents/${agentId}/execute`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userMessage: input,
        threadId: threadId  // ✅ null first time, then persisted
      })
    });

    const result = await response.json();

    // 3. Save threadId for next turn
    if (!threadId && result.threadId) {
      setThreadId(result.threadId);
      console.log('Thread created:', result.threadId);
    }

    // 4. Add assistant response
    const assistantMsg: ChatMessage = { role: 'assistant', content: result.response };
    setMessages([...messages, userMsg, assistantMsg]);

    setInput('');
  };

  return (
    <div>
      {/* Chat UI */}
      <div>
        {messages.map((msg, idx) => (
          <div key={idx} className={msg.role}>
            {msg.content}
          </div>
        ))}
      </div>

      <input 
        value={input} 
        onChange={e => setInput(e.target.value)}
        onKeyPress={e => e.key === 'Enter' && sendMessage()}
      />
    </div>
  );
}
```

---

## 🚀 MIGRATION PATH

### Fase 1: Backward Compatibility (Default SessionConfig)

Todos los agentes existentes obtienen:
```csharp
new SessionConfig
{
    EnableThreads = false,  // ← Legacy mode (stateless)
    AutoCreateThread = false
}
```

### Fase 2: Opt-In per Agent

Cada equipo habilita threads según su caso de uso:
```bash
# Designer UI: Toggle "Enable Multi-Turn Conversations"
# → Sets Session.EnableThreads = true
```

### Fase 3: Default Change (futuro)

Nuevos agentes por defecto con threads habilitados:
```csharp
new SessionConfig
{
    EnableThreads = true,
    DefaultThreadTtl = TimeSpan.FromDays(7),
    MaxTurnsPerThread = 100,
    ContextWindowSize = 10
}
```

---

## 📈 MÉTRICAS & OBSERVABILITY

### KPIs a Monitorear

```csharp
// ThreadMetrics published to Observability layer
public record ThreadMetrics
{
    public string AgentId { get; init; }
    public int ActiveThreads { get; init; }
    public int ExpiredThreads { get; init; }
    public double AvgTurnsPerThread { get; init; }
    public double AvgThreadDurationMinutes { get; init; }
    public int ThreadsExceedingMaxTurns { get; init; }
}
```

### Alertas Recomendadas

```yaml
Alert: TooManyActiveThreads
Condition: ActiveThreads > 10000
Action: Scale Redis / Review TTL configs

Alert: ThreadsHittingMaxTurns
Condition: ThreadsExceedingMaxTurns > 5% of total
Action: Increase MaxTurnsPerThread or enable summarization

Alert: AvgTurnsTooLow
Condition: AvgTurnsPerThread < 2
Action: Investigate abandonment rate (UX issue?)
```

---

## ✅ CHECKLIST DE IMPLEMENTACIÓN

### Backend
- [x] `SessionConfig` value object created
- [x] `AgentDefinition.Session` property added
- [ ] `ConversationThread` aggregate implemented
- [ ] `IConversationThreadRepository` + MongoDB impl
- [ ] `AgentExecutionEngine` integration
- [ ] `SemanticKernelBrain` thread context loading
- [ ] Real token tracking from LLM metadata
- [ ] Thread archival background job
- [ ] API endpoints: `/threads`, `/threads/{id}/messages`

### Frontend
- [ ] Chat UI component with thread persistence
- [ ] ThreadId state management (localStorage + useState)
- [ ] Thread history viewer
- [ ] Session config editor in Designer
- [ ] Thread metrics dashboard

### Testing
- [ ] Unit tests: SessionConfig validation
- [ ] Integration tests: Thread CRUD
- [ ] E2E tests: Multi-turn conversation flow
- [ ] Load tests: 10K concurrent threads

---

**Next Step**: Implementar `ConversationThread` aggregate según [SESSION-MANAGEMENT-RECOMMENDATIONS.md](./SESSION-MANAGEMENT-RECOMMENDATIONS.md)

