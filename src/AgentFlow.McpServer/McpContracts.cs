namespace AgentFlow.McpServer;

// ─── Contrato de entrada que todos los agentes envían al MCP server ───────────

public sealed record McpInvokeRequest
{
    public required string Tool { get; init; }
    public required string TenantId { get; init; }
    public string? ExecutionId { get; init; }
    public string? InputJson { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

// ─── Respuesta estándar del MCP server ───────────────────────────────────────

public sealed record McpInvokeResult
{
    public required bool Ok { get; init; }
    public required string Tool { get; init; }
    public string? TenantId { get; init; }
    public string? ExecutionId { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public static McpInvokeResult Success(string tool, string tenantId, object data, string? executionId = null) =>
        new() { Ok = true, Tool = tool, TenantId = tenantId, Data = data, ExecutionId = executionId };

    public static McpInvokeResult Fail(string tool, string error) =>
        new() { Ok = false, Tool = tool, Error = error };
}

// ─── Descriptor de cada tool para el catálogo GET /tools ─────────────────────

public sealed record McpToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// Who can use this tool: "router" | "config-assistant" | "any"
    /// </summary>
    public required string IntendedFor { get; init; }

    /// <summary>
    /// JSON Schema describing the input parameters.
    /// </summary>
    public required string InputSchemaJson { get; init; }
}
