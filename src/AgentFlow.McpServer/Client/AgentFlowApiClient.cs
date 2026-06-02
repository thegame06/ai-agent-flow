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

    public async Task<JsonElement?> ListCampaignsAsync(string tenantId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns", ct);

    public async Task<JsonElement?> GetCampaignAsync(string tenantId, string campaignId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}", ct);

    public async Task<JsonElement?> CreateCampaignAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns", body, ct);

    public async Task<JsonElement?> UpdateCampaignAsync(string tenantId, string campaignId, object body, CancellationToken ct) =>
        await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}", body, ct);

    public async Task<JsonElement?> PublishCampaignAsync(string tenantId, string campaignId, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}/publish", new { }, ct);

    public async Task<JsonElement?> PauseCampaignAsync(string tenantId, string campaignId, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}/pause", new { }, ct);

    public async Task<JsonElement?> ResumeCampaignAsync(string tenantId, string campaignId, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}/resume", new { }, ct);

    public async Task<JsonElement?> SimulateCampaignAsync(string tenantId, string campaignId, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}/simulate", new { }, ct);

    public async Task<JsonElement?> RunCampaignNowAsync(string tenantId, string campaignId, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}/run-now", new { }, ct);

    public async Task<JsonElement?> GetCampaignRunsAsync(string tenantId, string? campaignId, int limit, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-runs?campaignId={Uri.EscapeDataString(campaignId ?? string.Empty)}&limit={Math.Clamp(limit, 1, 500)}", ct);

    public async Task<JsonElement?> GetCampaignRunAsync(string tenantId, string runId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-runs/{Uri.EscapeDataString(runId)}", ct);

    public async Task<JsonElement?> RetryCampaignFailuresAsync(string tenantId, string runId, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-runs/{Uri.EscapeDataString(runId)}/retry-failures", new { }, ct);

    public async Task<JsonElement?> GetCampaignContactResultsAsync(string tenantId, string runId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-runs/{Uri.EscapeDataString(runId)}/contacts", ct);

    public async Task<JsonElement?> GetCampaignMetricsAsync(string tenantId, string campaignId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaigns/{Uri.EscapeDataString(campaignId)}/metrics", ct);

    public async Task<JsonElement?> ListCampaignSegmentsAsync(string tenantId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-segments", ct);

    public async Task<JsonElement?> GetCampaignSegmentAsync(string tenantId, string segmentId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-segments/{Uri.EscapeDataString(segmentId)}", ct);

    public async Task<JsonElement?> CreateCampaignSegmentAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-segments", body, ct);

    public async Task<JsonElement?> UpdateCampaignSegmentAsync(string tenantId, string segmentId, object body, CancellationToken ct) =>
        await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-segments/{Uri.EscapeDataString(segmentId)}", body, ct);

    public async Task<JsonElement?> PreviewCampaignSegmentAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-segments/preview", body, ct);

    public async Task<JsonElement?> PreviewCampaignSegmentByIdAsync(string tenantId, string segmentId, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-segments/{Uri.EscapeDataString(segmentId)}/preview", new { }, ct);

    public async Task<JsonElement?> ListCampaignCallPlaybooksAsync(string tenantId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-call-playbooks", ct);

    public async Task<JsonElement?> GetCampaignCallPlaybookAsync(string tenantId, string playbookId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-call-playbooks/{Uri.EscapeDataString(playbookId)}", ct);

    public async Task<JsonElement?> CreateCampaignCallPlaybookAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-call-playbooks", body, ct);

    public async Task<JsonElement?> UpdateCampaignCallPlaybookAsync(string tenantId, string playbookId, object body, CancellationToken ct) =>
        await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-call-playbooks/{Uri.EscapeDataString(playbookId)}", body, ct);

    public async Task<JsonElement?> ListCampaignCallOutcomesAsync(string tenantId, string runId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-runs/{Uri.EscapeDataString(runId)}/call-outcomes", ct);

    public async Task<JsonElement?> GetCampaignCallOutcomeAsync(string tenantId, string outcomeId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-call-outcomes/{Uri.EscapeDataString(outcomeId)}", ct);

    public async Task<JsonElement?> GetCampaignCallOutcomeByContactAsync(string tenantId, string contactExecutionId, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-contact-executions/{Uri.EscapeDataString(contactExecutionId)}/call-outcome", ct);

    public async Task<JsonElement?> CreateCampaignCallOutcomeAsync(string tenantId, string contactExecutionId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-contact-executions/{Uri.EscapeDataString(contactExecutionId)}/call-outcome", body, ct);

    public async Task<JsonElement?> UpdateCampaignCallOutcomeAsync(string tenantId, string outcomeId, object body, CancellationToken ct) =>
        await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-call-outcomes/{Uri.EscapeDataString(outcomeId)}", body, ct);

    public async Task<JsonElement?> DraftCampaignFromPromptAsync(string tenantId, string prompt, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-builder/draft-from-prompt", new { prompt }, ct);

    public async Task<JsonElement?> RefineCampaignDraftAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-builder/refine", body, ct);

    public async Task<JsonElement?> ValidateCampaignDraftAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/campaign-builder/validate", body, ct);

    // Commerce
    public async Task<CommerceParty?> ResolvePartyAsync(
        string tenantId,
        string channel,
        string identifier,
        string? displayName,
        string? kind,
        string? phone,
        string? email,
        string? fullName,
        string? sessionId,
        CancellationToken ct)
    {
        var body = new { channel, identifier, displayName, kind, phone, email, fullName, sessionId };
        return await PostAsync<CommerceParty>($"/api/v1/tenants/{tenantId}/commerce/crm/resolve-party", body, ct);
    }

    public async Task<CommerceConversationContext?> GetCommerceConversationContextAsync(string tenantId, string sessionId, CancellationToken ct) =>
        await GetAsync<CommerceConversationContext>($"/api/v1/tenants/{tenantId}/commerce/conversation-context/{sessionId}", ct);
    public async Task<CommerceConversationContext?> GetCommerceConversationContextByThreadAsync(string tenantId, string threadId, CancellationToken ct) =>
        await GetAsync<CommerceConversationContext>($"/api/v1/tenants/{tenantId}/commerce/conversation-context/by-thread/{threadId}", ct);

    public async Task<List<CommerceInventoryItem>> SearchInventoryAsync(string tenantId, string? query, int limit, CancellationToken ct) =>
        await GetAsync<List<CommerceInventoryItem>>($"/api/v1/tenants/{tenantId}/commerce/inventory/search?query={Uri.EscapeDataString(query ?? string.Empty)}&limit={Math.Clamp(limit, 1, 100)}", ct) ?? [];

    public async Task<JsonElement?> UpsertInventoryItemAsync(string tenantId, string sku, object body, CancellationToken ct) =>
        await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/items/{Uri.EscapeDataString(sku)}", body, ct);

    public async Task<JsonElement?> AdjustInventoryAsync(string tenantId, string sku, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/items/{Uri.EscapeDataString(sku)}/adjust", body, ct);

    public async Task<JsonElement?> SearchInventoryMovementsAsync(string tenantId, string? sku, int page, int pageSize, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/movements?sku={Uri.EscapeDataString(sku ?? string.Empty)}&page={Math.Max(0, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}", ct);

    public async Task<JsonElement?> SearchCategoriesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/categories?query={Uri.EscapeDataString(query ?? string.Empty)}&page={Math.Max(0, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}", ct);

    public async Task<JsonElement?> CreateCategoryAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/categories", body, ct);

    public async Task<JsonElement?> UpdateCategoryAsync(string tenantId, string categoryId, object body, CancellationToken ct) =>
        await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/categories/{Uri.EscapeDataString(categoryId)}", body, ct);

    public async Task<bool> DeleteCategoryAsync(string tenantId, string categoryId, CancellationToken ct)
    {
        var response = await _http.DeleteAsync($"/api/v1/tenants/{tenantId}/commerce/inventory/categories/{Uri.EscapeDataString(categoryId)}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<JsonElement?> SearchBranchesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct) =>
        await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/branches?query={Uri.EscapeDataString(query ?? string.Empty)}&page={Math.Max(0, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}", ct);

    public async Task<JsonElement?> CreateBranchAsync(string tenantId, object body, CancellationToken ct) =>
        await PostAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/branches", body, ct);

    public async Task<JsonElement?> UpdateBranchAsync(string tenantId, string branchId, object body, CancellationToken ct) =>
        await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/inventory/branches/{Uri.EscapeDataString(branchId)}", body, ct);

    public async Task<bool> DeleteBranchAsync(string tenantId, string branchId, CancellationToken ct)
    {
        var response = await _http.DeleteAsync($"/api/v1/tenants/{tenantId}/commerce/inventory/branches/{Uri.EscapeDataString(branchId)}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<CommerceSale?> CreateSaleAsync(string tenantId, string partyId, string? currency, string? sessionId, string? threadId, IReadOnlyList<CommerceLineItemPayload> items, CancellationToken ct)
    {
        var body = new { partyId, currency, sessionId, threadId, items };
        return await PostAsync<CommerceSale>($"/api/v1/tenants/{tenantId}/commerce/sales", body, ct);
    }

    public async Task<CommerceOrder?> CreateOrderAsync(string tenantId, string partyId, string? currency, string? sessionId, string? threadId, IReadOnlyList<CommerceLineItemPayload> items, CancellationToken ct)
    {
        var body = new { partyId, currency, sessionId, threadId, items };
        return await PostAsync<CommerceOrder>($"/api/v1/tenants/{tenantId}/commerce/orders", body, ct);
    }

    public async Task<CommerceInvoice?> CreateInvoiceAsync(string tenantId, string partyId, string? saleId, string? orderId, decimal total, string? currency, string? sessionId, string? threadId, CancellationToken ct)
    {
        var body = new { partyId, saleId, orderId, total, currency, sessionId, threadId };
        return await PostAsync<CommerceInvoice>($"/api/v1/tenants/{tenantId}/commerce/billing/invoices", body, ct);
    }

    public async Task<CommercePagedResult<CommerceParty>?> SearchCustomersAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct) =>
        await GetAsync<CommercePagedResult<CommerceParty>>($"/api/v1/tenants/{tenantId}/commerce/crm/customers?query={Uri.EscapeDataString(query ?? string.Empty)}&page={Math.Max(0, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}", ct);

    public async Task<CommerceParty?> UpdateCustomerAsync(string tenantId, string partyId, object body, CancellationToken ct)
    {
        var response = await _http.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/commerce/crm/customers/{partyId}", body, _json, ct);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<CommerceParty>(_json, ct);
    }

    public async Task<bool> DeleteCustomerAsync(string tenantId, string partyId, CancellationToken ct)
    {
        var response = await _http.DeleteAsync($"/api/v1/tenants/{tenantId}/commerce/crm/customers/{partyId}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<CommercePagedResult<CommerceSale>?> SearchSalesAsync(string tenantId, string? partyId, string? state, int page, int pageSize, CancellationToken ct)
        => await GetAsync<CommercePagedResult<CommerceSale>>($"/api/v1/tenants/{tenantId}/commerce/sales?partyId={Uri.EscapeDataString(partyId ?? string.Empty)}&state={Uri.EscapeDataString(state ?? string.Empty)}&page={Math.Max(0, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}", ct);

    public async Task<CommerceSaleDetail?> GetSaleByIdAsync(string tenantId, string saleId, CancellationToken ct)
        => await GetAsync<CommerceSaleDetail>($"/api/v1/tenants/{tenantId}/commerce/sales/{saleId}", ct);

    public async Task<JsonElement?> UpdateSaleAsync(string tenantId, string saleId, object body, CancellationToken ct)
        => await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/sales/{Uri.EscapeDataString(saleId)}", body, ct);

    public async Task<SaleCalculationResult?> CalculateSaleAsync(string tenantId, object body, CancellationToken ct)
        => await PostAsync<SaleCalculationResult>($"/api/v1/tenants/{tenantId}/commerce/sales/calculate", body, ct);

    public async Task<CommercePagedResult<CommerceOrder>?> SearchOrdersAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct)
        => await GetAsync<CommercePagedResult<CommerceOrder>>($"/api/v1/tenants/{tenantId}/commerce/orders?partyId={Uri.EscapeDataString(partyId ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&page={Math.Max(0, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}", ct);

    public async Task<JsonElement?> GetOrderByIdAsync(string tenantId, string orderId, CancellationToken ct)
        => await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/orders/{Uri.EscapeDataString(orderId)}", ct);

    public async Task<JsonElement?> UpdateOrderAsync(string tenantId, string orderId, object body, CancellationToken ct)
        => await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/orders/{Uri.EscapeDataString(orderId)}", body, ct);

    public async Task<JsonElement?> GetStoreSettingsAsync(string tenantId, CancellationToken ct)
        => await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/store/settings", ct);

    public async Task<JsonElement?> UpdateStoreSettingsAsync(string tenantId, object body, CancellationToken ct)
        => await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/store/settings", body, ct);

    public async Task<CommercePagedResult<CommerceInvoice>?> SearchInvoicesAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct)
        => await GetAsync<CommercePagedResult<CommerceInvoice>>($"/api/v1/tenants/{tenantId}/commerce/billing/invoices?partyId={Uri.EscapeDataString(partyId ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&page={Math.Max(0, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}", ct);

    public async Task<JsonElement?> GetInvoiceByIdAsync(string tenantId, string invoiceId, CancellationToken ct)
        => await GetAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/billing/invoices/{Uri.EscapeDataString(invoiceId)}", ct);

    public async Task<JsonElement?> UpdateInvoiceAsync(string tenantId, string invoiceId, object body, CancellationToken ct)
        => await PutAsync<JsonElement>($"/api/v1/tenants/{tenantId}/commerce/billing/invoices/{Uri.EscapeDataString(invoiceId)}", body, ct);

    public async Task<CommerceInvoice?> UpdateInvoiceStatusAsync(string tenantId, string invoiceId, string status, CancellationToken ct)
        => await PutAsync<CommerceInvoice>($"/api/v1/tenants/{tenantId}/commerce/billing/invoices/{invoiceId}/status", new { status }, ct);

    public async Task<CommerceParty?> GetCustomerByIdAsync(string tenantId, string partyId, CancellationToken ct) =>
        await GetAsync<CommerceParty>($"/api/v1/tenants/{tenantId}/commerce/crm/customers/{Uri.EscapeDataString(partyId)}", ct);

    public async Task<(string FileName, string ContentType, string Base64Content)?> GetInvoicePdfAsync(string tenantId, string invoiceId, CancellationToken ct)
    {
        var response = await _http.GetAsync($"/api/v1/tenants/{tenantId}/commerce/billing/invoices/{Uri.EscapeDataString(invoiceId)}/pdf", ct);
        if (!response.IsSuccessStatusCode) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"invoice-{invoiceId}.pdf";
        return (fileName.Trim('"'), contentType, Convert.ToBase64String(bytes));
    }

    public async Task<bool> SendInvoiceWhatsAppAsync(string tenantId, string invoiceId, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/commerce/billing/invoices/{invoiceId}/send-whatsapp", new { }, _json, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CloseConversationAsync(string tenantId, string sessionId, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/commerce/conversation-context/{sessionId}/close", new { }, _json, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SendConversationMessageAsync(string tenantId, string sessionId, string content, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/commerce/conversation-context/{sessionId}/messages", new { content }, _json, ct);
        return response.IsSuccessStatusCode;
    }

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

    private async Task<T?> PutAsync<T>(string path, object body, CancellationToken ct)
    {
        var response = await _http.PutAsJsonAsync(path, body, _json, ct);
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

public sealed record CommerceParty(
    string Id, string Kind, string Channel, string Identifier, string? DisplayName,
    string? FullName, string? Email, string? Phone, IReadOnlyList<CommerceIdentityLink>? LinkedIdentities,
    string? LastSessionId, string? LastThreadId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CommerceConversationContext(
    string Id, string ChannelType, string Identifier, string? ThreadId, string Status,
    DateTimeOffset? ExpiresAt, bool IsExpired, CommerceParty? Party);

public sealed record CommerceInventoryItem(
    string Id,
    string Sku,
    string Name,
    string? Description,
    string ItemType,
    string UnitOfMeasure,
    bool TracksInventory,
    decimal UnitPrice,
    int OnHand,
    bool Active,
    IReadOnlyList<string>? CategoryIds,
    IReadOnlyList<string>? CategoryNames,
    IReadOnlyList<CommerceProductAttribute>? Attributes,
    IReadOnlyList<CommerceProductVariation>? Variations);

public sealed record CommerceLineItemPayload(
    string Sku, string Name, decimal UnitPrice, decimal Quantity);

public sealed record CommerceSale(
    string Id, string PartyId, decimal Total, string Currency, DateTimeOffset CreatedAt);
public sealed record CommerceSaleDetail(
    string Id, string PartyId, decimal Subtotal, decimal Discount, decimal Tax, decimal Total, string Currency, string PaymentMethod, string State, IReadOnlyList<CommerceLineItemPayload> Items);

public sealed record CommerceOrder(
    string Id, string PartyId, decimal Total, string Currency, string Status, DateTimeOffset CreatedAt);

public sealed record CommerceInvoice(
    string Id, string PartyId, string? SaleId, string? OrderId, decimal Total, string Currency, string Status, DateTimeOffset CreatedAt);
public sealed record CommerceIdentityLink(string Channel, string Identifier);
public sealed record CommercePagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);
public sealed record SaleCalculationResult(decimal Subtotal, decimal Discount, decimal Tax, decimal Total);
public sealed record CommerceProductAttribute(string Key, string Value);
public sealed record CommerceProductVariation(string Id, string Sku, string Name, decimal Price, int Stock, bool Active, IReadOnlyList<CommerceProductAttribute>? Attributes);
