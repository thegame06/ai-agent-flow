# AgentFlow Tool SDK

**Build custom tools for AgentFlow AI agents** — The enterprise-grade plugin system for AI agent orchestration.

## 🎯 What is This?

AgentFlow Tool SDK enables third-party developers to create custom tools that can be dynamically loaded and executed by AgentFlow agents. Think of it as the **npm/NuGet for AI agent capabilities**.

### Why Tool SDK?

| Without SDK | With SDK |
|---|---|
| Hardcode tools in engine | Load tools dynamically |
| Rebuild engine for new integrations | Drop DLL in plugins folder |
| Limited to built-in tools | Unlimited extensibility |
| Vendor lock-in | Community marketplace |

---

## 🚀 Quick Start (5 Minutes)

### 1. Install

```bash
dotnet add package AgentFlow.ToolSDK
```

### 2. Create Your First Plugin

```csharp
using AgentFlow.ToolSDK;

public class WeatherPlugin : IToolPlugin
{
    public ToolMetadata Metadata => new()
    {
        Id = "weather-check",
        Name = "Weather Check",
        Version = "1.0.0",
        Author = "Your Name",
        Description = "Get current weather for a city",
        Tags = new[] { "weather", "api" },
        RiskLevel = ToolRiskLevel.Low
    };

    public ToolSchema GetSchema() => new()
    {
        Parameters = new Dictionary<string, ParameterSchema>
        {
            ["city"] = new()
            {
                Type = "string",
                Description = "City name (e.g., 'London', 'New York')"
            }
        },
        Required = new[] { "city" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct)
    {
        var city = context.Parameters["city"].ToString();
        
        // Call your weather API here
        var weather = await GetWeatherAsync(city);
        
        return ToolResult.FromSuccess(new
        {
            city = city,
            temperature = weather.Temp,
            condition = weather.Condition
        });
    }
    
    // ... rest of implementation
}
```

### 3. Register and Use

```csharp
var registry = new PluginRegistry();
await registry.RegisterPluginAsync(new WeatherPlugin());

// Execute
var result = await registry.ExecutePluginAsync(
    "weather-check",
    new ToolContext
    {
        TenantId = "tenant-123",
        UserId = "user-456",
        ExecutionId = "exec-789",
        Parameters = new Dictionary<string, object>
        {
            ["city"] = "London"
        }
    });

Console.WriteLine(result.Success); // true
Console.WriteLine(result.Output);  // { city: "London", temperature: 15, ... }
```

---

## 📚 Core Concepts

### IToolPlugin Interface

Every plugin implements `IToolPlugin`:

```csharp
public interface IToolPlugin
{
    ToolMetadata Metadata { get; }               // Who, what, version
    ToolSchema GetSchema();                      // Parameters schema
    Task<ToolResult> ExecuteAsync(...);          // Main execution
    Task<ToolValidationResult> ValidateAsync(...); // Optional validation
    PluginCapabilities Capabilities { get; }     // Features (async, caching, etc)
    IReadOnlyList<PolicyRequirement> RequiredPolicies { get; } // Security policies
}
```

### ToolContext

Execution context provided by the runtime:

```csharp
public record ToolContext
{
    string TenantId;        // Multi-tenant isolation
    string UserId;          // Who triggered this
    string ExecutionId;     // Trace correlation
    IReadOnlyDictionary<string, object> Parameters;  // LLM-provided params
    IReadOnlyDictionary<string, string> Metadata;    // Optional context
    TimeSpan? Timeout;      // Execution timeout
}
```

### ToolResult

Return type for all executions:

```csharp
// Success
return ToolResult.FromSuccess(new { data = "..." });

// Error
return ToolResult.FromError("Connection failed", "CONN_ERROR", "retry");
```

---

## 🏗️ Reference Implementations

We provide 2 production-ready examples:

### 1. SQL Query Plugin

Execute read-only SQL queries (SQL Server):

```csharp
var sqlPlugin = new SqlQueryPlugin(connectionString);
await registry.RegisterPluginAsync(sqlPlugin);

// Agent can now call:
// "Find all customers in the USA"
// → SELECT * FROM Customers WHERE Country = @country
```

**Features**:
- ✅ Parameterized queries (prevents SQL injection)
- ✅ Read-only enforcement (SELECT only)
- ✅ Row limit protection (max 1000 rows)
- ✅ Timeout handling
- ✅ Policy requirement: `database-access`

