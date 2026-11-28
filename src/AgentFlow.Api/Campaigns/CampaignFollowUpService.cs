using System.Globalization;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Connect;
using AgentFlow.Extensions;

namespace AgentFlow.Api.Campaigns;

public sealed record CampaignFollowUpRunSummary
{
    public int Campaigns { get; init; }
    public int Evaluated { get; init; }
    public int Sent { get; init; }
    public int HandedOff { get; init; }
    public int Failed { get; init; }
    /// <summary>Due but outside the contact window; retried when it opens.</summary>
    public int Deferred { get; init; }
    /// <summary>Due but the recipient reached the daily cap; retried tomorrow.</summary>
    public int Limited { get; init; }
    /// <summary>Not sent because the customer no longer owes anything.</summary>
    public int Settled { get; init; }
}

public interface ICampaignFollowUpService
{
    Task<CampaignFollowUpRunSummary> RunForCampaignAsync(string tenantId, string campaignId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Sends already made to a recipient today (campaign sends + follow-ups, all campaigns).</summary>
    Task<int> CountSendsTodayAsync(string tenantId, string recipientKey, CampaignContactPolicy policy, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class CampaignFollowUpService : ICampaignFollowUpService
{
    private static readonly CampaignContactExecutionStatus[] Reached =
    [
        CampaignContactExecutionStatus.Sent, CampaignContactExecutionStatus.Delivered, CampaignContactExecutionStatus.Read,
        CampaignContactExecutionStatus.Connected, CampaignContactExecutionStatus.WorkflowStarted
    ];

    private readonly ICampaignStore _campaigns;
    private readonly IOutboundMessageSender _outbound;
    private readonly ICollectionsStore? _collections;
    private readonly AgentFlow.Api.Messaging.IWhatsAppTemplateStore? _templates;
    private readonly AgentFlow.Domain.Repositories.IChannelDefinitionRepository? _channels;

    private readonly AgentFlow.Api.Channels.IConversationWindow _window;

    public CampaignFollowUpService(
        ICampaignStore campaigns,
        IOutboundMessageSender outbound,
        ICollectionsStore? collections = null,
        AgentFlow.Api.Messaging.IWhatsAppTemplateStore? templates = null,
        AgentFlow.Domain.Repositories.IChannelDefinitionRepository? channels = null,
        AgentFlow.Api.Channels.IConversationWindow? window = null)
    {
        _window = window ?? new AgentFlow.Api.Channels.ConversationWindow(campaigns);
        _campaigns = campaigns;
        _outbound = outbound;
        _collections = collections;
        _templates = templates;
        _channels = channels;
    }

    public async Task<int> CountSendsTodayAsync(string tenantId, string recipientKey, CampaignContactPolicy policy, DateTimeOffset now, CancellationToken ct = default)
    {
        var dayStart = policy.LocalDayStartUtc(now);
        var contacts = await _campaigns.GetContactExecutionsByRecipientKeyAsync(tenantId, recipientKey, 50, ct);
        return contacts.Count(c => c.CreatedAt >= dayStart && Reached.Contains(c.Status))
             + contacts.Sum(c => (c.FollowUps ?? []).Count(f => f.Action == "message" && f.Status == "sent" && f.At >= dayStart));
    }

    public async Task<CampaignFollowUpRunSummary> RunForCampaignAsync(string tenantId, string campaignId, DateTimeOffset now, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetCampaignAsync(tenantId, campaignId, ct)
            ?? throw new KeyNotFoundException("Campaign not found.");
        var policy = CampaignFollowUpPolicy.Parse(campaign.FollowupPolicyJson);
        if (policy is null) return new CampaignFollowUpRunSummary { Campaigns = 1 };

        var contactPolicy = CampaignContactPolicy.From(campaign);
        var windowOpen = contactPolicy.IsOpen(now);
        var channel = _channels is null ? null : await CampaignChannels.ResolveAsync(_channels, campaign, ct);
        var officialWhatsApp = CampaignChannels.IsOfficialWhatsApp(channel);
        var templateCache = new Dictionary<string, AgentFlow.Api.Messaging.WhatsAppTemplateDocument?>();
        var optedOut = await _campaigns.GetOptedOutRecipientKeysAsync(tenantId, ct);
        var usesPromises = policy.Rules.Any(r => r.Trigger.StartsWith("promise_", StringComparison.OrdinalIgnoreCase));

        // Only the latest contact per recipient is followed up (a campaign may have run several times).
        var contacts = (await _campaigns.GetContactExecutionsByCampaignAsync(tenantId, campaignId, 5000, ct))
            .GroupBy(c => c.RecipientKey ?? c.Recipient, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.CreatedAt).First())
            .ToList();

        int evaluated = 0, sent = 0, handedOff = 0, failed = 0, deferred = 0, limited = 0, settled = 0;
        foreach (var contact in contacts)
        {
            var promise = usesPromises ? await FindPromiseAsync(contact, ct) : null;
            var due = CampaignFollowUpPlanner.Plan(contact, policy, now, promise);
            if (due.Count == 0) continue;
            evaluated++;

            var records = (contact.FollowUps ?? []).ToList();
            var updated = contact;
            foreach (var rule in due)
            {
                if (rule.Action == "handoff")
                {
                    records.Add(Record(rule, "handed_off", now));
                    updated = updated with
                    {
                        NeedsAttention = true,
                        AttentionReason = AttentionText(rule.Trigger),
                        AttentionAt = now
                    };
                    handedOff++;
                    continue;
                }

                if (contact.RecipientKey is not null && optedOut.Contains(contact.RecipientKey)) continue;
                if (!windowOpen) { deferred++; continue; }
                if (contactPolicy.MaxPerRecipientPerDay > 0 && contact.RecipientKey is not null &&
                    await CountSendsTodayAsync(tenantId, contact.RecipientKey, contactPolicy, now, ct) >= contactPolicy.MaxPerRecipientPerDay)
                {
                    limited++;
                    continue;
                }

                var variables = await CurrentVariablesAsync(contact, promise, contactPolicy, ct);
                if (variables.Settled && !rule.Trigger.StartsWith("outcome:", StringComparison.OrdinalIgnoreCase))
                {
                    // The customer paid since the campaign ran: never chase a settled debt.
                    records.Add(Record(rule, "skipped", now, error: "sin saldo vencido"));
                    settled++;
                    continue;
                }

                var metadata = new Dictionary<string, string>
                {
                    ["campaign_contact_execution_id"] = contact.Id,
                    ["campaign_follow_up_rule"] = rule.Id
                };
                string text;
                string? templateName = null;
                IReadOnlyList<string> missing;
                // Free text only while the customer wrote in the last 24 h; otherwise the step's template.
                var freeText = !officialWhatsApp || CampaignChannels.WindowOpen(contact, now) ||
                               await _window.CanSendFreeTextAsync(tenantId, channel, contact.Recipient, now, ct);
                var useTemplate = !string.IsNullOrWhiteSpace(rule.WhatsAppTemplateId) &&
                                  (!freeText || string.IsNullOrWhiteSpace(rule.Message));
                if (useTemplate)
                {
                    var template = await TemplateAsync(tenantId, rule.WhatsAppTemplateId!, templateCache, ct);
                    var problem = template is null ? "La plantilla del seguimiento ya no existe."
                        : template.Status != AgentFlow.Api.Messaging.WhatsAppTemplateStatuses.Approved ? $"La plantilla {template.Name} no esta aprobada (estado {template.Status})."
                        : CampaignChannels.MetaAccountOf(channel) is { } waba && template.BusinessAccountId is { } own && waba != own ? $"La plantilla {template.Name} es de otra cuenta de Meta que el canal."
                        : null;
                    if (problem is not null)
                    {
                        records.Add(Record(rule, "failed", now, error: problem));
                        failed++;
                        continue;
                    }
                    var built = AgentFlow.Api.Messaging.WhatsAppTemplateText.Build(template!, variables.Values);
                    (text, missing, templateName) = (built.Text, built.Missing, template!.Name);
                    metadata[AgentFlow.Infrastructure.Channels.WhatsApp.WhatsAppTemplatePayload.MetadataKey] = built.ToSendJson();
                    metadata["wa.template_id"] = template.Id;
                    metadata["wa.template_version"] = template.Version.ToString(CultureInfo.InvariantCulture);
                }
                else if (!freeText)
                {
                    // Meta would reject free text: say why instead of a failed send.
                    records.Add(Record(rule, "failed", now, error: "Pasaron mas de 24 h desde el ultimo mensaje del cliente: este seguimiento necesita una plantilla de WhatsApp aprobada."));
                    failed++;
                    continue;
                }
                else
                {
                    (text, missing) = CampaignMessageRenderer.Render(rule.Message ?? string.Empty, variables.Values);
                }

                if (string.IsNullOrWhiteSpace(text) || missing.Count > 0)
                {
                    records.Add(Record(rule, "failed", now, text, missing.Count > 0 ? "Faltan datos: " + string.Join(", ", missing) : "Mensaje vacio", templateName));
                    failed++;
                    continue;
                }

                var result = await _outbound.SendAsync(
                    tenantId, campaign.Channel, contact.Recipient, text, "campaign-follow-up", campaign.Id, campaign.TemplateId, metadata, ct);

                records.Add(Record(rule, result.Sent ? "sent" : "failed", now, text, result.Sent ? null : result.Error, templateName));
                if (result.Sent) sent++; else failed++;
            }

            await _campaigns.UpdateContactExecutionAsync(updated with { FollowUps = records, UpdatedAt = DateTimeOffset.UtcNow }, ct);
        }

        return new CampaignFollowUpRunSummary
        {
            Campaigns = 1, Evaluated = evaluated, Sent = sent, HandedOff = handedOff, Failed = failed,
            Deferred = deferred, Limited = limited, Settled = settled
        };
    }

    private async Task<AgentFlow.Api.Messaging.WhatsAppTemplateDocument?> TemplateAsync(
        string tenantId, string id, Dictionary<string, AgentFlow.Api.Messaging.WhatsAppTemplateDocument?> cache, CancellationToken ct)
    {
        if (!cache.TryGetValue(id, out var template))
            cache[id] = template = _templates is null ? null : await _templates.GetAsync(tenantId, id, ct);
        return template;
    }

    private async Task<CollectionPromiseDocument?> FindPromiseAsync(CampaignContactExecutionContract contact, CancellationToken ct)
    {
        if (_collections is null || string.IsNullOrWhiteSpace(contact.PartyId)) return null;
        var promises = await _collections.GetPromisesAsync(contact.TenantId, null, contact.PartyId, 50, ct);
        return promises.FirstOrDefault(p => p.ContactExecutionId == contact.Id)
            ?? promises.Where(p => p.CreatedAt >= contact.CreatedAt).OrderByDescending(p => p.CreatedAt).FirstOrDefault();
    }

    /// <summary>The contact's variables, refreshed with today's portfolio (a follow-up quotes the current debt).</summary>
    private async Task<(Dictionary<string, string> Values, bool Settled)> CurrentVariablesAsync(
        CampaignContactExecutionContract contact, CollectionPromiseDocument? promise, CampaignContactPolicy policy, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(contact.Variables ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var settled = false;

        if (_collections is not null && !string.IsNullOrWhiteSpace(contact.PartyId))
        {
            var loans = (await _collections.GetLoansByPartyAsync(contact.TenantId, contact.PartyId!, ct))
                .Where(l => l.Status != CollectionLoanStatus.PaidOff && l.Status != CollectionLoanStatus.ChargedOff)
                .ToList();
            if (loans.Count > 0 || values.ContainsKey("total_vencido"))
            {
                var overdue = loans.Sum(l => l.OverdueBalance);
                settled = overdue <= 0;
                values["total_vencido"] = Money(overdue);
                values["saldo_total"] = Money(loans.Sum(l => l.TotalBalance));
                values["dias_atraso"] = (loans.Count == 0 ? 0 : loans.Max(l => l.DaysPastDue)).ToString(CultureInfo.InvariantCulture);
                values["num_prestamos"] = loans.Count.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (promise is not null)
        {
            values["monto_promesa"] = promise.Amount is { } amount ? Money(amount) : values.GetValueOrDefault("total_vencido", string.Empty);
            values["fecha_promesa"] = CampaignContactPolicy.ToLocal(promise.PromisedDate, policy.Timezone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        return (values, settled);
    }

    private static CampaignFollowUpRecord Record(CampaignFollowUpRule rule, string status, DateTimeOffset at, string? message = null, string? error = null, string? template = null) => new()
    {
        RuleId = rule.Id,
        Trigger = rule.Trigger,
        Action = rule.Action,
        Status = status,
        At = at,
        Message = message,
        Template = template,
        Error = error
    };

    private static string AttentionText(string trigger) => trigger switch
    {
        "outcome:needs_human" => "Pidio hablar con un asesor",
        "outcome:callback_requested" => "Pidio que lo contacten despues",
        "outcome:cannot_pay" => "Dice que no puede pagar: ofrecer convenio",
        "outcome:dispute" => "Desconoce la deuda: revisar",
        "promise_broken" => "Incumplio su promesa de pago",
        "no_response" => "No responde a los mensajes",
        _ => trigger.StartsWith("outcome:") ? $"Resultado: {CampaignOutcomes.Find(trigger[8..])?.Label ?? trigger[8..]}" : trigger
    };

    private static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}

/// <summary>Runs every campaign's follow-ups every few minutes.</summary>
public sealed class CampaignFollowUpWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CampaignFollowUpWorker> _logger;
    private readonly TimeSpan _interval;

    public CampaignFollowUpWorker(IServiceScopeFactory scopeFactory, ILogger<CampaignFollowUpWorker> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Campaigns:FollowUpIntervalMinutes", 5)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ICampaignStore>();
                var followUps = scope.ServiceProvider.GetRequiredService<ICampaignFollowUpService>();
                var entitlements = scope.ServiceProvider.GetRequiredService<IAppEntitlementService>();
                var locks = scope.ServiceProvider.GetService<AgentFlow.Abstractions.IDistributedLockService>();

                foreach (var campaign in await store.GetCampaignsWithFollowUpsAsync(stoppingToken))
                {
                    if (campaign.Status is CampaignStatus.Paused or CampaignStatus.Archived or CampaignStatus.Draft) continue;
                    if (!await entitlements.IsEnabledAsync(campaign.TenantId, AppModules.Campaigns, stoppingToken)) continue;
                    // One instance per campaign: two would send the same follow-up twice.
                    await using var held = locks is null ? null : await locks.TryAcquireAsync($"campaign-followups:{campaign.TenantId}:{campaign.Id}", TimeSpan.FromMinutes(10), stoppingToken);
                    if (locks is not null && held is null) continue;
                    try
                    {
                        var summary = await followUps.RunForCampaignAsync(campaign.TenantId, campaign.Id, DateTimeOffset.UtcNow, stoppingToken);
                        if (summary.Sent + summary.HandedOff + summary.Failed > 0)
                            _logger.LogInformation("Follow-ups for campaign {CampaignId}: {Sent} sent, {HandedOff} handed off, {Failed} failed",
                                campaign.Id, summary.Sent, summary.HandedOff, summary.Failed);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Follow-ups failed for campaign {CampaignId}", campaign.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Campaign follow-up iteration failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
