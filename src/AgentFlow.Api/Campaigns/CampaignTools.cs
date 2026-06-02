using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Api.Campaigns;

internal static class CampaignToolJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}

internal abstract class CampaignToolBase : IToolPlugin
{
    private readonly IServiceScopeFactory _scopeFactory;

    protected CampaignToolBase(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public abstract string ExtensionId { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    public virtual string? OutputSchemaJson => null;
    public abstract ToolRiskLevel RiskLevel { get; }
    public virtual IReadOnlyList<string> RequiredPermissions => [];
    public string Version => "1.0.0";

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(ExtensionHealthStatus.Healthy());
    public abstract Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default);

    internal async Task<ToolResult> WithScopeAsync(ToolExecutionContext context, Func<IServiceProvider, JsonElement, CancellationToken, Task<object>> action, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var input = ParseJson(context.InputJson);
            var result = await action(scope.ServiceProvider, input, ct);
            return ToolResult.Success(JsonSerializer.Serialize(result, CampaignToolJson.SerializerOptions));
        }
        catch (Exception ex)
        {
            return ToolResult.Failure("campaign.tool_error", ex.Message);
        }
    }

    internal static JsonElement ParseJson(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
            return JsonDocument.Parse("{}").RootElement.Clone();

        try
        {
            return JsonDocument.Parse(inputJson).RootElement.Clone();
        }
        catch
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    internal static string? GetString(JsonElement element, string propertyName)
        => element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;

    internal static int GetInt32(JsonElement element, string propertyName, int defaultValue)
        => element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : defaultValue;
}

internal sealed class CampaignListTool : CampaignToolBase
{
    public CampaignListTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.list";
    public override string Name => "af_list_campaigns";
    public override string Description => "List campaigns available in the current tenant.";
    public override string InputSchemaJson => """{"type":"object","properties":{}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, _, token) =>
        {
            var store = sp.GetRequiredService<ICampaignStore>();
            var campaigns = await store.GetCampaignsAsync(context.TenantId, token);
            return new { count = campaigns.Count, campaigns };
        }, ct);
}

internal sealed class CampaignGetTool : CampaignToolBase
{
    public CampaignGetTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.get";
    public override string Name => "af_get_campaign";
    public override string Description => "Get full campaign detail by id.";
    public override string InputSchemaJson => """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var campaignId = GetString(input, "campaignId") ?? throw new InvalidOperationException("campaignId is required.");
            var store = sp.GetRequiredService<ICampaignStore>();
            var campaign = await store.GetCampaignAsync(context.TenantId, campaignId, token)
                ?? throw new InvalidOperationException("Campaign not found.");
            return campaign;
        }, ct);
}

internal sealed class CampaignListSegmentsTool : CampaignToolBase
{
    public CampaignListSegmentsTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.segments.list";
    public override string Name => "af_list_campaign_segments";
    public override string Description => "List reusable campaign segments.";
    public override string InputSchemaJson => """{"type":"object","properties":{}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, _, token) =>
        {
            var store = sp.GetRequiredService<ICampaignStore>();
            var segments = await store.GetSegmentsAsync(context.TenantId, token);
            return new { count = segments.Count, segments };
        }, ct);
}

internal sealed class CampaignGetSegmentTool : CampaignToolBase
{
    public CampaignGetSegmentTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.segments.get";
    public override string Name => "af_get_campaign_segment";
    public override string Description => "Get a reusable campaign segment by id.";
    public override string InputSchemaJson => """{"type":"object","required":["segmentId"],"properties":{"segmentId":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var segmentId = GetString(input, "segmentId") ?? throw new InvalidOperationException("segmentId is required.");
            var store = sp.GetRequiredService<ICampaignStore>();
            var segment = await store.GetSegmentAsync(context.TenantId, segmentId, token)
                ?? throw new InvalidOperationException("Segment not found.");
            return segment;
        }, ct);
}

