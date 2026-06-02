using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Api.Workflow;

namespace AgentFlow.Api.Campaigns;

public interface ICampaignBuilderService
{
    Task<CampaignBuilderDraftContract> DraftFromPromptAsync(string tenantId, string prompt, string userId, CancellationToken ct = default);
    Task<CampaignBuilderDraftContract> RefineAsync(string tenantId, CampaignBuilderDraftContract current, string prompt, string userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ValidateAsync(string tenantId, CampaignBuilderDraftContract draft, CancellationToken ct = default);
}

public sealed class CampaignBuilderService : ICampaignBuilderService
{
    private readonly IWorkflowStudioStore _workflowStore;
    private readonly IRuntimeModelProfileStore _runtimeProfiles;

    public CampaignBuilderService(IWorkflowStudioStore workflowStore, IRuntimeModelProfileStore runtimeProfiles)
    {
        _workflowStore = workflowStore;
        _runtimeProfiles = runtimeProfiles;
    }

    public async Task<CampaignBuilderDraftContract> DraftFromPromptAsync(string tenantId, string prompt, string userId, CancellationToken ct = default)
    {
        var normalized = prompt.Trim();
        var lower = normalized.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var campaignType = lower.Contains("cobro") || lower.Contains("factura")
            ? CampaignType.Collections
            : lower.Contains("recordatorio")
                ? CampaignType.Reminder
                : lower.Contains("reactiv")
                    ? CampaignType.Reactivation
                    : CampaignType.Sales;

        var channel = lower.Contains("call center")
            ? "callcenter"
            : lower.Contains("llamada") || lower.Contains("voz")
                ? "voice"
                : "whatsapp";

        var channelAction = channel is "voice" or "callcenter"
            ? CampaignChannelAction.Call
            : CampaignChannelAction.Message;

        var executionMode = channelAction == CampaignChannelAction.Call
            ? CampaignExecutionMode.Workflow
            : CampaignExecutionMode.Hybrid;
        var runtimeProfile = channelAction == CampaignChannelAction.Call
            ? _runtimeProfiles.GetDefault(tenantId, "Voice")
            : null;

        var workflow = (await _workflowStore.GetDefinitionsAsync(tenantId, ct))
            .Where(x => x.Status == WorkflowDefinitionStatus.Published &&
                        string.Equals(x.TriggerEventName, "connect.campaign.triggered", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefault();

        var assumptions = new List<string>
        {
            "Se asumio schedule diario a las 09:00 si el prompt no define otro horario.",
            "Se priorizo workflow para llamadas y modo hybrid para mensajeria saliente."
        };
        if (runtimeProfile is not null)
            assumptions.Add($"Se aplico el perfil runtime de voz '{runtimeProfile.Name}' ({runtimeProfile.Id}) para reutilizar brain/stt/tts y metadata de voz.");

        var warnings = new List<string>();
        if (workflow is null)
            warnings.Add("No hay workflow publicado para connect.campaign.triggered. La campana quedara lista, pero no podra iniciar workflows hasta que publiques uno.");
        if (channelAction == CampaignChannelAction.Call && runtimeProfile is null)
            warnings.Add("No hay perfil runtime Voice predeterminado. La campana de llamada quedara sin runtimeModelProfileId hasta que configures uno.");

        var segmentFilter = BuildSuggestedFilter(lower, channel);
        var messageDraft = BuildSuggestedMessage(campaignType, lower);
        var goal = BuildGoal(campaignType, lower);
        var callScript = channelAction == CampaignChannelAction.Call
            ? "Hola, te contactamos para darte seguimiento sobre tu caso. Quiero ayudarte a resolverlo hoy."
            : null;
        var playbookId = channelAction == CampaignChannelAction.Call ? Guid.NewGuid().ToString("N") : null;
        var playbookDraft = channelAction == CampaignChannelAction.Call
            ? BuildPlaybookDraft(tenantId, playbookId!, goal, normalized, userId, now)
            : null;

        return new CampaignBuilderDraftContract
        {
            CampaignDraft = new CampaignContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Name = BuildName(campaignType, channel),
                Description = normalized,
                Status = CampaignStatus.Draft,
                CampaignType = campaignType,
                ExecutionMode = executionMode,
                TriggerType = CampaignTriggerType.Schedule,
                ChannelAction = channelAction,
                Channel = channel,
                Goal = goal,
                PlaybookId = playbookDraft?.Id,
                WorkflowDefinitionId = workflow?.Id,
                RuntimeModelProfileId = runtimeProfile?.Id,
                MessageDraft = messageDraft,
                CallScriptDraft = callScript,
                PromptOrigin = normalized,
                ScheduleType = CampaignScheduleType.Daily,
                ScheduleExpression = "09:00",
                Timezone = "America/Managua",
                StartAt = now,
                AudienceFilterJson = JsonSerializer.Serialize(segmentFilter),
                Enabled = true,
                NextRunAt = now.Date.AddDays(1).AddHours(9),
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = userId
            },
            SegmentDraft = new CampaignSegmentContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Name = $"Segmento {BuildName(campaignType, channel)}",
                Description = $"Generado desde prompt: {normalized}",
                SourceModules = ["commerce", "inbox", "audit", "threads"],
                FilterJson = JsonSerializer.Serialize(segmentFilter),
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = userId
            },
            PlaybookDraft = playbookDraft,
            RecommendedWorkflowLink = workflow?.Id,
            MessageDraft = messageDraft,
            CallScriptDraft = callScript,
            Assumptions = assumptions,
            Warnings = warnings
        };
    }

