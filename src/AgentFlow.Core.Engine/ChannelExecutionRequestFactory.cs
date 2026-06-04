using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
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
    private readonly IChannelMessageRepository _messageRepo;

    public ChannelExecutionRequestFactory(
        IIntentRoutingStore intentRoutingStore,
        ITenantContextAccessor tenantContext,
        IChannelMessageRepository messageRepo)
    {
        _intentRoutingStore = intentRoutingStore;
        _tenantContext = tenantContext;
        _messageRepo = messageRepo;
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

        var isRouterAgent = IsRouterAgent(channel, session, agentKey);

        var requestMetadata = new Dictionary<string, string>
        {
            ["channelMessageId"] = incomingMessage.Id,
            ["permissions"] = string.Join(",", executionContext.Permissions),
            ["mcp.policy.allow_actions"] = "tools.execute",
            ["routing.is_router_agent"] = isRouterAgent ? "true" : "false",
            ["routing.intent_confidence_threshold"] = channel.Config.GetValueOrDefault("IntentConfidenceThreshold") ?? "0.70",
            ["routing.assistant_confidence_threshold"] = channel.Config.GetValueOrDefault("AssistantConfidenceThreshold") ?? "0.80",
            ["routing.assistant_inference_enabled"] = channel.Config.GetValueOrDefault("AssistantIntentInferenceEnabled") ?? "false",
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

        var historyWindow = ReadConfiguredInt(channel.Config, "HistoryWindowMessagesForClassification", 3, 1, 10);
        var minMessagesBeforeClassification = ReadConfiguredInt(channel.Config, "MinMessagesBeforeClassification", 3, 1, 10);
        var maxUnclassifiedMessagesBeforeEscalation = ReadConfiguredInt(channel.Config, "MaxUnclassifiedMessagesBeforeEscalation", 4, minMessagesBeforeClassification, 12);
        var shouldSuppressRepliesWhileAccumulating = ReadConfiguredBool(channel.Config, "SuppressRepliesWhileAccumulating", true);
        var routingStage = session?.Metadata.GetValueOrDefault("routing.guard.stage") ?? "accumulating";
        var inboundHistory = await LoadInboundHistoryAsync(incomingMessage, session, historyWindow, maxUnclassifiedMessagesBeforeEscalation, ct);
        var inboundWindowMessages = inboundHistory.TakeLast(historyWindow).ToList();
        var inboundMessageCount = inboundHistory.Count;
        var accumulationActive = isRouterAgent &&
            !string.Equals(routingStage, "classified", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(routingStage, "escalated_human", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(routingStage, "spam_review", StringComparison.OrdinalIgnoreCase);

        requestMetadata["routing.history_window_messages"] = historyWindow.ToString();
        requestMetadata["routing.min_messages_before_classification"] = minMessagesBeforeClassification.ToString();
        requestMetadata["routing.max_unclassified_messages_before_escalation"] = maxUnclassifiedMessagesBeforeEscalation.ToString();
        requestMetadata["routing.suppress_replies_while_accumulating"] = shouldSuppressRepliesWhileAccumulating ? "true" : "false";
        requestMetadata["routing.inbound_message_count"] = inboundMessageCount.ToString();
        requestMetadata["routing.accumulation_active"] = accumulationActive ? "true" : "false";
        requestMetadata["routing.guard.stage"] = routingStage;

        var aggregatedUserMessage = BuildRoutingUserMessage(inboundWindowMessages, incomingMessage.Content, accumulationActive, minMessagesBeforeClassification);
        requestMetadata["channel.latest_user_message"] = incomingMessage.Content;
        requestMetadata["routing.aggregated_user_message"] = aggregatedUserMessage;

        return new AgentExecutionRequest
        {
            TenantId = incomingMessage.TenantId,
            AgentKey = agentKey,
            UserId = executionContext.UserId,
            UserMessage = aggregatedUserMessage,
            ContextJson = JsonSerializer.Serialize(new
            {
                ChannelType = channel.Type.ToString(),
                ChannelId = channel.Id,
                SessionId = incomingMessage.SessionId,
                From = incomingMessage.From,
                IntentCatalog = intentCatalogJson,
                inboundContextWindow = inboundWindowMessages.Select(m => new
                {
                    messageId = m.Id,
                    content = m.Content,
                    createdAt = m.CreatedAt
                }),
                conversationState = new
                {
                    intent = (string?)null,
                    stage = "incoming_message",
                    slots = new Dictionary<string, string>(),
                    handoff = new
                    {
                        source = "channel_gateway",
                        target = agentKey,
                        reason = "initial_dispatch"
                    },
                    attachments = Array.Empty<object>(),
                    externalContextRefs = Array.Empty<string>()
                }
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
        if (!IsRouterAgent(channel, session, agentKey))
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

    private static bool IsRouterAgent(ChannelDefinition channel, ChannelSession? session, string agentKey)
    {
        if (string.IsNullOrWhiteSpace(agentKey))
            return false;

        if (string.Equals(agentKey, channel.RouterAgentId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(session?.AgentId, channel.RouterAgentId, StringComparison.OrdinalIgnoreCase))
            return true;

        return GetRoutingAgents(channel)
            .Contains(agentKey, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetRoutingAgents(ChannelDefinition channel)
    {
        var raw = channel.Config.GetValueOrDefault("IntentAgents")
            ?? channel.Config.GetValueOrDefault("RoutingAgents")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<List<ChannelMessage>> LoadInboundHistoryAsync(
        ChannelMessage incomingMessage,
        ChannelSession? session,
        int historyWindow,
        int maxUnclassifiedMessagesBeforeEscalation,
        CancellationToken ct)
    {
        if (session is null)
            return [incomingMessage];

        var history = await _messageRepo.GetBySessionAsync(
            session.Id,
            incomingMessage.TenantId,
            Math.Max(historyWindow + maxUnclassifiedMessagesBeforeEscalation + 4, 12),
            ct);
        return history
            .Where(x => x.Direction == MessageDirection.Incoming)
            .OrderBy(x => x.CreatedAt)
            .ToList();
    }

    private static string BuildRoutingUserMessage(
        IReadOnlyList<ChannelMessage> inboundWindowMessages,
        string latestMessage,
        bool accumulationActive,
        int minMessagesBeforeClassification)
    {
        if (!accumulationActive)
            return latestMessage;

        if (inboundWindowMessages.Count < minMessagesBeforeClassification)
            return latestMessage;

        var parts = inboundWindowMessages
            .Select(x => x.Content?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return parts.Count == 0 ? latestMessage : string.Join("\n", parts);
    }

    private static int ReadConfiguredInt(
        IReadOnlyDictionary<string, string> config,
        string key,
        int fallback,
        int min,
        int max)
    {
        return int.TryParse(config.GetValueOrDefault(key), out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static bool ReadConfiguredBool(
        IReadOnlyDictionary<string, string> config,
        string key,
        bool fallback)
    {
        if (!config.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;

        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }
}