### 2. REST API Plugin

Make HTTP calls to external APIs:

```csharp
var httpPlugin = new RestApiPlugin(new HttpClient());
await registry.RegisterPluginAsync(httpPlugin);

// Agent can now call:
// "Create a customer in Salesforce"
// → POST https://api.salesforce.com/customers
```

**Features**:
- ✅ HTTPS enforcement
- ✅ Custom headers (auth, API keys)
- ✅ JSON body support
- ✅ Timeout configuration
- ✅ Policy requirement: `external-api-call`

---

## 🔐 Security & Governance

### Risk Levels

Classify your tool's risk:

```csharp
public enum ToolRiskLevel
{
    Low,      // No external access (e.g., string formatting)
    Medium,   // Read-only external access
    High,     // Write operations, PII access
    Critical  // Financial transactions, data deletion
}
```

### Policy Requirements

Declare what policies must be satisfied:

```csharp
public IReadOnlyList<PolicyRequirement> RequiredPolicies => new[]
{
    new PolicyRequirement
    {
        PolicyGroupId = "pii-access",
        IsMandatory = true,
        Reason = "Accesses customer personal data"
    }
};
```

AgentFlow's Policy Engine will enforce these before execution.

### Multi-Tenant Isolation

**CRITICAL**: Always filter by `context.TenantId`:

```csharp
// ✅ CORRECT
var query = "SELECT * FROM Customers WHERE TenantId = @tenantId AND Country = @country";

// ❌ WRONG (data leak!)
var query = "SELECT * FROM Customers WHERE Country = @country";
```

---

## 🎛️ Advanced Features

### Capabilities

Declare what your plugin supports:

```csharp
public PluginCapabilities Capabilities => new()
{
    SupportsAsync = true,        // Can return immediately, callback later
    SupportsStreaming = true,    // Can stream large results
    IsCacheable = true,          // Results can be cached
    RequiresNetwork = true,      // Needs network access
    IsReadOnly = false,          // Makes write operations
    EstimatedExecutionMs = 500   // Expected runtime
};
```

### Async Operations

For long-running tasks:

```csharp
public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct)
{
    if (Capabilities.SupportsAsync)
    {
        // Start background job
        var jobId = await StartBackgroundJobAsync(...);
        
        return ToolResult.FromSuccess(new
        {
            status = "processing",
            jobId = jobId,
            checkUrl = $"/api/jobs/{jobId}"
        });
    }
    
    // Otherwise, block until complete
    var result = await DoWorkAsync(...);
    return ToolResult.FromSuccess(result);
}
```

### Initialization & Cleanup

Manage resources across executions:

```csharp
public async Task InitializeAsync(PluginConfiguration config, CancellationToken ct)
{
    // Called once when plugin loads
    _connectionPool = CreateConnectionPool(config.Settings["ConnectionString"]);
}

public async Task DisposeAsync()
{
    // Called when plugin unloads
    await _connectionPool.DisposeAsync();
}
```

---

## 📦 Packaging for Distribution

### Create NuGet Package

```bash
dotnet pack -c Release
```

### Publish to NuGet

```bash
dotnet nuget push bin/Release/YourPlugin.1.0.0.nupkg \
  --api-key YOUR_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

### Load from Assembly

```csharp
var registry = new PluginRegistry();

// Load all plugins from DLL
var result = await registry.LoadPluginsFromAssemblyAsync("plugins/MyPlugin.dll");
Console.WriteLine($"Loaded {result.Value} plugins");
```

---

## 🏆 Best Practices

### 1. Validation is Critical

```csharp
public async Task<ToolValidationResult> ValidateAsync(ToolContext context, CancellationToken ct)
{
    var errors = new List<string>();
    
    if (!context.Parameters.TryGetValue("email", out var email))
        errors.Add("Email is required");
    else if (!IsValidEmail(email.ToString()))
        errors.Add("Email format is invalid");
    
    return errors.Any()
        ? ToolValidationResult.Failure(errors.ToArray())
        : ToolValidationResult.Success();
}
```

### 2. Error Handling

```csharp
try
{
    var result = await CallExternalApiAsync(...);
    return ToolResult.FromSuccess(result);
}
catch (HttpRequestException ex)
{
    return ToolResult.FromError(
        $"API call failed: {ex.Message}",
        "HTTP_ERROR",
        "retry"  // Suggest retry
    );
}
catch (Exception ex)
{
    return ToolResult.FromError(
        $"Unexpected error: {ex.Message}",
        "UNEXPECTED_ERROR",
        "escalate"  // Suggest human review
    );
}
```

### 3. Timeout Awareness

```csharp
var timeout = context.Timeout ?? TimeSpan.FromSeconds(30);
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(timeout);

