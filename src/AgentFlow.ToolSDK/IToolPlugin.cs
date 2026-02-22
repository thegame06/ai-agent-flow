using AgentFlow.Abstractions;

namespace AgentFlow.ToolSDK;

/// <summary>
/// Core interface for third-party tool plugins.
/// Implement this interface to create custom tools that can be dynamically loaded
/// and executed by the AgentFlow runtime.
/// 
/// Example: SAP connector, Salesforce API, Swift/Banking integration.
/// </summary>
public interface IToolPlugin
{
    /// <summary>
    /// Metadata describing this tool (name, version, author, description).
    /// Used for discovery, versioning, and marketplace listing.
    /// </summary>
    ToolMetadata Metadata { get; }

    /// <summary>
    /// JSON Schema describing the tool's input parameters.
    /// The LLM will use this to understand what parameters to provide.
    /// </summary>
    ToolSchema GetSchema();

    /// <summary>
    /// Execute the tool with the provided context.
    /// This is the main entry point called by the AgentFlow runtime.
    /// </summary>
    /// <param name="context">Execution context including parameters, tenant info, and user context</param>
    /// <param name="ct">Cancellation token for cooperative cancellation</param>
    /// <returns>Tool execution result with success/failure status and output data</returns>
    Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default);

    /// <summary>
    /// Optional: Validate parameters before execution.
    /// Return validation errors if parameters are invalid.
    /// If not implemented, basic schema validation is performed.
    /// </summary>
    Task<ToolValidationResult> ValidateAsync(ToolContext context, CancellationToken ct = default)
    {
        return Task.FromResult(ToolValidationResult.Success());
    }

    /// <summary>
    /// Optional: Capabilities supported by this tool (async, streaming, caching, etc).
    /// Used by the runtime to optimize execution.
    /// </summary>
    PluginCapabilities Capabilities => PluginCapabilities.Default;

    /// <summary>
    /// Policy requirements that must be satisfied before execution.
    /// Example: "pii-access", "financial-transaction", "external-api-call"
    /// </summary>
    IReadOnlyList<PolicyRequirement> RequiredPolicies => Array.Empty<PolicyRequirement>();

    /// <summary>
    /// Optional: Initialize plugin when loaded (e.g., establish connection pool).
    /// Called once when the plugin is registered.
    /// </summary>
    Task InitializeAsync(PluginConfiguration config, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Optional: Cleanup resources when plugin is unloaded.
    /// Called when the runtime shuts down or unregisters the plugin.
    /// </summary>
    Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Metadata for tool discovery and marketplace listing.
/// </summary>
public sealed record ToolMetadata
{
    /// <summary>
    /// Unique tool identifier (e.g., "sap-inventory-check", "salesforce-lead-create").
    /// Must be URL-safe and kebab-case.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name (e.g., "SAP Inventory Check").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Semantic version (e.g., "1.2.3").
    /// Breaking changes require major version bump.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Tool author/vendor (e.g., "SAP AG", "Acme Corp").
    /// </summary>
    public required string Author { get; init; }

    /// <summary>
    /// Brief description of what the tool does (1-2 sentences).
    /// Displayed in Designer UI and marketplace.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Optional: URL to documentation.
    /// </summary>
    public string? DocumentationUrl { get; init; }

    /// <summary>
    /// Optional: Tags for categorization (e.g., "finance", "crm", "database").
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional: License information (e.g., "MIT", "Commercial").
    /// </summary>
    public string? License { get; init; }

    /// <summary>
    /// Risk level for this tool (helps Policy Engine make decisions).
    /// </summary>
    public ToolRiskLevel RiskLevel { get; init; } = ToolRiskLevel.Medium;
}

/// <summary>
/// JSON Schema for tool parameters.
/// Follows JSON Schema Draft 7 specification.
/// </summary>
public sealed record ToolSchema
{
    /// <summary>
    /// Parameter definitions.
    /// Key = parameter name, Value = parameter schema.
    /// </summary>
    public required IDictionary<string, ParameterSchema> Parameters { get; init; }

    /// <summary>
    /// Required parameter names.
    /// </summary>
    public IReadOnlyList<string> Required { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional: Example usage for LLM guidance.
    /// </summary>
    public object? Example { get; init; }
}

/// <summary>
/// Schema for a single parameter.
/// </summary>
public sealed record ParameterSchema
{
    public required string Type { get; init; } // "string", "number", "boolean", "object", "array"
    public string? Description { get; init; }
    public object? DefaultValue { get; init; }
    public IReadOnlyList<string>? EnumValues { get; init; }
    public string? Pattern { get; init; } // Regex for string validation
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public ParameterSchema? Items { get; init; } // For array type
    public IDictionary<string, ParameterSchema>? Properties { get; init; } // For object type
}

/// <summary>
/// Context provided to the tool during execution.
/// </summary>
public sealed record ToolContext
{
    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// All database queries should filter by this.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// User ID who triggered the execution.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Execution ID for tracing and correlation.
    /// </summary>
    public required string ExecutionId { get; init; }

    /// <summary>
    /// Parameters provided by the LLM (validated against schema).
    /// </summary>
    public required IReadOnlyDictionary<string, object> Parameters { get; init; }

    /// <summary>
    /// Optional: Additional context from the agent execution.
    /// Can include session state, conversation history, etc.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } 
        = new Dictionary<string, string>();

    /// <summary>
    /// Optional: Timeout for this specific tool execution.
    /// Defaults to agent's ToolCallTimeout if not specified.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// Result of tool execution.
/// </summary>
public sealed record ToolResult
{
    /// <summary>
    /// Whether the tool executed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Output data from the tool (will be sent to LLM for observation).
    /// Should be JSON-serializable.
    /// </summary>
    public object? Output { get; init; }

    /// <summary>
    /// Error message if Success = false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code for programmatic error handling.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Optional: Metadata about the execution (duration, retries, etc).
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } 
        = new Dictionary<string, string>();

    /// <summary>
    /// Optional: Suggested next action for the agent.
    /// Example: "retry", "escalate", "continue"
    /// </summary>
    public string? SuggestedAction { get; init; }

    public static ToolResult FromSuccess(object output, IDictionary<string, string>? metadata = null) => new()
    {
        Success = true,
        Output = output,
        Metadata = (metadata ?? new Dictionary<string, string>()) as IReadOnlyDictionary<string, string> ?? new Dictionary<string, string>()
    };

    public static ToolResult FromError(string errorMessage, string? errorCode = null, string? suggestedAction = null) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        ErrorCode = errorCode,
        SuggestedAction = suggestedAction
    };
}

/// <summary>
/// Result of parameter validation.
/// </summary>
public sealed record ToolValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static ToolValidationResult Success() => new() { IsValid = true };
    public static ToolValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Capabilities supported by the plugin.
/// </summary>
public sealed record PluginCapabilities
{
    /// <summary>
    /// Whether this tool supports asynchronous long-running operations.
    /// If true, the tool can return immediately and send results via callback.
    /// </summary>
    public bool SupportsAsync { get; init; }

    /// <summary>
    /// Whether this tool supports streaming results (e.g., for large data sets).
    /// </summary>
    public bool SupportsStreaming { get; init; }

    /// <summary>
    /// Whether results can be cached (deterministic tools only).
    /// </summary>
    public bool IsCacheable { get; init; }

    /// <summary>
    /// Whether this tool requires network access.
    /// Used for sandboxing decisions.
    /// </summary>
    public bool RequiresNetwork { get; init; } = true;

    /// <summary>
    /// Whether this tool modifies external state (write operations).
    /// Read-only tools are less risky.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Optional: Estimated execution time in milliseconds.
    /// Used for timeout configuration.
    /// </summary>
    public int? EstimatedExecutionMs { get; init; }

    public static PluginCapabilities Default => new()
    {
        SupportsAsync = false,
        SupportsStreaming = false,
        IsCacheable = false,
        RequiresNetwork = true,
        IsReadOnly = false
    };
}

/// <summary>
/// Policy requirement for tool execution.
/// </summary>
public sealed record PolicyRequirement
{
    /// <summary>
    /// Policy group ID (e.g., "pii-access", "financial-transaction").
    /// Must match a policy group defined in the tenant's policy store.
    /// </summary>
    public required string PolicyGroupId { get; init; }

    /// <summary>
    /// Whether this policy is mandatory (true) or advisory (false).
    /// </summary>
    public bool IsMandatory { get; init; } = true;

    /// <summary>
    /// Optional: Reason for this requirement (for audit trail).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Configuration provided during plugin initialization.
/// </summary>
public sealed record PluginConfiguration
{
    /// <summary>
    /// Tenant-specific configuration (connection strings, API keys, etc).
    /// Stored securely in the tenant's configuration store.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>
    /// Optional: Environment (dev, staging, production).
    /// </summary>
    public string? Environment { get; init; }
}

/// <summary>
/// Risk level classification for tools.
/// </summary>
public enum ToolRiskLevel
{
    /// <summary>
    /// No external access, deterministic, read-only (e.g., string formatting).
    /// </summary>
    Low = 0,

    /// <summary>
    /// Read-only external access (e.g., API queries, database reads).
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Write operations, financial transactions, PII access.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical operations requiring multiple approvals (e.g., money transfer, data deletion).
    /// </summary>
    Critical = 3
}