internal sealed class CampaignPreviewSegmentTool : CampaignToolBase
{
    public CampaignPreviewSegmentTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.segments.preview";
    public override string Name => "af_preview_campaign_segment";
    public override string Description => "Preview the contacts that match a segment or raw filter json.";
    public override string InputSchemaJson => """{"type":"object","properties":{"segmentId":{"type":"string"},"filterJson":{"type":"string"},"campaignId":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var store = sp.GetRequiredService<ICampaignStore>();
            var audience = sp.GetRequiredService<ICampaignAudienceService>();
            var segmentId = GetString(input, "segmentId");
            var filterJson = GetString(input, "filterJson");
            if (string.IsNullOrWhiteSpace(filterJson) && !string.IsNullOrWhiteSpace(segmentId))
            {
                filterJson = (await store.GetSegmentAsync(context.TenantId, segmentId, token))?.FilterJson;
            }

            return await audience.PreviewAsync(context.TenantId, filterJson ?? "{}", GetString(input, "campaignId"), token);
        }, ct);
}

internal sealed class CampaignGetMetricsTool : CampaignToolBase
{
    public CampaignGetMetricsTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.metrics";
    public override string Name => "af_get_campaign_metrics";
    public override string Description => "Get aggregate metrics for a campaign.";
    public override string InputSchemaJson => """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var campaignId = GetString(input, "campaignId") ?? throw new InvalidOperationException("campaignId is required.");
            var service = sp.GetRequiredService<ICampaignExecutionService>();
            return await service.GetMetricsAsync(context.TenantId, campaignId, token);
        }, ct);
}

internal sealed class CampaignDraftFromPromptTool : CampaignToolBase
{
    public CampaignDraftFromPromptTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.draft";
    public override string Name => "af_draft_campaign_from_prompt";
    public override string Description => "Generate a campaign draft from a natural-language prompt.";
    public override string InputSchemaJson => """{"type":"object","required":["prompt"],"properties":{"prompt":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var prompt = GetString(input, "prompt") ?? throw new InvalidOperationException("prompt is required.");
            var builder = sp.GetRequiredService<ICampaignBuilderService>();
            return await builder.DraftFromPromptAsync(context.TenantId, prompt, context.UserId, token);
        }, ct);
}

internal sealed class CampaignRefineDraftTool : CampaignToolBase
{
    public CampaignRefineDraftTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.refine";
    public override string Name => "af_refine_campaign_draft";
    public override string Description => "Refine an existing campaign draft with a new prompt.";
    public override string InputSchemaJson => """{"type":"object","required":["current","prompt"],"properties":{"current":{"type":"object"},"prompt":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            if (!input.TryGetProperty("current", out var currentElement))
                throw new InvalidOperationException("current is required.");
            var current = JsonSerializer.Deserialize<CampaignBuilderDraftContract>(currentElement.GetRawText(), CampaignToolJson.SerializerOptions)
                ?? throw new InvalidOperationException("current draft is invalid.");
            var prompt = GetString(input, "prompt") ?? throw new InvalidOperationException("prompt is required.");
            var builder = sp.GetRequiredService<ICampaignBuilderService>();
            return await builder.RefineAsync(context.TenantId, current, prompt, context.UserId, token);
        }, ct);
}

