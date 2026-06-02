using AgentFlow.Abstractions.Connect;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Campaigns;

public interface ICampaignStore
{
    Task<IReadOnlyList<CampaignContract>> GetCampaignsAsync(string tenantId, CancellationToken ct = default);
    Task<CampaignContract?> GetCampaignAsync(string tenantId, string campaignId, CancellationToken ct = default);
    Task<CampaignContract> UpsertCampaignAsync(CampaignContract campaign, CancellationToken ct = default);
    Task<IReadOnlyList<CampaignContract>> GetDueCampaignsAsync(DateTimeOffset at, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignSegmentContract>> GetSegmentsAsync(string tenantId, CancellationToken ct = default);
    Task<CampaignSegmentContract?> GetSegmentAsync(string tenantId, string segmentId, CancellationToken ct = default);
    Task<CampaignSegmentContract> UpsertSegmentAsync(CampaignSegmentContract segment, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignCallPlaybookContract>> GetPlaybooksAsync(string tenantId, CancellationToken ct = default);
    Task<CampaignCallPlaybookContract?> GetPlaybookAsync(string tenantId, string playbookId, CancellationToken ct = default);
    Task<CampaignCallPlaybookContract> UpsertPlaybookAsync(CampaignCallPlaybookContract playbook, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignRunContract>> GetRunsAsync(string tenantId, string? campaignId = null, int limit = 100, CancellationToken ct = default);
    Task<CampaignRunContract?> GetRunAsync(string tenantId, string runId, CancellationToken ct = default);
    Task<CampaignRunContract> CreateRunAsync(CampaignRunContract run, CancellationToken ct = default);
    Task<CampaignRunContract?> UpdateRunAsync(CampaignRunContract run, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignContactExecutionContract>> GetContactExecutionsAsync(string tenantId, string runId, CancellationToken ct = default);
    Task<CampaignContactExecutionContract> CreateContactExecutionAsync(CampaignContactExecutionContract contact, CancellationToken ct = default);
    Task<CampaignContactExecutionContract?> UpdateContactExecutionAsync(CampaignContactExecutionContract contact, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignCallOutcomeContract>> GetCallOutcomesByRunAsync(string tenantId, string runId, CancellationToken ct = default);
    Task<CampaignCallOutcomeContract?> GetCallOutcomeByContactAsync(string tenantId, string contactExecutionId, CancellationToken ct = default);
    Task<CampaignCallOutcomeContract?> GetCallOutcomeAsync(string tenantId, string outcomeId, CancellationToken ct = default);
    Task<CampaignCallOutcomeContract> CreateCallOutcomeAsync(CampaignCallOutcomeContract outcome, CancellationToken ct = default);
    Task<CampaignCallOutcomeContract?> UpdateCallOutcomeAsync(CampaignCallOutcomeContract outcome, CancellationToken ct = default);
}

public sealed class MongoCampaignStore : ICampaignStore
{
    private readonly IMongoCollection<CampaignDocument> _campaigns;
    private readonly IMongoCollection<CampaignSegmentDocument> _segments;
    private readonly IMongoCollection<CampaignCallPlaybookDocument> _playbooks;
    private readonly IMongoCollection<CampaignRunDocument> _runs;
    private readonly IMongoCollection<CampaignContactExecutionDocument> _contacts;
    private readonly IMongoCollection<CampaignCallOutcomeDocument> _outcomes;

    public MongoCampaignStore(IMongoDatabase db)
    {
        _campaigns = db.GetCollection<CampaignDocument>("connect_campaigns_v2");
        _segments = db.GetCollection<CampaignSegmentDocument>("connect_campaign_segments");
        _playbooks = db.GetCollection<CampaignCallPlaybookDocument>("connect_campaign_call_playbooks");
        _runs = db.GetCollection<CampaignRunDocument>("connect_campaign_runs");
        _contacts = db.GetCollection<CampaignContactExecutionDocument>("connect_campaign_contact_executions");
        _outcomes = db.GetCollection<CampaignCallOutcomeDocument>("connect_campaign_call_outcomes");

        _campaigns.Indexes.CreateOne(new CreateIndexModel<CampaignDocument>(
            Builders<CampaignDocument>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.UpdatedAt)));
        _campaigns.Indexes.CreateOne(new CreateIndexModel<CampaignDocument>(
            Builders<CampaignDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.NextRunAt)));
        _segments.Indexes.CreateOne(new CreateIndexModel<CampaignSegmentDocument>(
            Builders<CampaignSegmentDocument>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.UpdatedAt)));
        _playbooks.Indexes.CreateOne(new CreateIndexModel<CampaignCallPlaybookDocument>(
            Builders<CampaignCallPlaybookDocument>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.UpdatedAt)));
        _runs.Indexes.CreateOne(new CreateIndexModel<CampaignRunDocument>(
            Builders<CampaignRunDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.CampaignId).Descending(x => x.StartedAt)));
        _contacts.Indexes.CreateOne(new CreateIndexModel<CampaignContactExecutionDocument>(
            Builders<CampaignContactExecutionDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.RunId).Descending(x => x.CreatedAt)));
        _outcomes.Indexes.CreateOne(new CreateIndexModel<CampaignCallOutcomeDocument>(
            Builders<CampaignCallOutcomeDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.RunId).Descending(x => x.CreatedAt)));
        _outcomes.Indexes.CreateOne(new CreateIndexModel<CampaignCallOutcomeDocument>(
            Builders<CampaignCallOutcomeDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.ContactExecutionId)));
    }

    public async Task<IReadOnlyList<CampaignContract>> GetCampaignsAsync(string tenantId, CancellationToken ct = default)
        => (await _campaigns.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<CampaignContract?> GetCampaignAsync(string tenantId, string campaignId, CancellationToken ct = default)
    {
        var doc = await _campaigns.Find(x => x.TenantId == tenantId && x.Id == campaignId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<IReadOnlyList<CampaignContract>> GetDueCampaignsAsync(DateTimeOffset at, CancellationToken ct = default)
    {
        var docs = await _campaigns.Find(x =>
                x.Enabled &&
                x.NextRunAt.HasValue &&
                x.NextRunAt <= at &&
                (x.Status == CampaignStatus.Published || x.Status == CampaignStatus.Active))
            .ToListAsync(ct);
        return docs.Select(ToContract).ToList();
    }

    public async Task<CampaignContract> UpsertCampaignAsync(CampaignContract campaign, CancellationToken ct = default)
    {
        var doc = new CampaignDocument
        {
            Id = campaign.Id,
            TenantId = campaign.TenantId,
            Name = campaign.Name,
            Description = campaign.Description,
            Status = campaign.Status,
            CampaignType = campaign.CampaignType,
            ExecutionMode = campaign.ExecutionMode,
            TriggerType = campaign.TriggerType,
            ChannelAction = campaign.ChannelAction,
            Channel = campaign.Channel,
            Goal = campaign.Goal,
            PlaybookId = campaign.PlaybookId,
            WorkflowDefinitionId = campaign.WorkflowDefinitionId,
            AssistantId = campaign.AssistantId,
            RuntimeModelProfileId = campaign.RuntimeModelProfileId,
            TemplateId = campaign.TemplateId,
            MessageDraft = campaign.MessageDraft,
            CallScriptDraft = campaign.CallScriptDraft,
            PromptOrigin = campaign.PromptOrigin,
            ScheduleType = campaign.ScheduleType,
            ScheduleExpression = campaign.ScheduleExpression,
            Timezone = campaign.Timezone,
            StartAt = campaign.StartAt,
            EndAt = campaign.EndAt,
            ExecutionWindowJson = campaign.ExecutionWindowJson,
            ThrottleJson = campaign.ThrottleJson,
            SegmentId = campaign.SegmentId,
            AudienceFilterJson = campaign.AudienceFilterJson,
            DedupePolicyJson = campaign.DedupePolicyJson,
            SuccessPolicyJson = campaign.SuccessPolicyJson,
            FollowupPolicyJson = campaign.FollowupPolicyJson,
            ResultMappingJson = campaign.ResultMappingJson,
            Enabled = campaign.Enabled,
            LastRunAt = campaign.LastRunAt,
            NextRunAt = campaign.NextRunAt,
            CreatedAt = campaign.CreatedAt,
            UpdatedAt = campaign.UpdatedAt,
            UpdatedBy = campaign.UpdatedBy
        };

        await _campaigns.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToContract(doc);
    }

    public async Task<IReadOnlyList<CampaignSegmentContract>> GetSegmentsAsync(string tenantId, CancellationToken ct = default)
        => (await _segments.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<CampaignSegmentContract?> GetSegmentAsync(string tenantId, string segmentId, CancellationToken ct = default)
    {
        var doc = await _segments.Find(x => x.TenantId == tenantId && x.Id == segmentId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<CampaignSegmentContract> UpsertSegmentAsync(CampaignSegmentContract segment, CancellationToken ct = default)
    {
        var doc = new CampaignSegmentDocument
        {
            Id = segment.Id,
            TenantId = segment.TenantId,
            Name = segment.Name,
            Description = segment.Description,
            SourceModules = segment.SourceModules.ToList(),
            FilterJson = segment.FilterJson,
            EstimatedCount = segment.EstimatedCount,
            SamplePreviewJson = segment.SamplePreviewJson,
            CreatedAt = segment.CreatedAt,
            UpdatedAt = segment.UpdatedAt,
            UpdatedBy = segment.UpdatedBy
        };

        await _segments.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToContract(doc);
    }

    public async Task<IReadOnlyList<CampaignCallPlaybookContract>> GetPlaybooksAsync(string tenantId, CancellationToken ct = default)
        => (await _playbooks.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<CampaignCallPlaybookContract?> GetPlaybookAsync(string tenantId, string playbookId, CancellationToken ct = default)
    {
        var doc = await _playbooks.Find(x => x.TenantId == tenantId && x.Id == playbookId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<CampaignCallPlaybookContract> UpsertPlaybookAsync(CampaignCallPlaybookContract playbook, CancellationToken ct = default)
    {
        var doc = new CampaignCallPlaybookDocument
        {
            Id = playbook.Id,
            TenantId = playbook.TenantId,
            Name = playbook.Name,
            Description = playbook.Description,
            Purpose = playbook.Purpose,
            Channel = playbook.Channel,
            OpeningScript = playbook.OpeningScript,
            QuestionsJson = playbook.QuestionsJson,
            AnswerSchemaJson = playbook.AnswerSchemaJson,
            CompletionRulesJson = playbook.CompletionRulesJson,
            FallbackRulesJson = playbook.FallbackRulesJson,
            HandoffRulesJson = playbook.HandoffRulesJson,
            CreatedAt = playbook.CreatedAt,
            UpdatedAt = playbook.UpdatedAt,
            UpdatedBy = playbook.UpdatedBy
        };

        await _playbooks.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToContract(doc);
    }

    public async Task<IReadOnlyList<CampaignRunContract>> GetRunsAsync(string tenantId, string? campaignId = null, int limit = 100, CancellationToken ct = default)
    {
        var filter = Builders<CampaignRunDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(campaignId))
            filter = Builders<CampaignRunDocument>.Filter.And(filter, Builders<CampaignRunDocument>.Filter.Eq(x => x.CampaignId, campaignId));
        return (await _runs.Find(filter).SortByDescending(x => x.StartedAt).Limit(Math.Clamp(limit, 1, 500)).ToListAsync(ct)).Select(ToContract).ToList();
    }

    public async Task<CampaignRunContract?> GetRunAsync(string tenantId, string runId, CancellationToken ct = default)
    {
        var doc = await _runs.Find(x => x.TenantId == tenantId && x.Id == runId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<CampaignRunContract> CreateRunAsync(CampaignRunContract run, CancellationToken ct = default)
    {
        var doc = new CampaignRunDocument
        {
            Id = run.Id,
            TenantId = run.TenantId,
            CampaignId = run.CampaignId,
            Status = run.Status,
            TriggeredBy = run.TriggeredBy,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            AudienceSnapshotJson = run.AudienceSnapshotJson,
            CountersJson = run.CountersJson,
            ErrorSummaryJson = run.ErrorSummaryJson,
            RequestedBy = run.RequestedBy
        };

        await _runs.InsertOneAsync(doc, cancellationToken: ct);
        return ToContract(doc);
    }

    public async Task<CampaignRunContract?> UpdateRunAsync(CampaignRunContract run, CancellationToken ct = default)
    {
        var doc = new CampaignRunDocument
        {
            Id = run.Id,
            TenantId = run.TenantId,
            CampaignId = run.CampaignId,
            Status = run.Status,
            TriggeredBy = run.TriggeredBy,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            AudienceSnapshotJson = run.AudienceSnapshotJson,
            CountersJson = run.CountersJson,
            ErrorSummaryJson = run.ErrorSummaryJson,
            RequestedBy = run.RequestedBy
        };

        var result = await _runs.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, cancellationToken: ct);
        return result.MatchedCount == 0 ? null : ToContract(doc);
    }

    public async Task<IReadOnlyList<CampaignContactExecutionContract>> GetContactExecutionsAsync(string tenantId, string runId, CancellationToken ct = default)
        => (await _contacts.Find(x => x.TenantId == tenantId && x.RunId == runId).SortByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<CampaignContactExecutionContract> CreateContactExecutionAsync(CampaignContactExecutionContract contact, CancellationToken ct = default)
    {
        var doc = new CampaignContactExecutionDocument
        {
            Id = contact.Id,
            TenantId = contact.TenantId,
            CampaignId = contact.CampaignId,
            RunId = contact.RunId,
            PartyId = contact.PartyId,
            Channel = contact.Channel,
            Recipient = contact.Recipient,
            Status = contact.Status,
            SkipReason = contact.SkipReason,
            WorkflowExecutionId = contact.WorkflowExecutionId,
            ChannelMessageId = contact.ChannelMessageId,
            CallId = contact.CallId,
            CallOutcomeId = contact.CallOutcomeId,
            AssistantExecutionId = contact.AssistantExecutionId,
            CreatedAt = contact.CreatedAt,
            UpdatedAt = contact.UpdatedAt
        };
        await _contacts.InsertOneAsync(doc, cancellationToken: ct);
        return ToContract(doc);
    }

    public async Task<CampaignContactExecutionContract?> UpdateContactExecutionAsync(CampaignContactExecutionContract contact, CancellationToken ct = default)
    {
        var doc = new CampaignContactExecutionDocument
        {
            Id = contact.Id,
            TenantId = contact.TenantId,
            CampaignId = contact.CampaignId,
            RunId = contact.RunId,
            PartyId = contact.PartyId,
            Channel = contact.Channel,
            Recipient = contact.Recipient,
            Status = contact.Status,
            SkipReason = contact.SkipReason,
            WorkflowExecutionId = contact.WorkflowExecutionId,
            ChannelMessageId = contact.ChannelMessageId,
            CallId = contact.CallId,
            CallOutcomeId = contact.CallOutcomeId,
            AssistantExecutionId = contact.AssistantExecutionId,
            CreatedAt = contact.CreatedAt,
            UpdatedAt = contact.UpdatedAt
        };
        var result = await _contacts.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, cancellationToken: ct);
        return result.MatchedCount == 0 ? null : ToContract(doc);
    }

    public async Task<IReadOnlyList<CampaignCallOutcomeContract>> GetCallOutcomesByRunAsync(string tenantId, string runId, CancellationToken ct = default)
        => (await _outcomes.Find(x => x.TenantId == tenantId && x.RunId == runId).SortByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<CampaignCallOutcomeContract?> GetCallOutcomeByContactAsync(string tenantId, string contactExecutionId, CancellationToken ct = default)
    {
        var doc = await _outcomes.Find(x => x.TenantId == tenantId && x.ContactExecutionId == contactExecutionId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<CampaignCallOutcomeContract?> GetCallOutcomeAsync(string tenantId, string outcomeId, CancellationToken ct = default)
    {
        var doc = await _outcomes.Find(x => x.TenantId == tenantId && x.Id == outcomeId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<CampaignCallOutcomeContract> CreateCallOutcomeAsync(CampaignCallOutcomeContract outcome, CancellationToken ct = default)
    {
        var doc = new CampaignCallOutcomeDocument
        {
            Id = outcome.Id,
            TenantId = outcome.TenantId,
            CampaignId = outcome.CampaignId,
            RunId = outcome.RunId,
            ContactExecutionId = outcome.ContactExecutionId,
            PlaybookId = outcome.PlaybookId,
            CallId = outcome.CallId,
            Status = outcome.Status,
            StartedAt = outcome.StartedAt,
            EndedAt = outcome.EndedAt,
            TranscriptJson = outcome.TranscriptJson,
            AnswersJson = outcome.AnswersJson,
            Summary = outcome.Summary,
            Sentiment = outcome.Sentiment,
            NextAction = outcome.NextAction,
            LinkedPartyId = outcome.LinkedPartyId,
            LinkedSaleId = outcome.LinkedSaleId,
            LinkedInvoiceId = outcome.LinkedInvoiceId,
            CreatedAt = outcome.CreatedAt,
            UpdatedAt = outcome.UpdatedAt
        };

        await _outcomes.InsertOneAsync(doc, cancellationToken: ct);
        return ToContract(doc);
    }

    public async Task<CampaignCallOutcomeContract?> UpdateCallOutcomeAsync(CampaignCallOutcomeContract outcome, CancellationToken ct = default)
    {
        var doc = new CampaignCallOutcomeDocument
        {
            Id = outcome.Id,
            TenantId = outcome.TenantId,
            CampaignId = outcome.CampaignId,
            RunId = outcome.RunId,
            ContactExecutionId = outcome.ContactExecutionId,
            PlaybookId = outcome.PlaybookId,
            CallId = outcome.CallId,
            Status = outcome.Status,
            StartedAt = outcome.StartedAt,
            EndedAt = outcome.EndedAt,
            TranscriptJson = outcome.TranscriptJson,
            AnswersJson = outcome.AnswersJson,
            Summary = outcome.Summary,
            Sentiment = outcome.Sentiment,
            NextAction = outcome.NextAction,
            LinkedPartyId = outcome.LinkedPartyId,
            LinkedSaleId = outcome.LinkedSaleId,
            LinkedInvoiceId = outcome.LinkedInvoiceId,
            CreatedAt = outcome.CreatedAt,
            UpdatedAt = outcome.UpdatedAt
        };

        var result = await _outcomes.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, cancellationToken: ct);
        return result.MatchedCount == 0 ? null : ToContract(doc);
    }

    private static CampaignContract ToContract(CampaignDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        Description = doc.Description,
        Status = doc.Status,
        CampaignType = doc.CampaignType,
        ExecutionMode = doc.ExecutionMode,
        TriggerType = doc.TriggerType,
        ChannelAction = doc.ChannelAction,
        Channel = doc.Channel,
        Goal = doc.Goal,
        PlaybookId = doc.PlaybookId,
        WorkflowDefinitionId = doc.WorkflowDefinitionId,
        AssistantId = doc.AssistantId,
        RuntimeModelProfileId = doc.RuntimeModelProfileId,
        TemplateId = doc.TemplateId,
        MessageDraft = doc.MessageDraft,
        CallScriptDraft = doc.CallScriptDraft,
        PromptOrigin = doc.PromptOrigin,
        ScheduleType = doc.ScheduleType,
        ScheduleExpression = doc.ScheduleExpression,
        Timezone = doc.Timezone,
        StartAt = doc.StartAt,
        EndAt = doc.EndAt,
        ExecutionWindowJson = doc.ExecutionWindowJson,
        ThrottleJson = doc.ThrottleJson,
        SegmentId = doc.SegmentId,
        AudienceFilterJson = doc.AudienceFilterJson,
        DedupePolicyJson = doc.DedupePolicyJson,
        SuccessPolicyJson = doc.SuccessPolicyJson,
        FollowupPolicyJson = doc.FollowupPolicyJson,
        ResultMappingJson = doc.ResultMappingJson,
        Enabled = doc.Enabled,
        LastRunAt = doc.LastRunAt,
        NextRunAt = doc.NextRunAt,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static CampaignCallPlaybookContract ToContract(CampaignCallPlaybookDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        Description = doc.Description,
        Purpose = doc.Purpose,
        Channel = doc.Channel,
        OpeningScript = doc.OpeningScript,
        QuestionsJson = doc.QuestionsJson,
        AnswerSchemaJson = doc.AnswerSchemaJson,
        CompletionRulesJson = doc.CompletionRulesJson,
        FallbackRulesJson = doc.FallbackRulesJson,
        HandoffRulesJson = doc.HandoffRulesJson,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static CampaignSegmentContract ToContract(CampaignSegmentDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        Description = doc.Description,
        SourceModules = doc.SourceModules,
        FilterJson = doc.FilterJson,
        EstimatedCount = doc.EstimatedCount,
        SamplePreviewJson = doc.SamplePreviewJson,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static CampaignRunContract ToContract(CampaignRunDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        CampaignId = doc.CampaignId,
        Status = doc.Status,
        TriggeredBy = doc.TriggeredBy,
        StartedAt = doc.StartedAt,
        CompletedAt = doc.CompletedAt,
        AudienceSnapshotJson = doc.AudienceSnapshotJson,
        CountersJson = doc.CountersJson,
        ErrorSummaryJson = doc.ErrorSummaryJson,
        RequestedBy = doc.RequestedBy
    };

    private static CampaignContactExecutionContract ToContract(CampaignContactExecutionDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        CampaignId = doc.CampaignId,
        RunId = doc.RunId,
        PartyId = doc.PartyId,
        Channel = doc.Channel,
        Recipient = doc.Recipient,
        Status = doc.Status,
        SkipReason = doc.SkipReason,
        WorkflowExecutionId = doc.WorkflowExecutionId,
        ChannelMessageId = doc.ChannelMessageId,
        CallId = doc.CallId,
        CallOutcomeId = doc.CallOutcomeId,
        AssistantExecutionId = doc.AssistantExecutionId,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt
    };

    private static CampaignCallOutcomeContract ToContract(CampaignCallOutcomeDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        CampaignId = doc.CampaignId,
        RunId = doc.RunId,
        ContactExecutionId = doc.ContactExecutionId,
        PlaybookId = doc.PlaybookId,
        CallId = doc.CallId,
        Status = doc.Status,
        StartedAt = doc.StartedAt,
        EndedAt = doc.EndedAt,
        TranscriptJson = doc.TranscriptJson,
        AnswersJson = doc.AnswersJson,
        Summary = doc.Summary,
        Sentiment = doc.Sentiment,
        NextAction = doc.NextAction,
        LinkedPartyId = doc.LinkedPartyId,
        LinkedSaleId = doc.LinkedSaleId,
        LinkedInvoiceId = doc.LinkedInvoiceId,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt
    };

    private sealed class CampaignDocument
    {
        [BsonId, BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CampaignStatus Status { get; set; }
        public CampaignType CampaignType { get; set; }
        public CampaignExecutionMode ExecutionMode { get; set; }
        public CampaignTriggerType TriggerType { get; set; }
        public CampaignChannelAction ChannelAction { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string Goal { get; set; } = string.Empty;
        public string? PlaybookId { get; set; }
        public string? WorkflowDefinitionId { get; set; }
        public string? AssistantId { get; set; }
        public string? RuntimeModelProfileId { get; set; }
        public string? TemplateId { get; set; }
        public string? MessageDraft { get; set; }
        public string? CallScriptDraft { get; set; }
        public string? PromptOrigin { get; set; }
        public CampaignScheduleType ScheduleType { get; set; }
        public string ScheduleExpression { get; set; } = string.Empty;
        public string Timezone { get; set; } = "America/Managua";
        public DateTimeOffset StartAt { get; set; }
        public DateTimeOffset? EndAt { get; set; }
        public string? ExecutionWindowJson { get; set; }
        public string? ThrottleJson { get; set; }
        public string? SegmentId { get; set; }
        public string AudienceFilterJson { get; set; } = "{}";
        public string? DedupePolicyJson { get; set; }
        public string? SuccessPolicyJson { get; set; }
        public string? FollowupPolicyJson { get; set; }
        public string? ResultMappingJson { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTimeOffset? LastRunAt { get; set; }
        public DateTimeOffset? NextRunAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class CampaignCallPlaybookDocument
    {
        [BsonId, BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string Channel { get; set; } = "voice";
        public string OpeningScript { get; set; } = string.Empty;
        public string QuestionsJson { get; set; } = "[]";
        public string AnswerSchemaJson { get; set; } = "{}";
        public string? CompletionRulesJson { get; set; }
        public string? FallbackRulesJson { get; set; }
        public string? HandoffRulesJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class CampaignSegmentDocument
    {
        [BsonId, BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> SourceModules { get; set; } = [];
        public string FilterJson { get; set; } = "{}";
        public int? EstimatedCount { get; set; }
        public string? SamplePreviewJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class CampaignRunDocument
    {
        [BsonId, BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public CampaignRunStatus Status { get; set; }
        public CampaignRunTrigger TriggeredBy { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string AudienceSnapshotJson { get; set; } = "[]";
        public string CountersJson { get; set; } = "{}";
        public string? ErrorSummaryJson { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
    }

    private sealed class CampaignContactExecutionDocument
    {
        [BsonId, BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string? PartyId { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public CampaignContactExecutionStatus Status { get; set; }
        public string? SkipReason { get; set; }
        public string? WorkflowExecutionId { get; set; }
        public string? ChannelMessageId { get; set; }
        public string? CallId { get; set; }
        public string? CallOutcomeId { get; set; }
        public string? AssistantExecutionId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class CampaignCallOutcomeDocument
    {
        [BsonId, BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string ContactExecutionId { get; set; } = string.Empty;
        public string? PlaybookId { get; set; }
        public string? CallId { get; set; }
        public CampaignCallOutcomeStatus Status { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public string? TranscriptJson { get; set; }
        public string AnswersJson { get; set; } = "{}";
        public string Summary { get; set; } = string.Empty;
        public string Sentiment { get; set; } = string.Empty;
        public string NextAction { get; set; } = string.Empty;
        public string? LinkedPartyId { get; set; }
        public string? LinkedSaleId { get; set; }
        public string? LinkedInvoiceId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
