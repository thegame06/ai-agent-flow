---
description: Reglas de seguridad enterprise para AgentFlow — invariantes no negociables
---

# Security Rules — Invariantes No Negociables

> Estas reglas aplican a TODO el código de AgentFlow.
> Si una decisión de diseño viola alguna, debe ser rechazada y rediseñada.
> No hay excepciones sin aprobación de arquitectura documentada.

---

## REGLA 1: TenantId SIEMPRE del JWT

```csharp
// ✅ CORRECTO — del TenantContextAccessor (populado desde JWT en middleware)
var tenantId = _tenantContext.Current!.TenantId;

// ❌ NUNCA HACER
var tenantId = Request.RouteValues["tenantId"]?.ToString();  // manipulable
var tenantId = body.TenantId;                                // manipulable
var tenantId = HttpContext.Request.Headers["X-Tenant-Id"];   // manipulable
```

**Acción ante discrepancia route tenantId vs JWT tenantId**:
1. Log de seguridad con nivel Warning + userId + ips
2. Metric: `agentflow.security.violations{type="cross_tenant_attempt"}`
3. Return 403 Forbid (no 400, no 404 — nunca revelar qué existe)

---

## REGLA 2: TenantId es el PRIMER campo en índices compuestos

```javascript
// ✅ CORRECTO — MongoDB usa el índice para filtrar por tenant primero
db.agent_executions.createIndex({ tenantId: 1, status: 1, createdAt: -1 })
db.agent_executions.createIndex({ tenantId: 1, agentId: 1, createdAt: -1 })

// ❌ INCORRECTO — MongoDB no puede usar el índice de tenant eficientemente
db.agent_executions.createIndex({ status: 1, tenantId: 1, createdAt: -1 })
db.agent_executions.createIndex({ createdAt: -1, tenantId: 1 })
```

**Verificar** en startup con `EnsureIndexesAsync()` centralizado.

---

## REGLA 3: Tool Execution Pipeline — Orden Exacto e Invariable

```
1. Resolve     → ¿Existe la tool? ¿Está Active? ¿Existe en authorizedTools del agente?
2. ValidateInput → Schema JSON sin I/O (jsonschema validation pura)
3. Authorize   → ¿El usuario tiene permiso para el RiskLevel de esta tool?
4. RateLimit   → ¿No excedió cuota de calls per execution / per day?
5. Sandbox     → Si RiskLevel >= High: ejecutar en contexto aislado
6. Execute     → Invocación real
7. Audit WORM  → Siempre, incluso si falló. No throwable.
```

**La validación de schema ocurre ANTES de la autorización.**
Razón: evitar que errores de autorización revelen información sobre la existencia/schema del tool.

---

## REGLA 4: Prompt Injection → Checkpoint Inmediato

```csharp
// Patrones detectados en Think rationale o user input
private static readonly (string Pattern, string Description)[] InjectionPatterns =
[
    (@"ignore.{0,20}(previous|above|prior|all).{0,20}(instruction|prompt|rule)",
        "Instruction override attempt"),
    (@"you are now",
        "Identity override"),
    (@"your new (system|role|instruction)",
        "System prompt replacement"),
    (@"forget.{0,20}(instruction|rule|context)",
        "Context erasure"),
    (@"disregard.{0,20}(instruction|rule)",
        "Rule bypass attempt"),
    (@"<\|.*\|>",
        "Special token injection"),
    (@"system:",
        "System role injection")
];

// Acción obligatoria:
// 1. ThinkDecision = Checkpoint (no continúa el loop)
// 2. SecurityViolation metric
// 3. Audit entry (WORM)
// 4. Policy escalation si está configurada
```

---

## REGLA 5: Audit WORM — tool_execution_logs

```csharp
// Esta colección es WRITE-ONCE READ-MANY
// NUNCA llamar UpdateAsync o DeleteAsync sobre tool_execution_logs

// En MongoDB: user de app con permisos limitados:
db.createRole({
  role: "toolLogAppender",
  privileges: [{
    resource: { db: "agentflow", collection: "tool_execution_logs" },
    actions: ["insert", "find"]  // NO "update", NO "delete", NO "drop"
  }],
  roles: []
})
```

**Retención**: configurable por tenant. Fintech: mínimo 7 años (220,752,000 segundos TTL).

---

## REGLA 6: TenantContextAccessor SIEMPRE Scoped

```csharp
// ✅ CORRECTO — una instancia por request
services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();

// ❌ INCORRECTO — comparte estado entre todos los requests del proceso
services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();

// ❌ INCORRECTO — el accessor puede no estar populado al inicio de la request
services.AddTransient<ITenantContextAccessor, TenantContextAccessor>();
```

**TenantContext se construye en OnTokenValidated** — antes de llegar al controller.

---

