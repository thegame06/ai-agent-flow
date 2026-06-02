using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Memory;

namespace AgentFlow.Api.Campaigns;

public interface ICampaignExecutionService
{
    Task<CampaignAudiencePreviewContract> SimulateAsync(string tenantId, CampaignContract campaign, CancellationToken ct = default);
    Task<CampaignRunContract> RunNowAsync(string tenantId, string campaignId, string requestedBy, CampaignRunTrigger trigger, CancellationToken ct = default);
    Task<CampaignRunContract> RetryFailuresAsync(string tenantId, string runId, string requestedBy, CancellationToken ct = default);
    Task<CampaignMetricsContract> GetMetricsAsync(string tenantId, string campaignId, CancellationToken ct = default);
    DateTimeOffset? ComputeNextRunAt(CampaignContract campaign, DateTimeOffset now);
}

public sealed class CampaignExecutionService : ICampaignExecutionService
{
    private readonly ICampaignStore _campaignStore;
    private readonly ICampaignAudienceService _audienceService;
    private readonly IConnectStore _connectStore;
    private readonly IWorkflowTriggerService _workflowTriggerService;
    private readonly IAuditMemory _auditMemory;
    private readonly IRuntimeModelProfileStore _runtimeProfiles;

    public CampaignExecutionService(
        ICampaignStore campaignStore,
        ICampaignAudienceService audienceService,
        IConnectStore connectStore,
        IWorkflowTriggerService workflowTriggerService,
        IAuditMemory auditMemory,
        IRuntimeModelProfileStore runtimeProfiles)
    {
        _campaignStore = campaignStore;
        _audienceService = audienceService;
        _connectStore = connectStore;
        _workflowTriggerService = workflowTriggerService;
        _auditMemory = auditMemory;
        _runtimeProfiles = runtimeProfiles;
    }

    public Task<CampaignAudiencePreviewContract> SimulateAsync(string tenantId, CampaignContract campaign, CancellationToken ct = default)
        => _audienceService.PreviewAsync(tenantId, campaign.AudienceFilterJson, campaign.Id, ct);