internal sealed class CampaignValidateDraftTool : CampaignToolBase
{
    public CampaignValidateDraftTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.validate";
    public override string Name => "af_validate_campaign_draft";
    public override string Description => "Validate a campaign draft before saving or publishing it.";
    public override string InputSchemaJson => """{"type":"object","required":["draft"],"properties":{"draft":{"type":"object"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            if (!input.TryGetProperty("draft", out var draftElement))
                throw new InvalidOperationException("draft is required.");
            var draft = JsonSerializer.Deserialize<CampaignBuilderDraftContract>(draftElement.GetRawText(), CampaignToolJson.SerializerOptions)
                ?? throw new InvalidOperationException("draft is invalid.");
            var builder = sp.GetRequiredService<ICampaignBuilderService>();
            var warnings = await builder.ValidateAsync(context.TenantId, draft, token);
            return new { valid = warnings.Count == 0, warnings };
        }, ct);
}

internal abstract class CampaignMutationToolBase : CampaignToolBase
{
    protected CampaignMutationToolBase(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Medium;
}

internal sealed class CampaignCreateTool : CampaignMutationToolBase
{
    public CampaignCreateTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.create";
    public override string Name => "af_create_campaign";
    public override string Description => "Create a campaign from a campaign draft or explicit campaign payload.";
    public override string InputSchemaJson => """{"type":"object","properties":{"campaign":{"type":"object"},"draft":{"type":"object"},"segment":{"type":"object"},"createSegment":{"type":"boolean"}}}""";
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var store = sp.GetRequiredService<ICampaignStore>();
            CampaignContract campaign;
            CampaignSegmentContract? segment = null;

            if (input.TryGetProperty("draft", out var draftElement))
            {
                var draft = JsonSerializer.Deserialize<CampaignBuilderDraftContract>(draftElement.GetRawText(), CampaignToolJson.SerializerOptions)
                    ?? throw new InvalidOperationException("draft is invalid.");
                campaign = draft.CampaignDraft;
                segment = draft.SegmentDraft;
            }
            else if (input.TryGetProperty("campaign", out var campaignElement))
            {
                campaign = JsonSerializer.Deserialize<CampaignContract>(campaignElement.GetRawText(), CampaignToolJson.SerializerOptions)
                    ?? throw new InvalidOperationException("campaign is invalid.");
                if (input.TryGetProperty("segment", out var segmentElement))
                {
                    segment = JsonSerializer.Deserialize<CampaignSegmentContract>(segmentElement.GetRawText(), CampaignToolJson.SerializerOptions);
                }
            }
            else
            {
                throw new InvalidOperationException("campaign or draft is required.");
            }

            var now = DateTimeOffset.UtcNow;
            var createSegment = !input.TryGetProperty("createSegment", out var createSegmentElement) || createSegmentElement.ValueKind != JsonValueKind.False;
            if (createSegment && segment is not null)
            {
                segment = await store.UpsertSegmentAsync(segment with
                {
                    Id = string.IsNullOrWhiteSpace(segment.Id) ? Guid.NewGuid().ToString("N") : segment.Id,
                    TenantId = context.TenantId,
                    CreatedAt = segment.CreatedAt == default ? now : segment.CreatedAt,
                    UpdatedAt = now,
                    UpdatedBy = context.UserId
                }, token);
                campaign = campaign with { SegmentId = segment.Id, AudienceFilterJson = segment.FilterJson };
            }

            var saved = await store.UpsertCampaignAsync(campaign with
            {
                Id = string.IsNullOrWhiteSpace(campaign.Id) ? Guid.NewGuid().ToString("N") : campaign.Id,
                TenantId = context.TenantId,
                CreatedAt = campaign.CreatedAt == default ? now : campaign.CreatedAt,
                UpdatedAt = now,
                UpdatedBy = context.UserId
            }, token);
            return new { campaign = saved, segment };
        }, ct);
}

internal sealed class CampaignUpdateTool : CampaignMutationToolBase
{
    public CampaignUpdateTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.update";
    public override string Name => "af_update_campaign";
    public override string Description => "Update an existing campaign.";
    public override string InputSchemaJson => """{"type":"object","required":["campaign"],"properties":{"campaign":{"type":"object"}}}""";
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            if (!input.TryGetProperty("campaign", out var campaignElement))
                throw new InvalidOperationException("campaign is required.");
            var campaign = JsonSerializer.Deserialize<CampaignContract>(campaignElement.GetRawText(), CampaignToolJson.SerializerOptions)
                ?? throw new InvalidOperationException("campaign is invalid.");
            var store = sp.GetRequiredService<ICampaignStore>();
            var existing = await store.GetCampaignAsync(context.TenantId, campaign.Id, token)
                ?? throw new InvalidOperationException("Campaign not found.");
            var updated = await store.UpsertCampaignAsync(campaign with
            {
                TenantId = context.TenantId,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = context.UserId
            }, token);
            return updated;
        }, ct);
}

internal sealed class CampaignPublishTool : CampaignMutationToolBase
{
    public CampaignPublishTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.publish";
    public override string Name => "af_publish_campaign";
    public override string Description => "Publish a campaign so it can start running on schedule.";
    public override string InputSchemaJson => """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""";
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        CampaignStatusToolHelpers.UpdateStatusAsync(context, this, CampaignStatus.Published, true, ct);
}

internal sealed class CampaignPauseTool : CampaignMutationToolBase
{
    public CampaignPauseTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.pause";
    public override string Name => "af_pause_campaign";
    public override string Description => "Pause a running or scheduled campaign.";
    public override string InputSchemaJson => """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""";
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        CampaignStatusToolHelpers.UpdateStatusAsync(context, this, CampaignStatus.Paused, false, ct);
}

internal sealed class CampaignResumeTool : CampaignMutationToolBase
{
    public CampaignResumeTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.resume";
    public override string Name => "af_resume_campaign";
    public override string Description => "Resume a paused campaign.";
    public override string InputSchemaJson => """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""";
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        CampaignStatusToolHelpers.UpdateStatusAsync(context, this, CampaignStatus.Active, true, ct);
}

internal static class CampaignStatusToolHelpers
{
    public static Task<ToolResult> UpdateStatusAsync(
        ToolExecutionContext context,
        CampaignToolBase tool,
        CampaignStatus status,
        bool enabled,
        CancellationToken ct)
        => tool.WithScopeAsync(context, async (sp, input, token) =>
        {
            var campaignId = CampaignToolBase.GetString(input, "campaignId") ?? throw new InvalidOperationException("campaignId is required.");
            var store = sp.GetRequiredService<ICampaignStore>();
            var execution = sp.GetRequiredService<ICampaignExecutionService>();
            var existing = await store.GetCampaignAsync(context.TenantId, campaignId, token)
                ?? throw new InvalidOperationException("Campaign not found.");
            return await store.UpsertCampaignAsync(existing with
            {
                Status = status,
                Enabled = enabled,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = context.UserId,
                NextRunAt = enabled ? execution.ComputeNextRunAt(existing with { Status = status, Enabled = enabled }, DateTimeOffset.UtcNow) : null
            }, token);
        }, ct);
}

internal sealed class CampaignRunNowTool : CampaignMutationToolBase
{
    public CampaignRunNowTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.run";
    public override string Name => "af_run_campaign_now";
    public override string Description => "Run a campaign immediately.";
    public override string InputSchemaJson => """{"type":"object","required":["campaignId"],"properties":{"campaignId":{"type":"string"}}}""";
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var campaignId = GetString(input, "campaignId") ?? throw new InvalidOperationException("campaignId is required.");
            var service = sp.GetRequiredService<ICampaignExecutionService>();
            return await service.RunNowAsync(context.TenantId, campaignId, context.UserId, CampaignRunTrigger.Manual, token);
        }, ct);
}

internal sealed class CampaignListRunsTool : CampaignToolBase
{
    public CampaignListRunsTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.runs.list";
    public override string Name => "af_list_campaign_runs";
    public override string Description => "List campaign runs, optionally filtered by campaign.";
    public override string InputSchemaJson => """{"type":"object","properties":{"campaignId":{"type":"string"},"limit":{"type":"integer"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var store = sp.GetRequiredService<ICampaignStore>();
            var runs = await store.GetRunsAsync(context.TenantId, GetString(input, "campaignId"), GetInt32(input, "limit", 50), token);
            return new { count = runs.Count, runs };
        }, ct);
}

internal sealed class CampaignGetRunTool : CampaignToolBase
{
    public CampaignGetRunTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.runs.get";
    public override string Name => "af_get_campaign_run";
    public override string Description => "Get a campaign run with its contact executions.";
    public override string InputSchemaJson => """{"type":"object","required":["runId"],"properties":{"runId":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var runId = GetString(input, "runId") ?? throw new InvalidOperationException("runId is required.");
            var store = sp.GetRequiredService<ICampaignStore>();
            var run = await store.GetRunAsync(context.TenantId, runId, token)
                ?? throw new InvalidOperationException("Run not found.");
            var contacts = await store.GetContactExecutionsAsync(context.TenantId, runId, token);
            return new { run, contacts };
        }, ct);
}

internal sealed class CampaignRetryFailuresTool : CampaignMutationToolBase
{
    public CampaignRetryFailuresTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.runs.retry";
    public override string Name => "af_retry_campaign_failures";
    public override string Description => "Retry the failed contacts from a previous campaign run.";
    public override string InputSchemaJson => """{"type":"object","required":["runId"],"properties":{"runId":{"type":"string"}}}""";
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var runId = GetString(input, "runId") ?? throw new InvalidOperationException("runId is required.");
            var service = sp.GetRequiredService<ICampaignExecutionService>();
            return await service.RetryFailuresAsync(context.TenantId, runId, context.UserId, token);
        }, ct);
}

internal sealed class CampaignGetContactResultsTool : CampaignToolBase
{
    public CampaignGetContactResultsTool(IServiceScopeFactory scopeFactory) : base(scopeFactory) { }
    public override string ExtensionId => "builtin.campaigns.contacts.get";
    public override string Name => "af_get_campaign_contact_results";
    public override string Description => "Get contact-level results for a campaign run.";
    public override string InputSchemaJson => """{"type":"object","required":["runId"],"properties":{"runId":{"type":"string"}}}""";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public override Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) =>
        WithScopeAsync(context, async (sp, input, token) =>
        {
            var runId = GetString(input, "runId") ?? throw new InvalidOperationException("runId is required.");
            var store = sp.GetRequiredService<ICampaignStore>();
            var contacts = await store.GetContactExecutionsAsync(context.TenantId, runId, token);
            return new { count = contacts.Count, contacts };
        }, ct);
}