## REGLA 7: Working Memory en Redis — TTL Obligatorio

```csharp
// Siempre con TTL igual o menor a maxExecutionSeconds del agente
await _redis.SetAsync(
    key: $"working:{executionId}",
    value: serializedState,
    expiry: TimeSpan.FromSeconds(agent.LoopConfig.MaxExecutionSeconds + 30) // buffer mínimo
);

// Al completar la ejecución: limpiar explícitamente
await _redis.DeleteAsync($"working:{executionId}");
```

**Nunca** dejar working memory sin TTL. Si el proceso muere, Redis limpia automáticamente.

---

## REGLA 8: Distributed Lock por ExecutionId

```csharp
// Antes de ejecutar cualquier agente: adquirir lock
var lockKey = $"exec-lock:{executionId}";
var lockAcquired = await _redis.SetNxAsync(lockKey, "locked", TimeSpan.FromSeconds(maxExecSeconds + 60));

if (!lockAcquired)
{
    // Ejecución ya en proceso (duplicado)
    return Result.Failure(Error.Conflict($"Execution {executionId} is already running."));
}
```

**Propósito**: Evitar que un retry del cliente procese la misma ejecución dos veces.

---

## REGLA 9: Extension Isolation

```
Tools con RiskLevel.Low  → In-process, timeout 30s, sin restricción de red
Tools con RiskLevel.Medium → In-process, timeout 15s, log obligatorio
Tools con RiskLevel.High   → Out-of-process preferred, sandbox, sin red interna
Tools con RiskLevel.Critical → Out-of-process obligatorio, MFA del user, aprobación admin del tenant
```

**Out-of-process** significa: subprocess o sidecar container con:
- Sin acceso a la red interna del cluster
- Sin acceso al filesystem del proceso principal
- Timeout estricto
- Health check requerido en startup
- Version pinning en `AgentDefinition.authorizedTools`

---

## REGLA 10: No Exception-as-Control-Flow

```csharp
// ✅ CORRECTO — control flow explícito con Result pattern
public async Task<Result<AgentDefinition>> GetAgentAsync(string id, string tenantId, CancellationToken ct)
{
    var agent = await _repo.GetByIdAsync(id, tenantId, ct);
    if (agent is null)
        return Result<AgentDefinition>.Failure(Error.NotFound("AgentDefinition"));
    if (agent.Status != AgentStatus.Published)
        return Result<AgentDefinition>.Failure(Error.Validation("status", "Agent is not published"));
    return Result<AgentDefinition>.Success(agent);
}

// ❌ INCORRECTO — control flow por excepciones
try
{
    var agent = await _repo.GetByIdAsync(id, tenantId, ct);
    // NotFoundException thrown internally
}
catch (NotFoundException ex) { return NotFound(); }
catch (InvalidStatusException ex) { return BadRequest(); }
```

Excepciones solo para condiciones **verdaderamente excepcionales** (fallas de infraestructura, bugs).

---

## REGLA 11: Secrets Management

```
Ambiente       │ Estrategia
───────────────┼────────────────────────────────────────
Development    │ dotnet user-secrets (NUNCA en appsettings)
Staging        │ Azure Key Vault / AWS Secrets Manager
Production     │ Azure Key Vault / AWS Secrets Manager / HashiCorp Vault

NUNCA en:
  ❌ appsettings.Production.json (excluido en .gitignore)
  ❌ Variables de ambiente hardcodeadas en Dockerfile
  ❌ Logs de la aplicación (sanitización obligatoria)
  ❌ Código fuente
  ❌ Base de datos (para secrets de infraestructura)
```

---

## REGLA 12: Security Headers en Cada Response

```csharp
// En Program.cs — middleware antes de UseAuthentication
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options", "DENY");
    ctx.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    ctx.Response.Headers.Append("Referrer-Policy", "no-referrer");
    ctx.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    // HSTS solo en producción con HTTPS configurado
    await next();
});
```

---

## REGLA 13: AgentDefinition Published es Inmutable

```csharp
// Si AgentDefinition.Status == Published:
// → No se permite ningún Update
// → Cualquier cambio requiere crear nueva versión (semver bump)
// → El sistema guarda snapshot de la config exacta en AgentExecution.RuntimeSnapshot

// En AgentExecutionEngine:
var runtimeSnapshot = new AgentRuntimeSnapshot
{
    AgentVersion = agentDef.Version,
    PolicySetId = agentDef.Policies.PolicySetId,
    PromptProfileId = agentDef.PromptProfile,
    ModelId = selectedModel,
    Temperature = runtimeConfig.Temperature,
    CapturedAt = DateTimeOffset.UtcNow
};
// → Persiste en AgentExecution ANTES de iniciar el loop
```

**Propósito**: Cualquier ejecución es 100% reproducible con los parámetros exactos que se usaron.
