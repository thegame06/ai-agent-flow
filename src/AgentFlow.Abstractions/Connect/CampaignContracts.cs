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
    public string? MessageDraft { get; init; }
    public string? CallScriptDraft { get; init; }
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
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
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
