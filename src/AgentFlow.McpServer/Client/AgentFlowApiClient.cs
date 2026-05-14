using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentFlow.McpServer.Client;

/// <summary>
/// Cliente HTTP interno que el MCP Server usa para llamar a AgentFlow.Api.
/// Configurado con la base URL y el API key interno del sidecar.
/// 
/// SEGURIDAD: Las credenciales van en la configuración, nunca hardcodeadas.
/// El API key interno debe tener alcance restringido (solo lectura + trigger).
/// </summary>
public sealed class AgentFlowApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentFlowApiClient(HttpClient http) => _http = http;

    // ─── Agents ──────────────────────────────────────────────────────────────

    public async Task<List<AgentSummary>> ListAgentsAsync(string tenantId, CancellationToken ct) =>
        await GetAsync<List<AgentSummary>>($"/api/v1/tenants/{tenantId}/agents", ct) ?? [];

    public async Task<AgentDetail?> GetAgentAsync(string tenantId, string agentId, CancellationToken ct) =>
        await GetAsync<AgentDetail>($"/api/v1/tenants/{tenantId}/agents/{agentId}", ct);

    // ─── Workflows ───────────────────────────────────────────────────────────

    public async Task<List<WorkflowSummary>> ListWorkflowsAsync(string tenantId, CancellationToken ct) =>
        await GetAsync<List<WorkflowSummary>>($"/api/v1/tenants/{tenantId}/workflows/definitions", ct) ?? [];

    public async Task<WorkflowSummary?> GetWorkflowAsync(string tenantId, string workflowId, CancellationToken ct) =>
        await GetAsync<WorkflowSummary>($"/api/v1/tenants/{tenantId}/workflows/definitions/{workflowId}", ct);

    public async Task<WorkflowExecutionResult?> TriggerWorkflowAsync(
        string tenantId, string eventName, string requestedBy,
        string? correlationId, Dictionary<string, object?>? payload, CancellationToken ct)
    {
        var body = new
        {
            eventName,
            requestedBy,
            correlationId,
            payload = payload ?? new Dictionary<string, object?>()
        };
        return await PostAsync<WorkflowExecutionResult>(
            $"/api/v1/tenants/{tenantId}/workflows/trigger", body, ct);
    }

    // ─── Channels ────────────────────────────────────────────────────────────

    public async Task<List<ChannelSummary>> ListChannelsAsync(string tenantId, CancellationToken ct) =>
        await GetAsync<List<ChannelSummary>>($"/api/v1/tenants/{tenantId}/channels", ct) ?? [];

    // ─── Sessions ────────────────────────────────────────────────────────────

    public async Task<SessionContext?> GetSessionContextAsync(
        string tenantId, string sessionId, CancellationToken ct) =>
        await GetAsync<SessionContext>($"/api/v1/tenants/{tenantId}/sessions/{sessionId}", ct);

    // ─── Integrations ────────────────────────────────────────────────────────

    public async Task<List<IntegrationSummary>> ListIntegrationsAsync(string tenantId, CancellationToken ct) =>
        await GetAsync<List<IntegrationSummary>>($"/api/v1/tenants/{tenantId}/integrations", ct) ?? [];

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(_json, ct);
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(path, body, _json, ct);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(_json, ct);
    }
}

// ─── DTOs de respuesta (mapeados desde la API) ────────────────────────────────

public sealed record AgentSummary(
    string Id, string Name, string Status, string SystemRole, bool IsSystemAgent,
    string Description, IReadOnlyList<string> Tags, DateTimeOffset CreatedAt);

public sealed record AgentDetail(
    string Id, string Name, string Status, string SystemRole, bool IsSystemAgent,
    string Description, object? Brain, IReadOnlyList<string> Tags);

public sealed record WorkflowSummary(
    string Id, string Name, string TriggerEventName, string Status,
    int Version, DateTimeOffset UpdatedAt, string UpdatedBy);

public sealed record WorkflowExecutionResult(
    string Id, string Status, string WorkflowDefinitionId, string TriggerEventName);

public sealed record ChannelSummary(
    string Id, string Name, string Type, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? LastActivityAt);

public sealed record SessionContext(
    string Id, string UserIdentifier, string? DisplayName,
    string ChannelId, string Status, bool IsExpired,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record IntegrationSummary(
    string Id, string Name, string Type, string Status);