    public async Task<CampaignRunContract> RunNowAsync(string tenantId, string campaignId, string requestedBy, CampaignRunTrigger trigger, CancellationToken ct = default)
    {
        var campaign = await _campaignStore.GetCampaignAsync(tenantId, campaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        var preview = await SimulateAsync(tenantId, campaign, ct);
        var run = await _campaignStore.CreateRunAsync(new CampaignRunContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            CampaignId = campaignId,
            Status = CampaignRunStatus.Running,
            TriggeredBy = trigger,
            StartedAt = DateTimeOffset.UtcNow,
            AudienceSnapshotJson = JsonSerializer.Serialize(preview.Contacts),
            CountersJson = "{}",
            RequestedBy = requestedBy
        }, ct);

        var counters = new CampaignCounters();
        var errors = new List<string>();
        foreach (var contact in preview.Contacts)
        {
            var execution = await _campaignStore.CreateContactExecutionAsync(new CampaignContactExecutionContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                CampaignId = campaign.Id,
                RunId = run.Id,
                PartyId = contact.PartyId,
                Channel = campaign.Channel,
                Recipient = contact.Recipient,
                Status = CampaignContactExecutionStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);

            try
            {
                execution = await ExecuteContactAsync(campaign, run, execution, ct);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                execution = (await _campaignStore.UpdateContactExecutionAsync(execution with
                {
                    Status = CampaignContactExecutionStatus.Failed,
                    SkipReason = ex.Message,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct))!;
            }

            counters.Observe(execution.Status);
        }

        var completed = run with
        {
            Status = errors.Count == 0 ? CampaignRunStatus.Completed : CampaignRunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            CountersJson = JsonSerializer.Serialize(counters),
            ErrorSummaryJson = errors.Count == 0 ? null : JsonSerializer.Serialize(errors.Take(20).ToList())
        };
        completed = (await _campaignStore.UpdateRunAsync(completed, ct))!;

        var nextRunAt = ComputeNextRunAt(campaign, DateTimeOffset.UtcNow);
        await _campaignStore.UpsertCampaignAsync(campaign with
        {
            LastRunAt = DateTimeOffset.UtcNow,
            NextRunAt = nextRunAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = requestedBy,
            Status = campaign.Enabled ? CampaignStatus.Active : campaign.Status
        }, ct);

        await _auditMemory.RecordAsync(new AuditEntry
        {
            TenantId = tenantId,
            UserId = requestedBy,
            AgentId = "campaign-runtime",
            ExecutionId = run.Id,
            EventType = AuditEventType.ConnectOperation,
            CorrelationId = campaign.Id,
            EventJson = JsonSerializer.Serialize(new
            {
                action = "campaign.run.completed",
                campaignId = campaign.Id,
                runId = run.Id,
                counters,
                errors = errors.Count
            })
        }, ct);

        return completed;
    }

    public async Task<CampaignRunContract> RetryFailuresAsync(string tenantId, string runId, string requestedBy, CancellationToken ct = default)
    {
        var run = await _campaignStore.GetRunAsync(tenantId, runId, ct)
            ?? throw new InvalidOperationException("Run not found.");
        var campaign = await _campaignStore.GetCampaignAsync(tenantId, run.CampaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        var retryRun = await _campaignStore.CreateRunAsync(new CampaignRunContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            CampaignId = campaign.Id,
            Status = CampaignRunStatus.Running,
            TriggeredBy = CampaignRunTrigger.Retry,
            StartedAt = DateTimeOffset.UtcNow,
            AudienceSnapshotJson = run.AudienceSnapshotJson,
            RequestedBy = requestedBy
        }, ct);

        var counters = new CampaignCounters();
        var originalExecutions = await _campaignStore.GetContactExecutionsAsync(tenantId, runId, ct);
        foreach (var failed in originalExecutions.Where(x => x.Status == CampaignContactExecutionStatus.Failed))
        {
            var cloned = await _campaignStore.CreateContactExecutionAsync(failed with
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = retryRun.Id,
                Status = CampaignContactExecutionStatus.Queued,
                SkipReason = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);
            cloned = await ExecuteContactAsync(campaign, retryRun, cloned, ct);
            counters.Observe(cloned.Status);
        }

        return (await _campaignStore.UpdateRunAsync(retryRun with
        {
            Status = CampaignRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            CountersJson = JsonSerializer.Serialize(counters)
        }, ct))!;
    }

    public async Task<CampaignMetricsContract> GetMetricsAsync(string tenantId, string campaignId, CancellationToken ct = default)
    {
        var runs = await _campaignStore.GetRunsAsync(tenantId, campaignId, 500, ct);
        var counters = new CampaignCounters();
        foreach (var run in runs)
        {
            var contacts = await _campaignStore.GetContactExecutionsAsync(tenantId, run.Id, ct);
            foreach (var contact in contacts)
                counters.Observe(contact.Status);
        }

        return new CampaignMetricsContract
        {
            CampaignId = campaignId,
            Runs = runs.Count,
            Queued = counters.Queued,
            Sent = counters.Sent,
            Delivered = counters.Delivered,
            Read = counters.Read,
            Failed = counters.Failed,
            Connected = counters.Connected,
            WorkflowStarted = counters.WorkflowStarted,
            Skipped = counters.Skipped
        };
    }

    public DateTimeOffset? ComputeNextRunAt(CampaignContract campaign, DateTimeOffset now)
    {
        if (!campaign.Enabled) return null;
        return campaign.ScheduleType switch
        {
            CampaignScheduleType.Once => campaign.StartAt > now ? campaign.StartAt : null,
            CampaignScheduleType.Hourly => now.AddHours(1),
            CampaignScheduleType.Daily => ComputeDaily(campaign.ScheduleExpression, now),
            CampaignScheduleType.Weekly => ComputeWeekly(campaign.ScheduleExpression, now),
            CampaignScheduleType.Cron => now.AddMinutes(15),
            _ => null
        };
    }

    private async Task<CampaignContactExecutionContract> ExecuteContactAsync(
        CampaignContract campaign,
        CampaignRunContract run,
        CampaignContactExecutionContract execution,
        CancellationToken ct)
    {
        CampaignCallOutcomeContract? callOutcome = null;
        if (campaign.ChannelAction == CampaignChannelAction.Call)
        {
            callOutcome = await _campaignStore.CreateCallOutcomeAsync(new CampaignCallOutcomeContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = campaign.TenantId,
                CampaignId = campaign.Id,
                RunId = run.Id,
                ContactExecutionId = execution.Id,
                PlaybookId = campaign.PlaybookId,
                Status = CampaignCallOutcomeStatus.Queued,
                StartedAt = DateTimeOffset.UtcNow,
                Summary = "Call outcome placeholder created by campaign runtime.",
                NextAction = "await_provider_or_workflow",
                LinkedPartyId = execution.PartyId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);

            execution = execution with
            {
                CallOutcomeId = callOutcome.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            execution = (await _campaignStore.UpdateContactExecutionAsync(execution, ct))!;
        }

        if (campaign.ChannelAction == CampaignChannelAction.WorkflowStart ||
            (campaign.ChannelAction == CampaignChannelAction.Call && !string.IsNullOrWhiteSpace(campaign.WorkflowDefinitionId)) ||
            campaign.ExecutionMode == CampaignExecutionMode.Workflow)
        {
            var runtimeProfile = !string.IsNullOrWhiteSpace(campaign.RuntimeModelProfileId)
                ? _runtimeProfiles.Get(campaign.TenantId, campaign.RuntimeModelProfileId!)
                : (campaign.ChannelAction == CampaignChannelAction.Call ? _runtimeProfiles.GetDefault(campaign.TenantId, "Voice") : null);
            var playbook = !string.IsNullOrWhiteSpace(campaign.PlaybookId)
                ? await _campaignStore.GetPlaybookAsync(campaign.TenantId, campaign.PlaybookId, ct)
                : null;
            var payload = new Dictionary<string, object?>
            {
                ["campaign"] = campaign,
                ["run"] = run,
                ["contact"] = execution,
                ["segment"] = campaign.SegmentId,
                ["party"] = execution.PartyId,
                ["salesSummary"] = new { },
                ["invoiceSummary"] = new { },
                ["conversationSummary"] = new { recipient = execution.Recipient },
                ["channelAction"] = campaign.ChannelAction.ToString(),
                ["callPlaybook"] = playbook,
                ["callOutcome"] = callOutcome,
                ["runtimeModelProfile"] = runtimeProfile is null
                    ? null
                    : new
                    {
                        runtimeProfile.Id,
                        runtimeProfile.Name,
                        runtimeProfile.RuntimeKind,
                        runtimeProfile.Roles,
                        runtimeProfile.Metadata
                    },
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["goal"] = campaign.Goal,
                    ["runtimeModelProfileId"] = runtimeProfile?.Id ?? campaign.RuntimeModelProfileId,
                    ["resultMappingJson"] = campaign.ResultMappingJson,
                    ["followupPolicyJson"] = campaign.FollowupPolicyJson
                }
            };
            var workflowExecution = await _workflowTriggerService.TriggerEventAsync(
                campaign.TenantId,
                "connect.campaign.triggered",
                run.RequestedBy,
                execution.Id,
                payload,
                ct);

            return (await _campaignStore.UpdateContactExecutionAsync(execution with
            {
                Status = CampaignContactExecutionStatus.WorkflowStarted,
                WorkflowExecutionId = workflowExecution.Id,
                CallOutcomeId = callOutcome?.Id ?? execution.CallOutcomeId,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct))!;
        }

        if (campaign.ChannelAction == CampaignChannelAction.Message)
        {
            var inbox = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = campaign.TenantId,
                Channel = campaign.Channel,
                Recipient = execution.Recipient,
                Content = campaign.MessageDraft ?? "Mensaje de campana",
                CampaignId = campaign.Id,
                TemplateId = campaign.TemplateId,
                Status = ConnectOperationalStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = run.RequestedBy
            }, ct);

            return (await _campaignStore.UpdateContactExecutionAsync(execution with
            {
                Status = CampaignContactExecutionStatus.Sent,
                ChannelMessageId = inbox.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct))!;
        }

        return (await _campaignStore.UpdateContactExecutionAsync(execution with
        {
            Status = CampaignContactExecutionStatus.Failed,
            SkipReason = "call_direct_not_supported_without_workflow",
            CallOutcomeId = callOutcome?.Id ?? execution.CallOutcomeId,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct))!;
    }

    private static DateTimeOffset ComputeDaily(string expression, DateTimeOffset now)
    {
        if (!TimeOnly.TryParse(expression, out var time))
            time = new TimeOnly(9, 0);
        var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, time.Hour, time.Minute, 0, now.Offset);
        return candidate > now ? candidate : candidate.AddDays(1);
    }

    private static DateTimeOffset ComputeWeekly(string expression, DateTimeOffset now)
    {
        var parts = expression.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var day = DayOfWeek.Monday;
        var time = new TimeOnly(9, 0);
        if (parts.Length > 0 && Enum.TryParse<DayOfWeek>(parts[0], true, out var parsedDay))
            day = parsedDay;
        if (parts.Length > 1 && TimeOnly.TryParse(parts[1], out var parsedTime))
            time = parsedTime;

        var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, time.Hour, time.Minute, 0, now.Offset);
        while (candidate.DayOfWeek != day || candidate <= now)
            candidate = candidate.AddDays(1);
        return candidate;
    }

    private sealed class CampaignCounters
    {
        public int Queued { get; private set; }
        public int Sent { get; private set; }
        public int Delivered { get; private set; }
        public int Read { get; private set; }
        public int Failed { get; private set; }
        public int Connected { get; private set; }
        public int WorkflowStarted { get; private set; }
        public int Skipped { get; private set; }

        public void Observe(CampaignContactExecutionStatus status)
        {
            switch (status)
            {
                case CampaignContactExecutionStatus.Queued: Queued++; break;
                case CampaignContactExecutionStatus.Sent: Sent++; break;
                case CampaignContactExecutionStatus.Delivered: Delivered++; break;
                case CampaignContactExecutionStatus.Read: Read++; break;
                case CampaignContactExecutionStatus.Failed: Failed++; break;
                case CampaignContactExecutionStatus.Connected: Connected++; break;
                case CampaignContactExecutionStatus.WorkflowStarted: WorkflowStarted++; break;
                case CampaignContactExecutionStatus.Skipped: Skipped++; break;
            }
        }
    }
}