    public async Task<CampaignBuilderDraftContract> RefineAsync(string tenantId, CampaignBuilderDraftContract current, string prompt, string userId, CancellationToken ct = default)
    {
        var refined = await DraftFromPromptAsync(tenantId, $"{current.CampaignDraft.PromptOrigin}\n{prompt}".Trim(), userId, ct);
        return refined with
        {
            CampaignDraft = refined.CampaignDraft with
            {
                Id = current.CampaignDraft.Id,
                WorkflowDefinitionId = string.IsNullOrWhiteSpace(refined.CampaignDraft.WorkflowDefinitionId)
                    ? current.CampaignDraft.WorkflowDefinitionId
                    : refined.CampaignDraft.WorkflowDefinitionId
            },
            SegmentDraft = refined.SegmentDraft with
            {
                Id = current.SegmentDraft.Id
            }
        };
    }

    public Task<IReadOnlyList<string>> ValidateAsync(string tenantId, CampaignBuilderDraftContract draft, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.CampaignDraft.Name))
            warnings.Add("El nombre de la campana es requerido.");
        if (draft.CampaignDraft.ChannelAction == CampaignChannelAction.Call &&
            string.IsNullOrWhiteSpace(draft.CampaignDraft.WorkflowDefinitionId))
            warnings.Add("Las campanas de llamada requieren workflowDefinitionId en este MVP.");
        if (draft.CampaignDraft.ChannelAction == CampaignChannelAction.Call &&
            string.IsNullOrWhiteSpace(draft.CampaignDraft.PlaybookId))
            warnings.Add("Las campanas de llamada deben referenciar un playbook de preguntas.");
        if (string.IsNullOrWhiteSpace(draft.CampaignDraft.AudienceFilterJson) && string.IsNullOrWhiteSpace(draft.CampaignDraft.SegmentId))
            warnings.Add("Debes definir un segmento o filtros inline.");
        if (draft.CampaignDraft.ScheduleType != CampaignScheduleType.Once &&
            string.IsNullOrWhiteSpace(draft.CampaignDraft.ScheduleExpression))
            warnings.Add("La campana necesita una expresion de schedule.");
        return Task.FromResult<IReadOnlyList<string>>(warnings);
    }

    private static object BuildSuggestedFilter(string lower, string channel)
    {
        if (lower.Contains("factura") || lower.Contains("cobro"))
        {
            return new
            {
                channel,
                minOverdueDays = lower.Contains("3 dias") ? 3 : 1,
                minOutstandingAmount = 1,
                excludePaid = true
            };
        }

        if (lower.Contains("promocion") || lower.Contains("producto") || lower.Contains("celular"))
        {
            return new
            {
                channel = "whatsapp",
                productKeywords = new[] { ExtractKeyword(lower, "celular", "producto", "promocion") },
                minPurchaseCount = 0
            };
        }

        if (lower.Contains("frecuente"))
        {
            return new
            {
                channel,
                minPurchaseCount = 2,
                minTotalPurchased = 100
            };
        }

        return new
        {
            channel,
            kind = "lead"
        };
    }

    private static string BuildSuggestedMessage(CampaignType campaignType, string lower)
    {
        return campaignType switch
        {
            CampaignType.Collections => "Hola, te contactamos porque tienes un pago pendiente. Si gustas, puedo ayudarte a resolverlo hoy.",
            CampaignType.Reminder => "Hola, este es un recordatorio amistoso sobre tu compromiso pendiente. Si necesitas apoyo, aqui estoy.",
            CampaignType.Reactivation => "Hola, hace tiempo no hablamos. Quiero compartirte una opcion que podria interesarte.",
            _ when lower.Contains("promocion") => "Hola, vimos tu interes en nuestras promociones. Te puedo compartir opciones actualizadas si gustas.",
            _ => "Hola, queria darte seguimiento y ayudarte con la opcion que mejor se ajuste a lo que buscas."
        };
    }

    private static string BuildName(CampaignType campaignType, string channel)
        => $"{campaignType} {channel} {DateTimeOffset.UtcNow:yyyyMMdd-HHmm}";

    private static string BuildGoal(CampaignType campaignType, string lower)
    {
        if (campaignType == CampaignType.Collections) return "collect_payment";
        if (lower.Contains("encuesta")) return "post_sale_survey";
        if (lower.Contains("lead")) return "qualify_lead";
        if (campaignType == CampaignType.Reminder) return "remind_customer";
        return "follow_up_sale";
    }

    private static CampaignCallPlaybookContract BuildPlaybookDraft(
        string tenantId,
        string playbookId,
        string goal,
        string prompt,
        string userId,
        DateTimeOffset now)
    {
        var questions = goal switch
        {
            "collect_payment" => new object[]
            {
                new { key = "confirm_balance", question = "Reconoce el saldo pendiente?", type = "boolean" },
                new { key = "promise_date", question = "Que fecha propone para el pago?", type = "date" },
                new { key = "needs_human", question = "Necesita apoyo de un agente humano?", type = "boolean" }
            },
            "qualify_lead" => new object[]
            {
                new { key = "interest_level", question = "Que tan interesado sigues en la oferta?", type = "scale_1_5" },
                new { key = "budget_range", question = "Que presupuesto tienes considerado?", type = "text" },
                new { key = "purchase_window", question = "Cuando esperas tomar la decision?", type = "text" }
            },
            _ => new object[]
            {
                new { key = "satisfaction_score", question = "Como calificas tu experiencia del 1 al 5?", type = "scale_1_5" },
                new { key = "issue_reported", question = "Tuviste algun inconveniente?", type = "boolean" },
                new { key = "followup_requested", question = "Deseas que un asesor te contacte?", type = "boolean" }
            }
        };

        return new CampaignCallPlaybookContract
        {
            Id = playbookId,
            TenantId = tenantId,
            Name = $"Playbook {goal} {now:yyyyMMdd-HHmm}",
            Description = $"Generado desde prompt: {prompt}",
            Purpose = goal,
            Channel = "voice",
            OpeningScript = "Hola, te llamamos para dar seguimiento y hacerte unas preguntas breves.",
            QuestionsJson = JsonSerializer.Serialize(questions),
            AnswerSchemaJson = JsonSerializer.Serialize(new
            {
                goal,
                fields = questions.Select(q => q.GetType().GetProperty("key")?.GetValue(q)?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))
            }),
            CompletionRulesJson = """{"minAnswered":2}""",
            FallbackRulesJson = """{"onNoAnswer":"reschedule","onRefused":"mark_refused"}""",
            HandoffRulesJson = """{"whenNeedsHuman":true}""",
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = userId
        };
    }

    private static string ExtractKeyword(string lower, params string[] candidates)
        => candidates.FirstOrDefault(candidate => lower.Contains(candidate, StringComparison.OrdinalIgnoreCase)) ?? "producto";
}
