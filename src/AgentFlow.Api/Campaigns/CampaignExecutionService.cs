using System.Globalization;
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
    /// <summary>Sends the campaign message to one number, filled with the first audience contact's data.</summary>
    Task<OutboundSendResult> SendTestAsync(string tenantId, string campaignId, string recipient, string requestedBy, CancellationToken ct = default);
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
    private readonly IOutboundMessageSender _outbound;
    private readonly ICampaignFollowUpService? _followUps;
    private readonly AgentFlow.Api.Messaging.IWhatsAppTemplateStore? _waTemplates;
    private readonly AgentFlow.Domain.Repositories.IChannelDefinitionRepository? _channels;

    public CampaignExecutionService(
        ICampaignStore campaignStore,
        ICampaignAudienceService audienceService,
        IConnectStore connectStore,
        IWorkflowTriggerService workflowTriggerService,
        IAuditMemory auditMemory,
        IRuntimeModelProfileStore runtimeProfiles,
        IOutboundMessageSender outbound,
        ICampaignFollowUpService? followUps = null,
        AgentFlow.Api.Messaging.IWhatsAppTemplateStore? waTemplates = null,
        AgentFlow.Domain.Repositories.IChannelDefinitionRepository? channels = null)
    {
        _followUps = followUps;
        _waTemplates = waTemplates;
        _channels = channels;
        _campaignStore = campaignStore;
        _audienceService = audienceService;
        _connectStore = connectStore;
        _workflowTriggerService = workflowTriggerService;
        _auditMemory = auditMemory;
        _runtimeProfiles = runtimeProfiles;
        _outbound = outbound;
    }

    /// <summary>The Meta account (WABA) of the channel the campaign sends through, when it is a Business API channel.</summary>
    private async Task<string?> CampaignChannelWabaAsync(CampaignContract campaign, CancellationToken ct)
    {
        if (_channels is null || string.IsNullOrWhiteSpace(campaign.Channel)) return null;
        var channel = await _channels.GetByIdAsync(campaign.Channel, campaign.TenantId, ct);
        if (channel is null && Enum.TryParse<AgentFlow.Domain.Aggregates.ChannelType>(campaign.Channel, true, out var type))
            channel = (await _channels.GetByTypeAsync(type, campaign.TenantId, ct)).FirstOrDefault(c => c.Status == AgentFlow.Domain.Aggregates.ChannelStatus.Active);
        return channel?.Config.GetValueOrDefault("BusinessAccountId") is { Length: > 0 } waba ? waba : null;
    }

    public Task<CampaignAudiencePreviewContract> SimulateAsync(string tenantId, CampaignContract campaign, CancellationToken ct = default)
        => _audienceService.PreviewAsync(tenantId, campaign.AudienceFilterJson, campaign.Id, ct);

    public async Task<CampaignRunContract> RunNowAsync(string tenantId, string campaignId, string requestedBy, CampaignRunTrigger trigger, CancellationToken ct = default)
    {
        var campaign = await _campaignStore.GetCampaignAsync(tenantId, campaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        // The whole audience (a preview is capped at 50 contacts for display).
        var preview = await _audienceService.ResolveAsync(tenantId, campaign.AudienceFilterJson, campaign.Id, ct);
        var optedOut = await _campaignStore.GetOptedOutRecipientKeysAsync(tenantId, ct);
        var contactPolicy = CampaignContactPolicy.From(campaign);
        var run = await _campaignStore.CreateRunAsync(new CampaignRunContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            CampaignId = campaignId,
            Status = CampaignRunStatus.Running,
            TriggeredBy = trigger,
            StartedAt = DateTimeOffset.UtcNow,
            AudienceSnapshotJson = JsonSerializer.Serialize(preview.Contacts.Take(500)),
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
                RecipientKey = RecipientKeys.From(contact.Recipient),
                DisplayName = contact.DisplayName,
                Variables = contact.Variables,
                Status = CampaignContactExecutionStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);

            if (execution.RecipientKey is not null && _followUps is not null && contactPolicy.MaxPerRecipientPerDay > 0 &&
                await _followUps.CountSendsTodayAsync(tenantId, execution.RecipientKey, contactPolicy, DateTimeOffset.UtcNow, ct) >= contactPolicy.MaxPerRecipientPerDay)
            {
                execution = (await _campaignStore.UpdateContactExecutionAsync(execution with
                {
                    Status = CampaignContactExecutionStatus.Skipped,
                    SkipReason = "limite_diario",
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct))!;
                counters.Observe(execution.Status);
                continue;
            }

            if (execution.RecipientKey is not null && optedOut.Contains(execution.RecipientKey))
            {
                execution = (await _campaignStore.UpdateContactExecutionAsync(execution with
                {
                    Status = CampaignContactExecutionStatus.Skipped,
                    SkipReason = "opt_out",
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct))!;
                counters.Observe(execution.Status);
                continue;
            }

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

    public async Task<OutboundSendResult> SendTestAsync(string tenantId, string campaignId, string recipient, string requestedBy, CancellationToken ct = default)
    {
        var campaign = await _campaignStore.GetCampaignAsync(tenantId, campaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");
        var preview = await _audienceService.PreviewAsync(tenantId, campaign.AudienceFilterJson, null, ct);
        var sample = preview.Contacts.FirstOrDefault();
        var (text, _) = CampaignMessageRenderer.Render(campaign.MessageDraft ?? string.Empty, sample?.Variables);
        return await _outbound.SendAsync(
            tenantId,
            campaign.Channel,
            recipient,
            "[Prueba] " + text,
            requestedBy,
            campaign.Id,
            campaign.TemplateId,
            new Dictionary<string, string> { ["campaign_test_send"] = "true" },
            ct);
    }

    public DateTimeOffset? ComputeNextRunAt(CampaignContract campaign, DateTimeOffset now)
    {
        if (!campaign.Enabled) return null;
        return campaign.ScheduleType switch
        {
            CampaignScheduleType.Once => campaign.StartAt > now ? campaign.StartAt : null,
            CampaignScheduleType.Hourly => now.AddHours(1),
            CampaignScheduleType.Daily => ComputeDaily(campaign.ScheduleExpression, now, campaign.Timezone),
            CampaignScheduleType.Weekly => ComputeWeekly(campaign.ScheduleExpression, now, campaign.Timezone),
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

        // Workflow mode is the default ExecutionMode, so it only routes a Message campaign to a
        // workflow when one is configured; otherwise the message is sent directly.
        var hasWorkflow = !string.IsNullOrWhiteSpace(campaign.WorkflowDefinitionId);
        if (campaign.ChannelAction == CampaignChannelAction.WorkflowStart ||
            (campaign.ChannelAction == CampaignChannelAction.Call && hasWorkflow) ||
            (campaign.ExecutionMode == CampaignExecutionMode.Workflow && hasWorkflow))
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
                ["recipient"] = execution.Recipient,
                ["variables"] = execution.Variables ?? new Dictionary<string, string>(),
                ["message"] = CampaignMessageRenderer.Render(campaign.MessageDraft ?? string.Empty, execution.Variables).Text,
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
            // Run the campaign's own workflow; fall back to the event listener only when none is set.
            var workflowExecution = hasWorkflow
                ? await _workflowTriggerService.TriggerWorkflowAsync(
                    campaign.TenantId,
                    campaign.WorkflowDefinitionId!,
                    "connect.campaign.triggered",
                    run.RequestedBy,
                    execution.Id,
                    payload,
                    ct)
                : await _workflowTriggerService.TriggerEventAsync(
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
            var sendMetadata = new Dictionary<string, string>
            {
                ["campaign_run_id"] = run.Id,
                ["campaign_contact_execution_id"] = execution.Id
            };
            string message;
            IReadOnlyList<string> missing;
            if (!string.IsNullOrWhiteSpace(campaign.WhatsAppTemplateId))
            {
                // A WhatsApp template: Meta needs it to start the conversation; the customer
                // reads it filled in with their own data.
                var template = _waTemplates is null ? null : await _waTemplates.GetAsync(campaign.TenantId, campaign.WhatsAppTemplateId!, ct);
                if (template is null || template.Status != AgentFlow.Api.Messaging.WhatsAppTemplateStatuses.Approved)
                {
                    return (await _campaignStore.UpdateContactExecutionAsync(execution with
                    {
                        Status = CampaignContactExecutionStatus.Failed,
                        SkipReason = template is null
                            ? "La plantilla de WhatsApp de la campana ya no existe."
                            : $"La plantilla {template.Name} no esta aprobada por Meta (estado {template.Status}).",
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, ct))!;
                }
                // A template only exists in its own Meta account: the campaign's number must belong to it.
                var channelWaba = await CampaignChannelWabaAsync(campaign, ct);
                if (channelWaba is not null && template.BusinessAccountId is not null && channelWaba != template.BusinessAccountId)
                {
                    return (await _campaignStore.UpdateContactExecutionAsync(execution with
                    {
                        Status = CampaignContactExecutionStatus.Failed,
                        SkipReason = $"La plantilla {template.Name} es de otra cuenta de Meta que el canal de la campana.",
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, ct))!;
                }
                var built = AgentFlow.Api.Messaging.WhatsAppTemplateText.Build(template, execution.Variables);
                message = built.Text;
                missing = built.Missing;
                sendMetadata[AgentFlow.Infrastructure.Channels.WhatsApp.WhatsAppTemplatePayload.MetadataKey] = built.ToSendJson();
                sendMetadata["wa.template_id"] = template.Id;
                sendMetadata["wa.template_version"] = template.Version.ToString();
            }
            else
            {
                (message, missing) = CampaignMessageRenderer.Render(campaign.MessageDraft ?? "Mensaje de campana", execution.Variables);
            }
            if (missing.Count > 0)
            {
                // Never send a message with "{{total_vencido}}" left in it.
                return (await _campaignStore.UpdateContactExecutionAsync(execution with
                {
                    Status = CampaignContactExecutionStatus.Failed,
                    SkipReason = "Faltan datos para el mensaje: " + string.Join(", ", missing),
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct))!;
            }

            // Real send through the campaign's channel; "Sent" only when the transport accepted it.
            var sent = await _outbound.SendAsync(
                campaign.TenantId,
                campaign.Channel,
                execution.Recipient,
                message,
                run.RequestedBy,
                campaign.Id,
                campaign.TemplateId,
                sendMetadata,
                ct);

            return (await _campaignStore.UpdateContactExecutionAsync(execution with
            {
                Status = sent.Sent ? CampaignContactExecutionStatus.Sent : CampaignContactExecutionStatus.Failed,
                RenderedMessage = message,
                ChannelMessageId = sent.ChannelMessageId ?? sent.InboxMessageId,
                SkipReason = sent.Sent ? execution.SkipReason : sent.Error,
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

    // Schedules are wall-clock times in the campaign's timezone ("09:00" = 9 am in America/Managua),
    // not in the server's UTC clock.
    public static DateTimeOffset ComputeDaily(string expression, DateTimeOffset now, string? timezone = null)
    {
        if (!TimeOnly.TryParse(expression, CultureInfo.InvariantCulture, out var time))
            time = new TimeOnly(9, 0);
        var local = CampaignContactPolicy.ToLocal(now, timezone);
        for (var i = 0; i < 3; i++)
        {
            var candidate = LocalToUtc(local.Date.AddDays(i) + time.ToTimeSpan(), timezone);
            if (candidate > now) return candidate;
        }
        return now.AddDays(1);
    }

    /// <param name="expression">"Monday:09:30" (day, then the time).</param>
    public static DateTimeOffset ComputeWeekly(string expression, DateTimeOffset now, string? timezone = null)
    {
        var separator = expression.IndexOf(':');
        var dayPart = separator < 0 ? expression : expression[..separator];
        var timePart = separator < 0 ? string.Empty : expression[(separator + 1)..];
        var day = Enum.TryParse<DayOfWeek>(dayPart.Trim(), true, out var parsedDay) ? parsedDay : DayOfWeek.Monday;
        var time = TimeOnly.TryParse(timePart.Trim(), CultureInfo.InvariantCulture, out var parsedTime) ? parsedTime : new TimeOnly(9, 0);

        var local = CampaignContactPolicy.ToLocal(now, timezone);
        for (var i = 0; i < 15; i++)
        {
            var date = local.Date.AddDays(i);
            if (date.DayOfWeek != day) continue;
            var candidate = LocalToUtc(date + time.ToTimeSpan(), timezone);
            if (candidate > now) return candidate;
        }
        return now.AddDays(7);
    }

    private static DateTimeOffset LocalToUtc(DateTime localWallClock, string? timezone)
    {
        var unspecified = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        try
        {
            var zone = string.IsNullOrWhiteSpace(timezone) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return new DateTimeOffset(unspecified, TimeSpan.Zero);
        }
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