var result = await DoWorkAsync(cts.Token);
```

### 4. Logging & Observability

```csharp
return ToolResult.FromSuccess(result, new Dictionary<string, string>
{
    ["ExecutionTimeMs"] = stopwatch.ElapsedMilliseconds.ToString(),
    ["ApiVersion"] = "v2",
    ["RetryCount"] = retryCount.ToString()
});
```

---

## 🌍 Real-World Examples

### SAP Inventory Check

```csharp
public class SAPInventoryPlugin : IToolPlugin
{
    public ToolMetadata Metadata => new()
    {
        Id = "sap-inventory-check",
        Name = "SAP Inventory Check",
        Version = "1.0.0",
        Author = "SAP AG",
        Description = "Check product inventory in SAP ERP",
        RiskLevel = ToolRiskLevel.Medium
    };
    
    // ... implementation calls SAP RFC
}
```

### Salesforce Lead Creation

```csharp
public class SalesforceCRMPlugin : IToolPlugin
{
    public ToolMetadata Metadata => new()
    {
        Id = "salesforce-lead-create",
        Name = "Salesforce Lead Creation",
        Version = "2.1.0",
        Author = "Salesforce Inc",
        Description = "Create leads in Salesforce CRM",
        RiskLevel = ToolRiskLevel.High // Writes to external system
    };
    
    // ... implementation uses Salesforce REST API
}
```

### Swift Payment Initiation

```csharp
public class SwiftPaymentPlugin : IToolPlugin
{
    public ToolMetadata Metadata => new()
    {
        Id = "swift-payment-init",
        Name = "SWIFT Payment Initiation",
        Version = "1.0.0",
        Author = "Banking Corp",
        Description = "Initiate SWIFT wire transfers",
        RiskLevel = ToolRiskLevel.Critical // Financial transaction
    };
    
    public IReadOnlyList<PolicyRequirement> RequiredPolicies => new[]
    {
        new PolicyRequirement { PolicyGroupId = "financial-transaction" },
        new PolicyRequirement { PolicyGroupId = "dual-approval" }
    };
    
    // ... implementation requires multi-factor auth
}
```

---

## 🧪 Testing Your Plugin

```csharp
[Fact]
public async Task ExecuteAsync_ValidCity_ReturnsWeather()
{
    // Arrange
    var plugin = new WeatherPlugin();
    var context = new ToolContext
    {
        TenantId = "test-tenant",
        UserId = "test-user",
        ExecutionId = "test-exec",
        Parameters = new Dictionary<string, object>
        {
            ["city"] = "London"
        }
    };
    
    // Act
    var result = await plugin.ExecuteAsync(context, CancellationToken.None);
    
    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.Output);
}
```

---

## 📋 Checklist Before Publishing

- [ ] Implemented `IToolPlugin` interface
- [ ] Defined clear `ToolMetadata` (id, name, version, description)
- [ ] Created comprehensive `ToolSchema` with examples
- [ ] Handled all error cases gracefully
- [ ] Validated input parameters
- [ ] Respected `context.TenantId` for multi-tenant isolation
- [ ] Declared `RequiredPolicies` for governance
- [ ] Set appropriate `RiskLevel`
- [ ] Added unit tests (>80% coverage)
- [ ] Documented usage in README
- [ ] Tested with real AgentFlow runtime
- [ ] Optimized for performance (caching, connection pooling)
- [ ] Implemented timeout handling
- [ ] Added logging/observability metadata

---

## 🤝 Contributing

1. Fork the repo
2. Create your plugin
3. Submit PR with:
   - Plugin code
   - Tests
   - Documentation
   - Example usage

---

## 📄 License

MIT — See LICENSE file

---

## 🆘 Support

- **Docs**: https://docs.agentflow.dev/sdk
- **Issues**: https://github.com/agentflow/sdk/issues
- **Discord**: https://discord.gg/agentflow
- **Email**: sdk@agentflow.dev

---

**Made with ❤️ by the AgentFlow Team**

*Build the future of AI agents, one plugin at a time.*
