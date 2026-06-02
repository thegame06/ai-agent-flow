using System.Text.Json;
using AgentFlow.McpServer.Client;

namespace AgentFlow.McpServer.Tools;

internal static class CampaignToolInputs
{
    public static JsonElement Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }

    public static string? String(this JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        return el.TryGetProperty(property, out var val) ? val.GetString() : null;
    }
}

public sealed class ListCampaignsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListCampaignsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_list_campaigns";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Lists all campaigns in the tenant.", IntendedFor = "any", InputSchemaJson = "{}" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct) =>
        (await _api.ListCampaignsAsync(request.TenantId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to list campaigns");
}

public sealed class GetCampaignTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetCampaignTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_get_campaign";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Gets a campaign by campaignId.", IntendedFor = "any", InputSchemaJson = """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var campaignId = CampaignToolInputs.Parse(request.InputJson).String("campaignId");
        if (string.IsNullOrWhiteSpace(campaignId)) return McpInvokeResult.Fail(Name, "campaignId is required");
        return (await _api.GetCampaignAsync(request.TenantId, campaignId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Campaign '{campaignId}' not found");
    }
}

public sealed class ListCampaignSegmentsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListCampaignSegmentsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_list_campaign_segments";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Lists reusable campaign segments.", IntendedFor = "any", InputSchemaJson = "{}" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct) =>
        (await _api.ListCampaignSegmentsAsync(request.TenantId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to list campaign segments");
}

public sealed class GetCampaignSegmentTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetCampaignSegmentTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_get_campaign_segment";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Gets a campaign segment by segmentId.", IntendedFor = "any", InputSchemaJson = """{"type":"object","required":["segmentId"],"properties":{"segmentId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var segmentId = CampaignToolInputs.Parse(request.InputJson).String("segmentId");
        if (string.IsNullOrWhiteSpace(segmentId)) return McpInvokeResult.Fail(Name, "segmentId is required");
        return (await _api.GetCampaignSegmentAsync(request.TenantId, segmentId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Segment '{segmentId}' not found");
    }
}

public sealed class PreviewCampaignSegmentTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public PreviewCampaignSegmentTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_preview_campaign_segment";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Previews the audience for a segment or inline filterJson.", IntendedFor = "any", InputSchemaJson = """{"type":"object","properties":{"segmentId":{"type":"string"},"filterJson":{"type":"string"},"campaignId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        var segmentId = input.String("segmentId");
        JsonElement? result = string.IsNullOrWhiteSpace(segmentId)
            ? await _api.PreviewCampaignSegmentAsync(request.TenantId, new { filterJson = input.String("filterJson"), campaignId = input.String("campaignId") }, ct)
            : await _api.PreviewCampaignSegmentByIdAsync(request.TenantId, segmentId, ct);
        return result is { } preview
            ? McpInvokeResult.Success(Name, request.TenantId, preview, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to preview campaign segment");
    }
}

public sealed class ListCampaignCallPlaybooksTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListCampaignCallPlaybooksTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_list_campaign_call_playbooks";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Lists call playbooks used by campaign voice flows.", IntendedFor = "any", InputSchemaJson = "{}" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct) =>
        (await _api.ListCampaignCallPlaybooksAsync(request.TenantId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to list campaign call playbooks");
}

public sealed class GetCampaignCallPlaybookTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetCampaignCallPlaybookTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_get_campaign_call_playbook";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Gets a call playbook by playbookId.", IntendedFor = "any", InputSchemaJson = """{"type":"object","required":["playbookId"],"properties":{"playbookId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var playbookId = CampaignToolInputs.Parse(request.InputJson).String("playbookId");
        if (string.IsNullOrWhiteSpace(playbookId)) return McpInvokeResult.Fail(Name, "playbookId is required");
        return (await _api.GetCampaignCallPlaybookAsync(request.TenantId, playbookId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Playbook '{playbookId}' not found");
    }
}

public sealed class CreateCampaignCallPlaybookTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CreateCampaignCallPlaybookTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_create_campaign_call_playbook";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Creates a call playbook for campaign voice flows.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["payload"],"properties":{"payload":{"type":"object"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        if (!input.TryGetProperty("payload", out var payload)) return McpInvokeResult.Fail(Name, "payload is required");
        return (await _api.CreateCampaignCallPlaybookAsync(request.TenantId, JsonSerializer.Deserialize<object>(payload.GetRawText()) ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to create call playbook");
    }
}

public sealed class UpdateCampaignCallPlaybookTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public UpdateCampaignCallPlaybookTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_update_campaign_call_playbook";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Updates a campaign call playbook.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["playbookId","payload"],"properties":{"playbookId":{"type":"string"},"payload":{"type":"object"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        var playbookId = input.String("playbookId");
        if (string.IsNullOrWhiteSpace(playbookId) || !input.TryGetProperty("payload", out var payload))
            return McpInvokeResult.Fail(Name, "playbookId and payload are required");
        return (await _api.UpdateCampaignCallPlaybookAsync(request.TenantId, playbookId, JsonSerializer.Deserialize<object>(payload.GetRawText()) ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to update playbook '{playbookId}'");
    }
}

public sealed class GetCampaignMetricsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetCampaignMetricsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_get_campaign_metrics";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Gets aggregate metrics for a campaign.", IntendedFor = "any", InputSchemaJson = """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var campaignId = CampaignToolInputs.Parse(request.InputJson).String("campaignId");
        if (string.IsNullOrWhiteSpace(campaignId)) return McpInvokeResult.Fail(Name, "campaignId is required");
        return (await _api.GetCampaignMetricsAsync(request.TenantId, campaignId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to get metrics for '{campaignId}'");
    }
}

public sealed class DraftCampaignFromPromptTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public DraftCampaignFromPromptTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_draft_campaign_from_prompt";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Creates a campaign draft from natural language.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["prompt"],"properties":{"prompt":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var prompt = CampaignToolInputs.Parse(request.InputJson).String("prompt");
        if (string.IsNullOrWhiteSpace(prompt)) return McpInvokeResult.Fail(Name, "prompt is required");
        return (await _api.DraftCampaignFromPromptAsync(request.TenantId, prompt, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to draft campaign");
    }
}

public sealed class RefineCampaignDraftTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public RefineCampaignDraftTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_refine_campaign_draft";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Refines a campaign draft with a new prompt.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["current","prompt"],"properties":{"current":{"type":"object"},"prompt":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct) =>
        (await _api.RefineCampaignDraftAsync(request.TenantId, JsonSerializer.Deserialize<object>(request.InputJson ?? "{}") ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to refine campaign draft");
}

public sealed class ValidateCampaignDraftTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ValidateCampaignDraftTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_validate_campaign_draft";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Validates a campaign draft and returns warnings.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["draft"],"properties":{"draft":{"type":"object"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct) =>
        (await _api.ValidateCampaignDraftAsync(request.TenantId, JsonSerializer.Deserialize<object>(request.InputJson ?? "{}") ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to validate campaign draft");
}

public sealed class CreateCampaignTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CreateCampaignTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_create_campaign";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Creates a campaign using a payload body.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["payload"],"properties":{"payload":{"type":"object"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        if (!input.TryGetProperty("payload", out var payload)) return McpInvokeResult.Fail(Name, "payload is required");
        return (await _api.CreateCampaignAsync(request.TenantId, JsonSerializer.Deserialize<object>(payload.GetRawText()) ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to create campaign");
    }
}

public sealed class UpdateCampaignTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public UpdateCampaignTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_update_campaign";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Updates a campaign using a payload body.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["campaignId","payload"],"properties":{"campaignId":{"type":"string"},"payload":{"type":"object"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        var campaignId = input.String("campaignId");
        if (string.IsNullOrWhiteSpace(campaignId) || !input.TryGetProperty("payload", out var payload))
            return McpInvokeResult.Fail(Name, "campaignId and payload are required");
        return (await _api.UpdateCampaignAsync(request.TenantId, campaignId, JsonSerializer.Deserialize<object>(payload.GetRawText()) ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to update campaign '{campaignId}'");
    }
}

public sealed class PublishCampaignTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public PublishCampaignTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_publish_campaign";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Publishes a campaign.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var campaignId = CampaignToolInputs.Parse(request.InputJson).String("campaignId");
        if (string.IsNullOrWhiteSpace(campaignId)) return McpInvokeResult.Fail(Name, "campaignId is required");
        return (await _api.PublishCampaignAsync(request.TenantId, campaignId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to publish campaign '{campaignId}'");
    }
}

public sealed class PauseCampaignTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public PauseCampaignTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_pause_campaign";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Pauses a campaign.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var campaignId = CampaignToolInputs.Parse(request.InputJson).String("campaignId");
        if (string.IsNullOrWhiteSpace(campaignId)) return McpInvokeResult.Fail(Name, "campaignId is required");
        return (await _api.PauseCampaignAsync(request.TenantId, campaignId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to pause campaign '{campaignId}'");
    }
}

public sealed class ResumeCampaignTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ResumeCampaignTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_resume_campaign";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Resumes a campaign.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var campaignId = CampaignToolInputs.Parse(request.InputJson).String("campaignId");
        if (string.IsNullOrWhiteSpace(campaignId)) return McpInvokeResult.Fail(Name, "campaignId is required");
        return (await _api.ResumeCampaignAsync(request.TenantId, campaignId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to resume campaign '{campaignId}'");
    }
}

public sealed class RunCampaignNowTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public RunCampaignNowTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_run_campaign_now";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Runs a campaign immediately.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var campaignId = CampaignToolInputs.Parse(request.InputJson).String("campaignId");
        if (string.IsNullOrWhiteSpace(campaignId)) return McpInvokeResult.Fail(Name, "campaignId is required");
        return (await _api.RunCampaignNowAsync(request.TenantId, campaignId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to run campaign '{campaignId}'");
    }
}

public sealed class ListCampaignRunsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListCampaignRunsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_list_campaign_runs";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Lists campaign runs, optionally filtered by campaign.", IntendedFor = "any", InputSchemaJson = """{"type":"object","properties":{"campaignId":{"type":"string"},"limit":{"type":"integer"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        var limit = input.TryGetProperty("limit", out var limitEl) && limitEl.TryGetInt32(out var parsed) ? parsed : 50;
        return (await _api.GetCampaignRunsAsync(request.TenantId, input.String("campaignId"), limit, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "Unable to list campaign runs");
    }
}

public sealed class ListCampaignCallOutcomesTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListCampaignCallOutcomesTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_list_campaign_call_outcomes";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Lists call outcomes for a campaign run.", IntendedFor = "any", InputSchemaJson = """{"type":"object","required":["runId"],"properties":{"runId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var runId = CampaignToolInputs.Parse(request.InputJson).String("runId");
        if (string.IsNullOrWhiteSpace(runId)) return McpInvokeResult.Fail(Name, "runId is required");
        return (await _api.ListCampaignCallOutcomesAsync(request.TenantId, runId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to list call outcomes for run '{runId}'");
    }
}

public sealed class GetCampaignCallOutcomeTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetCampaignCallOutcomeTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_get_campaign_call_outcome";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Gets a campaign call outcome by outcomeId or contactExecutionId.", IntendedFor = "any", InputSchemaJson = """{"type":"object","properties":{"outcomeId":{"type":"string"},"contactExecutionId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        var outcomeId = input.String("outcomeId");
        var contactExecutionId = input.String("contactExecutionId");
        JsonElement? result = !string.IsNullOrWhiteSpace(outcomeId)
            ? await _api.GetCampaignCallOutcomeAsync(request.TenantId, outcomeId, ct)
            : !string.IsNullOrWhiteSpace(contactExecutionId)
                ? await _api.GetCampaignCallOutcomeByContactAsync(request.TenantId, contactExecutionId, ct)
                : null;
        return result is { } outcome
            ? McpInvokeResult.Success(Name, request.TenantId, outcome, request.ExecutionId)
            : McpInvokeResult.Fail(Name, "outcomeId or contactExecutionId is required");
    }
}

public sealed class CreateCampaignCallOutcomeTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CreateCampaignCallOutcomeTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_create_campaign_call_outcome";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Creates a structured call outcome for a contact execution.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["contactExecutionId","payload"],"properties":{"contactExecutionId":{"type":"string"},"payload":{"type":"object"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        var contactExecutionId = input.String("contactExecutionId");
        if (string.IsNullOrWhiteSpace(contactExecutionId) || !input.TryGetProperty("payload", out var payload))
            return McpInvokeResult.Fail(Name, "contactExecutionId and payload are required");
        return (await _api.CreateCampaignCallOutcomeAsync(request.TenantId, contactExecutionId, JsonSerializer.Deserialize<object>(payload.GetRawText()) ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to create call outcome for '{contactExecutionId}'");
    }
}

public sealed class UpdateCampaignCallOutcomeTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public UpdateCampaignCallOutcomeTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_update_campaign_call_outcome";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Updates a campaign call outcome.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["outcomeId","payload"],"properties":{"outcomeId":{"type":"string"},"payload":{"type":"object"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var input = CampaignToolInputs.Parse(request.InputJson);
        var outcomeId = input.String("outcomeId");
        if (string.IsNullOrWhiteSpace(outcomeId) || !input.TryGetProperty("payload", out var payload))
            return McpInvokeResult.Fail(Name, "outcomeId and payload are required");
        return (await _api.UpdateCampaignCallOutcomeAsync(request.TenantId, outcomeId, JsonSerializer.Deserialize<object>(payload.GetRawText()) ?? new { }, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to update call outcome '{outcomeId}'");
    }
}

public sealed class GetCampaignRunTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetCampaignRunTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_get_campaign_run";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Gets a campaign run and its contacts.", IntendedFor = "any", InputSchemaJson = """{"type":"object","required":["runId"],"properties":{"runId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var runId = CampaignToolInputs.Parse(request.InputJson).String("runId");
        if (string.IsNullOrWhiteSpace(runId)) return McpInvokeResult.Fail(Name, "runId is required");
        return (await _api.GetCampaignRunAsync(request.TenantId, runId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Run '{runId}' not found");
    }
}

public sealed class RetryCampaignFailuresTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public RetryCampaignFailuresTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_retry_campaign_failures";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Retries failed executions from a campaign run.", IntendedFor = "config-assistant", InputSchemaJson = """{"type":"object","required":["runId"],"properties":{"runId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var runId = CampaignToolInputs.Parse(request.InputJson).String("runId");
        if (string.IsNullOrWhiteSpace(runId)) return McpInvokeResult.Fail(Name, "runId is required");
        return (await _api.RetryCampaignFailuresAsync(request.TenantId, runId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to retry failures for run '{runId}'");
    }
}

public sealed class GetCampaignContactResultsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetCampaignContactResultsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_get_campaign_contact_results";
    public McpToolDescriptor Descriptor => new() { Name = Name, Description = "Gets contact-level execution results for a campaign run.", IntendedFor = "any", InputSchemaJson = """{"type":"object","required":["runId"],"properties":{"runId":{"type":"string"}}}""" };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct)
    {
        var runId = CampaignToolInputs.Parse(request.InputJson).String("runId");
        if (string.IsNullOrWhiteSpace(runId)) return McpInvokeResult.Fail(Name, "runId is required");
        return (await _api.GetCampaignContactResultsAsync(request.TenantId, runId, ct)) is { } result
            ? McpInvokeResult.Success(Name, request.TenantId, result, request.ExecutionId)
            : McpInvokeResult.Fail(Name, $"Unable to get contact results for run '{runId}'");
    }
}
