using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Security;

namespace AgentFlow.Core.Engine;

public interface IChannelExecutionRequestFactory
{
    Task<AgentExecutionRequest> CreateAsync(
        ChannelMessage incomingMessage,
        ChannelDefinition channel,
        ChannelSession? session,
        string agentKey,
        CancellationToken ct = default);
}

public sealed class ChannelExecutionRequestFactory : IChannelExecutionRequestFactory
{
    private readonly IIntentRoutingStore _intentRoutingStore;
    private readonly ITenantContextAccessor _tenantContext;

    public ChannelExecutionRequestFactory(
        IIntentRoutingStore intentRoutingStore,
        ITenantContextAccessor tenantContext)
    {
        _intentRoutingStore = intentRoutingStore;
        _tenantContext = tenantContext;
    }

    public async Task<AgentExecutionRequest> CreateAsync(
        ChannelMessage incomingMessage,
        ChannelDefinition channel,
        ChannelSession? session,
        string agentKey,
        CancellationToken ct = default)
    {
        var sessionContext = session != null ? new AgentSessionContext
        {
            SessionId = session.Id,
            UserIdentifier = session.Identifier,
            DisplayName = session.Metadata.GetValueOrDefault("display_name"),
            ChannelType = channel.Type.ToString(),
            ChannelId = channel.Id,
            IsWindowOpen = !session.IsExpired(),
            WindowHours = channel.SessionWindowHours,
            WindowExpiresAt = session.ExpiresAt
        } : null;

        var intentCatalogJson = await ResolveIntentCatalogJsonAsync(incomingMessage, channel, session, agentKey, ct);

        var ambientContext = _tenantContext.Current;
        var executionContext = ambientContext ?? new TenantContext
        {
            TenantId = incomingMessage.TenantId,
            UserId = incomingMessage.From,
            IsPlatformAdmin = false,
            Roles = ["developer"],
            Permissions = AgentFlowRoles.Developer.ToList()
        };

        if (ambientContext is null)
            _tenantContext.Set(executionContext);

        var requestMetadata = new Dictionary<string, string>
        {
            ["channelMessageId"] = incomingMessage.Id,
            ["permissions"] = string.Join(",", executionContext.Permissions),
            ["mcp.policy.allow_actions"] = "tools.execute",
            ["routing.intent_confidence_threshold"] = channel.Config.GetValueOrDefault("IntentConfidenceThreshold") ?? "0.70",
            ["routing.assistant_confidence_threshold"] = channel.Config.GetValueOrDefault("AssistantConfidenceThreshold") ?? "0.80",
            ["routing.no_match_action"] = channel.Config.GetValueOrDefault("NoMatchAction") ?? "human_review_only",
            ["routing.fallback_agent_id"] = channel.Config.GetValueOrDefault("RouterFallbackAgentId") ?? string.Empty,
            ["routing.fallback_max_clarification_turns"] = channel.Config.GetValueOrDefault("MaxClarificationTurns") ?? "2",
            ["routing.fallback_escalation_target"] = channel.Config.GetValueOrDefault("EscalationTarget") ?? string.Empty,
            ["routing.fallback_questions_json"] = channel.Config.GetValueOrDefault("FallbackQuestionsJson") ?? "[]"
        };

        if (session is not null)
        {
            requestMetadata["routing.fallback.state"] = session.Metadata.GetValueOrDefault("routing.fallback.state") ?? string.Empty;
            requestMetadata["routing.fallback.turn"] = session.Metadata.GetValueOrDefault("routing.fallback.turn") ?? "0";
        }

        return new AgentExecutionRequest
        {
            TenantId = incomingMessage.TenantId,
            AgentKey = agentKey,
            UserId = executionContext.UserId,
            UserMessage = incomingMessage.Content,
            ContextJson = JsonSerializer.Serialize(new
            {
                ChannelType = channel.Type.ToString(),
                ChannelId = channel.Id,
                SessionId = incomingMessage.SessionId,
                From = incomingMessage.From,
                IntentCatalog = intentCatalogJson
            }),
            CorrelationId = incomingMessage.SessionId,
            ThreadId = session?.ThreadId,
            Priority = ExecutionPriority.Normal,
            SessionContext = sessionContext,
            Metadata = requestMetadata
        };
    }

    private async Task<string?> ResolveIntentCatalogJsonAsync(
        ChannelMessage incomingMessage,
        ChannelDefinition channel,
        ChannelSession? session,
        string agentKey,
        CancellationToken ct)
    {
        if (channel.RouterAgentId != agentKey && session?.AgentId != channel.RouterAgentId)
            return null;

        var rules = await _intentRoutingStore.GetRulesByChannelAsync(
            incomingMessage.TenantId,
            channel.Type.ToString().ToLowerInvariant(),
            ct);

        if (rules is not { Count: > 0 })
            return null;

        return JsonSerializer.Serialize(rules.Select(r => new
        {
            intentKey = r.IntentKey,
            description = r.IntentDescription,
            examplePhrases = r.ExamplePhrases,
            targetAgentId = r.TargetAgentId,
            workflowId = r.WorkflowDefinitionId
        }));
    }
}

