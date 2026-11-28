namespace AgentFlow.Abstractions.Connect;

public enum CampaignStatus
{
    Draft,
    Published,
    Active,
    Paused,
    Completed,
    Failed,
    Archived
}

public enum CampaignType
{
    Sales,
    Collections,
    Reminder,
    Reactivation,
    Custom
}

public enum CampaignExecutionMode
{
    Workflow,
    Direct,
    Hybrid
}

public enum CampaignTriggerType
{
    Schedule,
    Manual,
    EventAssisted
}

public enum CampaignChannelAction
{
    Message,
    Call,
    WorkflowStart
}

public enum CampaignScheduleType
{
    Once,
    Hourly,
    Daily,
    Weekly,
    Cron
}

public enum CampaignRunStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum CampaignRunTrigger
{
    Schedule,
    Manual,
    Retry
}

public enum CampaignContactExecutionStatus
{
    Queued,
    Sent,
    Delivered,
    Read,
    Failed,
    Connected,
    WorkflowStarted,
    Skipped
}

public enum CampaignCallOutcomeStatus
{
    Queued,
    InProgress,
    Completed,
    Partial,
    NoAnswer,
    Voicemail,
    Refused,
    NeedsHuman,
    Failed
}

public sealed record CampaignContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public CampaignStatus Status { get; init; } = CampaignStatus.Draft;
    public CampaignType CampaignType { get; init; } = CampaignType.Custom;
    public CampaignExecutionMode ExecutionMode { get; init; } = CampaignExecutionMode.Workflow;
    public CampaignTriggerType TriggerType { get; init; } = CampaignTriggerType.Schedule;
    public CampaignChannelAction ChannelAction { get; init; } = CampaignChannelAction.WorkflowStart;
    public string Channel { get; init; } = "whatsapp";
    public string Goal { get; init; } = string.Empty;
    public string? PlaybookId { get; init; }
    public string? WorkflowDefinitionId { get; init; }
    public string? AssistantId { get; init; }
    public string? RuntimeModelProfileId { get; init; }
    public string? TemplateId { get; init; }
    /// <summary>
    /// Approved WhatsApp template (Meta) the first message is sent with: Meta only lets a
    /// business start a conversation with a template. Its placeholders are filled from the contact.
    /// </summary>
    public string? WhatsAppTemplateId { get; init; }
    public string? MessageDraft { get; init; }
    public string? CallScriptDraft { get; init; }
    /// <summary>
    /// Call campaigns: "conversation" (default) hands the call to the voice assistant; "notify" reads
    /// the script twice and hangs up (a reminder, also left on voicemail).
    /// </summary>
    public string? CallMode { get; init; }
    public string? PromptOrigin { get; init; }
    public CampaignScheduleType ScheduleType { get; init; } = CampaignScheduleType.Once;
    public string ScheduleExpression { get; init; } = string.Empty;
    public string Timezone { get; init; } = "America/Managua";
    public DateTimeOffset StartAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndAt { get; init; }
    public string? ExecutionWindowJson { get; init; }
    public string? ThrottleJson { get; init; }
    public string? SegmentId { get; init; }
    public string AudienceFilterJson { get; init; } = "{}";
    public string? DedupePolicyJson { get; init; }
    public string? SuccessPolicyJson { get; init; }
    public string? FollowupPolicyJson { get; init; }
    public string? ResultMappingJson { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? LastRunAt { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record CampaignSegmentContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceModules { get; init; } = [];
    public string FilterJson { get; init; } = "{}";
    public int? EstimatedCount { get; init; }
    public string? SamplePreviewJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record CampaignRunContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string CampaignId { get; init; } = string.Empty;
    public CampaignRunStatus Status { get; init; } = CampaignRunStatus.Pending;
    public CampaignRunTrigger TriggeredBy { get; init; } = CampaignRunTrigger.Schedule;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public string AudienceSnapshotJson { get; init; } = "[]";
    public string CountersJson { get; init; } = "{}";
    public string? ErrorSummaryJson { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
}

public sealed record CampaignContactExecutionContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string CampaignId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string? PartyId { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public CampaignContactExecutionStatus Status { get; init; } = CampaignContactExecutionStatus.Queued;
    public string? SkipReason { get; init; }
    public string? WorkflowExecutionId { get; init; }
    public string? ChannelMessageId { get; init; }
    public string? CallId { get; init; }
    public string? CallOutcomeId { get; init; }
    public string? AssistantExecutionId { get; init; }
    /// <summary>Name shown to operators (from the audience source).</summary>
    public string? DisplayName { get; init; }
    /// <summary>Per-contact template variables (e.g. total_vencido) resolved from the audience source.</summary>
    public IReadOnlyDictionary<string, string>? Variables { get; init; }
    /// <summary>The message actually sent to this contact, after variables were filled in.</summary>
    public string? RenderedMessage { get; init; }
    /// <summary>Last digits of the recipient, used to attribute inbound replies to this contact.</summary>
    public string? RecipientKey { get; init; }
    public DateTimeOffset? RepliedAt { get; init; }
    /// <summary>The customer's latest reply: WhatsApp's 24-hour window for free text counts from here.</summary>
    public DateTimeOffset? LastReplyAt { get; init; }
    public string? LastReply { get; init; }
    public int ReplyCount { get; init; }
    /// <summary>Business result of the contact (see <see cref="CampaignOutcomes"/>); null while unknown.</summary>
    public string? Outcome { get; init; }
    /// <summary>Who set the outcome: system, reply, agent, workflow or human.</summary>
    public string? OutcomeSource { get; init; }
    public DateTimeOffset? OutcomeAt { get; init; }
    public string? OutcomeBy { get; init; }
    public string? OutcomeNotes { get; init; }
    public decimal? PromiseAmount { get; init; }
    public DateTimeOffset? PromiseDate { get; init; }
    /// <summary>Follow-ups the campaign's policy already ran for this contact (each rule at most once).</summary>
    public IReadOnlyList<CampaignFollowUpRecord>? FollowUps { get; init; }
    /// <summary>A person should take over (e.g. the customer asked for an advisor).</summary>
    public bool NeedsAttention { get; init; }
    /// <summary>
    /// A failed send that a retry sent again: the retry's contact is the one that counts, so this one
    /// is left out of results, metrics and recovery (the customer was contacted once).
    /// </summary>
    public string? RetriedByExecutionId { get; init; }
    public string? AttentionReason { get; init; }
    public DateTimeOffset? AttentionAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A follow-up executed for a campaign contact.</summary>
public sealed record CampaignFollowUpRecord
{
    public string RuleId { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    /// <summary>message or handoff.</summary>
    public string Action { get; init; } = string.Empty;
    /// <summary>sent, failed or handed_off.</summary>
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset At { get; init; }
    public string? Message { get; init; }
    /// <summary>The WhatsApp template it was sent with (outside the 24-hour window).</summary>
    public string? Template { get; init; }
    public string? Error { get; init; }
}

/// <summary>One business result a campaign contact can end in.</summary>
/// <param name="Category">positive, negative, neutral or unreachable — drives colors and rates.</param>
/// <param name="CampaignTypes">Campaign types the outcome is offered for; empty = all.</param>
public sealed record CampaignOutcomeDefinition(
    string Code,
    string Label,
    string Category,
    IReadOnlyList<CampaignType> CampaignTypes);

/// <summary>
/// Generic catalog of contact outcomes shared by every campaign type. Collections-only outcomes
/// (promise to pay, already paid, ...) are tagged so sales campaigns don't offer them.
/// </summary>
public static class CampaignOutcomes
{
    public const string NoResponse = "no_response";
    public const string Unreachable = "unreachable";
    public const string Replied = "replied";
    public const string Interested = "interested";
    public const string NotInterested = "not_interested";
    public const string PromiseToPay = "promise_to_pay";
    public const string AlreadyPaid = "already_paid";
    public const string CannotPay = "cannot_pay";
    public const string Dispute = "dispute";
    public const string CallbackRequested = "callback_requested";
    public const string WrongNumber = "wrong_number";
    public const string OptOut = "opt_out";
    public const string NeedsHuman = "needs_human";

    public static class Sources
    {
        public const string System = "system";
        public const string Reply = "reply";
        public const string Agent = "agent";
        public const string Workflow = "workflow";
        public const string Human = "human";
    }

    private static readonly CampaignType[] CollectionsOnly = [CampaignType.Collections, CampaignType.Reminder];

    public static readonly IReadOnlyList<CampaignOutcomeDefinition> All =
    [
        new(Interested, "Dijo que si / interesado", "positive", []),
        new(PromiseToPay, "Promesa de pago", "positive", CollectionsOnly),
        new(AlreadyPaid, "Dice que ya pago", "positive", CollectionsOnly),
        new(CallbackRequested, "Pidio que lo contacten despues", "neutral", []),
        new(NeedsHuman, "Requiere asesor", "neutral", []),
        new(Replied, "Respondio (sin clasificar)", "neutral", []),
        new(NotInterested, "Dijo que no / no interesado", "negative", []),
        new(CannotPay, "No puede pagar", "negative", CollectionsOnly),
        new(Dispute, "Desconoce la deuda", "negative", CollectionsOnly),
        new(OptOut, "Pidio no ser contactado", "negative", []),
        new(WrongNumber, "Numero equivocado", "unreachable", []),
        new(NoResponse, "Sin respuesta", "unreachable", []),
        new(Unreachable, "No se pudo contactar", "unreachable", [])
    ];

    public static CampaignOutcomeDefinition? Find(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<CampaignOutcomeDefinition> For(CampaignType type) =>
        All.Where(x => x.CampaignTypes.Count == 0 || x.CampaignTypes.Contains(type)).ToList();
}

public sealed record CampaignCallPlaybookContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string Channel { get; init; } = "voice";
    public string OpeningScript { get; init; } = string.Empty;
    public string QuestionsJson { get; init; } = "[]";
    public string AnswerSchemaJson { get; init; } = "{}";
    public string? CompletionRulesJson { get; init; }
    public string? FallbackRulesJson { get; init; }
    public string? HandoffRulesJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record CampaignCallOutcomeContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string CampaignId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string ContactExecutionId { get; init; } = string.Empty;
    public string? PlaybookId { get; init; }
    public string? CallId { get; init; }
    public CampaignCallOutcomeStatus Status { get; init; } = CampaignCallOutcomeStatus.Queued;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; init; }
    public string? TranscriptJson { get; init; }
    public string AnswersJson { get; init; } = "{}";
    public string Summary { get; init; } = string.Empty;
    public string Sentiment { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
    public string? LinkedPartyId { get; init; }
    public string? LinkedSaleId { get; init; }
    public string? LinkedInvoiceId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CampaignAudiencePreviewContract
{
    public int EstimatedCount { get; init; }
    public string FilterJson { get; init; } = "{}";
    public IReadOnlyList<CampaignAudienceContactContract> Contacts { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record CampaignAudienceContactContract
{
    public string PartyId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int PurchaseCount { get; init; }
    public decimal TotalPurchased { get; init; }
    public int OpenInvoiceCount { get; init; }
    public decimal OutstandingAmount { get; init; }
    public string Reason { get; init; } = string.Empty;
    /// <summary>Template variables for this contact (e.g. nombre, total_vencido, dias_atraso).</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
}

public sealed record CampaignBuilderDraftContract
{
    public CampaignContract CampaignDraft { get; init; } = new();
    public CampaignSegmentContract SegmentDraft { get; init; } = new();
    public CampaignCallPlaybookContract? PlaybookDraft { get; init; }
    public string? RecommendedWorkflowLink { get; init; }
    public string? MessageDraft { get; init; }
    public string? CallScriptDraft { get; init; }
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record CampaignMetricsContract
{
    public string CampaignId { get; init; } = string.Empty;
    public int Runs { get; init; }
    public int Queued { get; init; }
    public int Sent { get; init; }
    public int Delivered { get; init; }
    public int Read { get; init; }
    public int Failed { get; init; }
    public int Connected { get; init; }
    public int WorkflowStarted { get; init; }
    public int Skipped { get; init; }
}

/// <summary>
/// Normalizes phone-like recipients so an inbound reply ("5215512345678@c.us") can be matched to
/// the number a campaign sent to ("+52 55 1234 5678"): digits only, last 10 digits.
/// </summary>
public static class RecipientKeys
{
    private static readonly string[] Prefixes = ["whatsapp:", "tel:", "sms:"];

    public static string? From(string? recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        var value = recipient.Trim();
        foreach (var prefix in Prefixes)
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) value = value[prefix.Length..];
        var at = value.IndexOf('@');
        var local = at > 0 ? value[..at] : value;

        // Not a phone number (user id, email local part...): match it as-is, case-insensitively.
        if (local.Any(char.IsLetter)) return value.ToLowerInvariant();

        var digits = new string(local.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        return digits.Length > 10 ? digits[^10..] : digits;
    }
}
