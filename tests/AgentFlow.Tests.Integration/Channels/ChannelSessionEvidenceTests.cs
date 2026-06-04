using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Controllers;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentFlow.Tests.Integration.Channels;

public class ChannelSessionEvidenceTests
{
    [Fact]
    public async Task GetMessages_Returns_Execution_And_Channel_Message_Ids()
    {
        var tenantId = "tenant-1";
        var sessionId = "session-123";

        var tenantContext = BuildTenantContext(tenantId);

        var message = ChannelMessage.CreateIncoming(
            tenantId: tenantId,
            channelId: "ch-1",
            sessionId: sessionId,
            from: "+50581143874",
            content: "hola");

        message.LinkExecution("exec-abc");
        message.Metadata["wa_message_id"] = "wamid.in.1";
        message.Metadata["wa_message_id_out"] = "wamid.out.1";

        var messageRepo = new InMemorySingleMessageRepository(message);
        var services = new ServiceCollection()
            .AddSingleton<IChannelMessageRepository>(messageRepo)
            .BuildServiceProvider();

        var controller = new ChannelSessionsController(
            new NullSessionRepository(),
            new InMemorySpamReputationRepository(),
            new NoopChannelGateway(),
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };

        var result = await controller.GetMessages(tenantId, sessionId, limit: 50, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<IEnumerable<ChannelMessageDto>>(ok.Value);
        var dto = Assert.Single(payload);

        Assert.Equal("exec-abc", dto.AgentExecutionId);
        Assert.Equal("wamid.in.1", dto.ChannelMessageIdIn);
        Assert.Equal("wamid.out.1", dto.ChannelMessageIdOut);
    }

    [Fact]
    public async Task UpdateSpamReputation_MarksSessionForSpamReview()
    {
        var tenantId = "tenant-1";
        var session = ChannelSession.Create(tenantId, "ch-1", ChannelType.WhatsApp, "+50581143874");
        var tenantContext = BuildTenantContext(tenantId);
        var sessionRepo = new InMemorySessionRepository(session);
        var spamRepo = new InMemorySpamReputationRepository();

        var controller = new ChannelSessionsController(
            sessionRepo,
            spamRepo,
            new NoopChannelGateway(),
            tenantContext);

        var result = await controller.UpdateSpamReputation(
            tenantId,
            session.Id,
            new UpdateSessionSpamReputationRequest
            {
                Status = "confirmed_spam",
                ReasonCode = "manual_review_confirmed"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<SessionSpamReputationDto>(ok.Value);

        Assert.Equal("confirmed_spam", payload.Status);
        Assert.Equal("manual_review_confirmed", payload.LastReasonCode);
        Assert.Equal("spam_review", session.Metadata["routing.fallback.state"]);
        Assert.Equal("spam_review", session.Metadata["routing.guard.stage"]);
    }

    [Fact]
    public async Task GetSession_ReturnsEmbeddedSpamReputationFields()
    {
        var tenantId = "tenant-1";
        var session = ChannelSession.Create(tenantId, "ch-1", ChannelType.WhatsApp, "+50581143874");
        session.Metadata["routing.guard.stage"] = "accumulating";
        session.Metadata["reply_pending"] = "true";
        var tenantContext = BuildTenantContext(tenantId);
        var sessionRepo = new InMemorySessionRepository(session);
        var spamRepo = new InMemorySpamReputationRepository();
        var reputation = ChannelSpamReputation.Create(tenantId, session.ChannelId, session.Identifier);
        reputation.MarkSuspected("heuristic_spam");
        await spamRepo.UpsertAsync(reputation, CancellationToken.None);

        var controller = new ChannelSessionsController(
            sessionRepo,
            spamRepo,
            new NoopChannelGateway(),
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider()
                }
            }
        };

        var result = await controller.GetById(tenantId, session.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ChannelSessionDto>(ok.Value);
        Assert.Equal("accumulating", dto.RoutingStage);
        Assert.Equal("spam_review", dto.OperationalState);
        Assert.True(dto.ReplyPending);
        Assert.True(dto.RequiresHumanReview);
        Assert.Equal("suspected", dto.SpamReputationStatus);
        Assert.Equal(1, dto.SpamSignalCount);
        Assert.Equal("heuristic_spam", dto.SpamLastReasonCode);
    }

    [Fact]
    public async Task GetActive_ReturnsOperationalStateForAccumulatingSession()
    {
        var tenantId = "tenant-1";
        var session = ChannelSession.Create(tenantId, "ch-1", ChannelType.WhatsApp, "+50581143874");
        session.Metadata["routing.guard.stage"] = "accumulating";
        session.Metadata["reply_pending"] = "true";

        var tenantContext = BuildTenantContext(tenantId);
        var sessionRepo = new InMemorySessionRepository(session);
        var spamRepo = new InMemorySpamReputationRepository();
        var controller = new ChannelSessionsController(
            sessionRepo,
            spamRepo,
            new NoopChannelGateway(),
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider()
                }
            }
        };

        var result = await controller.GetActive(tenantId, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<PagedResponse<ChannelSessionDto>>(ok.Value);
        var dto = Assert.Single(payload.Items);
        Assert.Equal("awaiting_classification", dto.OperationalState);
        Assert.Equal("accumulating", dto.RoutingStage);
        Assert.Null(dto.RoutingFallbackState);
        Assert.False(dto.RequiresHumanReview);
    }

    [Fact]
    public async Task GetActive_FiltersByOperationalState()
    {
        var tenantId = "tenant-1";
        var accumulating = ChannelSession.Create(tenantId, "ch-1", ChannelType.WhatsApp, "+50581143874");
        accumulating.Metadata["routing.guard.stage"] = "accumulating";

        var spamReview = ChannelSession.Create(tenantId, "ch-1", ChannelType.WhatsApp, "+50580000000");
        spamReview.Metadata["routing.guard.stage"] = "spam_review";
        spamReview.Metadata["routing.fallback.state"] = "spam_review";
        spamReview.Metadata["requires_human_review"] = "true";

        var tenantContext = BuildTenantContext(tenantId);
        var sessionRepo = new InMemorySessionRepository(accumulating, spamReview);
        var controller = new ChannelSessionsController(
            sessionRepo,
            new InMemorySpamReputationRepository(),
            new NoopChannelGateway(),
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider()
                }
            }
        };

        var result = await controller.GetActive(tenantId, operationalState: "spam_review", ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<PagedResponse<ChannelSessionDto>>(ok.Value);
        var dto = Assert.Single(payload.Items);
        Assert.Equal(spamReview.Id, dto.Id);
        Assert.Equal("spam_review", dto.OperationalState);
        Assert.True(dto.RequiresHumanReview);
    }

    [Fact]
    public async Task GenericWebhook_StoresInboxMessage_WhenGatewaySuppressesReply()
    {
        var tenantId = "tenant-1";
        var channel = ChannelDefinition.Create(tenantId, "api", ChannelType.Api);
        channel.Activate();
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DefaultAgentId"] = "router-agent"
        });

        var sessionRepo = new InMemorySessionRepository();
        var channelRepo = new InMemoryChannelDefinitionRepository(channel);
        var connectStore = new InMemoryConnectStore();
        var messageRepo = new InMemoryWebhookMessageRepository();
        var gateway = new SuppressingGateway();

        var controller = new GenericWebhooksController(
            connectStore,
            gateway,
            channelRepo,
            sessionRepo,
            messageRepo,
            new NoopWorkflowAuditService(),
            NullLogger<GenericWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var payload = new Dictionary<string, object?>
        {
            ["recipient"] = "+50581143874",
            ["message"] = "hola"
        };

        var result = await controller.Receive(tenantId, "api", payload, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Single(connectStore.InboxMessages);
        Assert.Equal(ConnectOperationalStatus.Queued, connectStore.InboxMessages[0].Status);

        var savedSession = Assert.Single(sessionRepo.Items);
        Assert.Equal("hola", savedSession.Metadata["last_customer_message"]);

        var responseJson = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"status\":\"accepted\"", responseJson);
    }

    private static TenantContextAccessor BuildTenantContext(string tenantId)
    {
        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = tenantId,
            UserId = "u1",
            UserEmail = "u1@test.local",
            IsPlatformAdmin = false
        });
        return tenantContext;
    }

    private sealed class InMemorySingleMessageRepository : IChannelMessageRepository
    {
        private readonly ChannelMessage _message;

        public InMemorySingleMessageRepository(ChannelMessage message)
        {
            _message = message;
        }

        public Task<ChannelMessage?> GetByIdAsync(string messageId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelMessage?>(_message.Id == messageId && _message.TenantId == tenantId ? _message : null);

        public Task<IReadOnlyList<ChannelMessage>> GetBySessionAsync(string sessionId, string tenantId, int limit = 50, CancellationToken ct = default)
        {
            IReadOnlyList<ChannelMessage> list = _message.SessionId == sessionId && _message.TenantId == tenantId
                ? new[] { _message }
                : Array.Empty<ChannelMessage>();
            return Task.FromResult(list);
        }

        public Task<(IReadOnlyList<ChannelMessage> Items, long Total)> GetBySessionPagedAsync(string sessionId, string tenantId, int page = 0, int pageSize = 50, CancellationToken ct = default)
        {
            IReadOnlyList<ChannelMessage> list = _message.SessionId == sessionId && _message.TenantId == tenantId
                ? new[] { _message }
                : Array.Empty<ChannelMessage>();
            return Task.FromResult((list, (long)list.Count));
        }

        public Task<IReadOnlyList<ChannelMessage>> GetByChannelAsync(string channelId, string tenantId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelMessage>>(Array.Empty<ChannelMessage>());

        public Task<ChannelMessage?> GetByExternalMessageIdAsync(string tenantId, string channelId, string externalMessageId, MessageDirection direction, CancellationToken ct = default)
            => Task.FromResult<ChannelMessage?>(null);

        public Task<ChannelMessage?> GetLatestOutgoingByExecutionIdAsync(string tenantId, string executionId, CancellationToken ct = default)
            => Task.FromResult<ChannelMessage?>(null);

        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelMessage message, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelMessage message, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string messageId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
    }

    private sealed class NullSessionRepository : IChannelSessionRepository
    {
        public Task<ChannelSession?> GetByIdAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelSession?>(null);
        public Task<ChannelSession?> GetByThreadIdAsync(string threadId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelSession?>(null);
        public Task<ChannelSession?> GetByChannelAndIdentifierAsync(string channelId, string identifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelSession?>(null);
        public Task<IReadOnlyList<ChannelSession>> GetActiveByChannelAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());
        public Task<IReadOnlyList<ChannelSession>> GetActiveByUserAsync(string userIdentifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());
        public Task<(IReadOnlyList<ChannelSession> Items, long Total)> SearchAsync(string tenantId, string? channelId = null, string? status = null, string? operationalState = null, string? query = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
            => Task.FromResult(((IReadOnlyList<ChannelSession>)Array.Empty<ChannelSession>(), 0L));
        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelSession session, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelSession session, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
        public Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class InMemorySessionRepository : IChannelSessionRepository
    {
        private readonly Dictionary<string, ChannelSession> _items = new(StringComparer.OrdinalIgnoreCase);

        public InMemorySessionRepository(params ChannelSession[] sessions)
        {
            foreach (var session in sessions)
                _items[session.Id] = session;
        }

        public IReadOnlyCollection<ChannelSession> Items => _items.Values;

        public Task<ChannelSession?> GetByIdAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(_items.TryGetValue(sessionId, out var session) && session.TenantId == tenantId ? session : null);

        public Task<ChannelSession?> GetByThreadIdAsync(string threadId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(_items.Values.FirstOrDefault(x => x.ThreadId == threadId && x.TenantId == tenantId));

        public Task<ChannelSession?> GetByChannelAndIdentifierAsync(string channelId, string identifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult(_items.Values.FirstOrDefault(x => x.ChannelId == channelId && x.Identifier == identifier && x.TenantId == tenantId));

        public Task<IReadOnlyList<ChannelSession>> GetActiveByChannelAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(_items.Values.Where(x => x.ChannelId == channelId && x.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<ChannelSession>> GetActiveByUserAsync(string userIdentifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(_items.Values.Where(x => x.Identifier == userIdentifier && x.TenantId == tenantId).ToList());

        public Task<(IReadOnlyList<ChannelSession> Items, long Total)> SearchAsync(string tenantId, string? channelId = null, string? status = null, string? operationalState = null, string? query = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
        {
            IEnumerable<ChannelSession> items = _items.Values.Where(x => x.TenantId == tenantId);
            if (!string.IsNullOrWhiteSpace(channelId))
                items = items.Where(x => x.ChannelId == channelId);
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<SessionStatus>(status, true, out var parsedStatus))
                items = items.Where(x => x.Status == parsedStatus);
            if (!string.IsNullOrWhiteSpace(operationalState))
            {
                var normalized = operationalState.Trim().ToLowerInvariant();
                items = items.Where(x => MatchesOperationalState(x, normalized));
            }
            if (!string.IsNullOrWhiteSpace(query))
                items = items.Where(x => x.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase));

            var materialized = items.ToList();
            return Task.FromResult(((IReadOnlyList<ChannelSession>)materialized, (long)materialized.Count));
        }

        private static bool MatchesOperationalState(ChannelSession session, string operationalState)
        {
            var guardStage = session.Metadata.GetValueOrDefault("routing.guard.stage") ?? "classified";
            var fallbackState = session.Metadata.GetValueOrDefault("routing.fallback.state");
            var requiresHumanReview = string.Equals(session.Metadata.GetValueOrDefault("requires_human_review"), "true", StringComparison.OrdinalIgnoreCase);

            return operationalState switch
            {
                "awaiting_classification" =>
                    string.Equals(guardStage, "accumulating", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fallbackState, "spam_review", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fallbackState, "escalated_human", StringComparison.OrdinalIgnoreCase) &&
                    !requiresHumanReview,
                "spam_review" => string.Equals(fallbackState, "spam_review", StringComparison.OrdinalIgnoreCase),
                "escalated_human" => string.Equals(fallbackState, "escalated_human", StringComparison.OrdinalIgnoreCase),
                "pending_human_review" =>
                    requiresHumanReview &&
                    !string.Equals(fallbackState, "spam_review", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fallbackState, "escalated_human", StringComparison.OrdinalIgnoreCase),
                "classified" =>
                    !string.Equals(guardStage, "accumulating", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fallbackState, "spam_review", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fallbackState, "escalated_human", StringComparison.OrdinalIgnoreCase) &&
                    !requiresHumanReview,
                _ => true
            };
        }

        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelSession session, CancellationToken ct = default)
        {
            _items[session.Id] = session;
            return Task.FromResult(AgentFlow.Abstractions.Result.Success());
        }

        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelSession session, CancellationToken ct = default)
        {
            _items[session.Id] = session;
            return Task.FromResult(AgentFlow.Abstractions.Result.Success());
        }

        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string sessionId, string tenantId, CancellationToken ct = default)
        {
            _items.Remove(sessionId);
            return Task.FromResult(AgentFlow.Abstractions.Result.Success());
        }

        public Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_items.Values.Count(x => x.TenantId == tenantId));
    }

    private sealed class InMemorySpamReputationRepository : IChannelSpamReputationRepository
    {
        private readonly Dictionary<string, ChannelSpamReputation> _items = new(StringComparer.OrdinalIgnoreCase);

        public Task<ChannelSpamReputation?> GetAsync(string tenantId, string channelId, string identifier, CancellationToken ct = default)
            => Task.FromResult(_items.TryGetValue($"{tenantId}:{channelId}:{identifier}", out var value) ? value : null);

        public Task<AgentFlow.Abstractions.Result> UpsertAsync(ChannelSpamReputation reputation, CancellationToken ct = default)
        {
            _items[$"{reputation.TenantId}:{reputation.ChannelId}:{reputation.Identifier}"] = reputation;
            return Task.FromResult(AgentFlow.Abstractions.Result.Success());
        }
    }

    private sealed class InMemoryChannelDefinitionRepository : IChannelDefinitionRepository
    {
        private readonly ChannelDefinition _channel;

        public InMemoryChannelDefinitionRepository(ChannelDefinition channel)
        {
            _channel = channel;
        }

        public Task<ChannelDefinition?> GetByIdAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelDefinition?>(_channel.Id == channelId && _channel.TenantId == tenantId ? _channel : null);

        public Task<IReadOnlyList<ChannelDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelDefinition>>([_channel]);

        public Task<IReadOnlyList<ChannelDefinition>> GetByTypeAsync(ChannelType type, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelDefinition>>(_channel.Type == type && _channel.TenantId == tenantId ? [_channel] : []);

        public Task<ChannelDefinition?> GetByNameAsync(string name, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelDefinition?>(null);

        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelDefinition channel, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelDefinition channel, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
    }

    private sealed class InMemoryConnectStore : IConnectStore
    {
        public List<ConnectInboxMessageContract> InboxMessages { get; } = new();

        public Task<IReadOnlyList<ConnectTemplateContract>> GetTemplatesAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConnectTemplateContract>>(Array.Empty<ConnectTemplateContract>());
        public Task<ConnectTemplateContract?> GetTemplateAsync(string tenantId, string templateId, CancellationToken ct = default)
            => Task.FromResult<ConnectTemplateContract?>(null);
        public Task<ConnectTemplateContract> UpsertTemplateAsync(ConnectTemplateContract template, CancellationToken ct = default)
            => Task.FromResult(template);
        public Task<IReadOnlyList<ConnectCampaignContract>> GetCampaignsAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConnectCampaignContract>>(Array.Empty<ConnectCampaignContract>());
        public Task<ConnectCampaignContract> UpsertCampaignAsync(ConnectCampaignContract campaign, CancellationToken ct = default)
            => Task.FromResult(campaign);
        public Task<IReadOnlyList<ConnectInboxMessageContract>> GetInboxAsync(string tenantId, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConnectInboxMessageContract>>(InboxMessages.Where(x => x.TenantId == tenantId).ToList());
        public Task<ConnectInboxMessageContract?> GetInboxMessageByExternalKeyAsync(string tenantId, string externalEventKey, CancellationToken ct = default)
            => Task.FromResult(InboxMessages.FirstOrDefault(x => x.TenantId == tenantId && x.ExternalEventKey == externalEventKey));
        public Task<ConnectInboxMessageContract> CreateInboxMessageAsync(ConnectInboxMessageContract message, CancellationToken ct = default)
        {
            InboxMessages.Add(message);
            return Task.FromResult(message);
        }
        public Task<ConnectInboxMessageContract?> UpdateMessageStatusAsync(string tenantId, string messageId, ConnectOperationalStatus status, string updatedBy, string? lastError, CancellationToken ct = default)
            => Task.FromResult<ConnectInboxMessageContract?>(null);
    }

    private sealed class InMemoryWebhookMessageRepository : IChannelMessageRepository
    {
        public List<ChannelMessage> Items { get; } = new();

        public Task<ChannelMessage?> GetByIdAsync(string messageId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == messageId && x.TenantId == tenantId));

        public Task<ChannelMessage?> GetByExternalMessageIdAsync(string tenantId, string channelId, string externalMessageId, MessageDirection direction, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.TenantId == tenantId && x.ChannelId == channelId && x.ExternalMessageId == externalMessageId && x.Direction == direction));

        public Task<IReadOnlyList<ChannelMessage>> GetBySessionAsync(string sessionId, string tenantId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelMessage>>(Items.Where(x => x.SessionId == sessionId && x.TenantId == tenantId).ToList());

        public Task<(IReadOnlyList<ChannelMessage> Items, long Total)> GetBySessionPagedAsync(string sessionId, string tenantId, int page = 0, int pageSize = 50, CancellationToken ct = default)
        {
            var items = Items.Where(x => x.SessionId == sessionId && x.TenantId == tenantId).ToList();
            return Task.FromResult(((IReadOnlyList<ChannelMessage>)items, (long)items.Count));
        }

        public Task<IReadOnlyList<ChannelMessage>> GetByChannelAsync(string channelId, string tenantId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelMessage>>(Items.Where(x => x.ChannelId == channelId && x.TenantId == tenantId).ToList());

        public Task<ChannelMessage?> GetLatestOutgoingByExecutionIdAsync(string tenantId, string executionId, CancellationToken ct = default)
            => Task.FromResult(Items.LastOrDefault(x => x.TenantId == tenantId && x.AgentExecutionId == executionId && x.Direction == MessageDirection.Outgoing));

        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelMessage message, CancellationToken ct = default)
        {
            Items.Add(message);
            return Task.FromResult(AgentFlow.Abstractions.Result.Success());
        }

        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelMessage message, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string messageId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
    }

    private sealed class NoopChannelGateway : IChannelGateway
    {
        public void RegisterHandler(IChannelHandler handler) { }
        public IChannelHandler? GetHandler(ChannelType channelType) => null;
        public Task<ChannelMessage> ProcessMessageAsync(ChannelMessage incomingMessage, CancellationToken ct = default)
            => Task.FromResult(incomingMessage);
        public Task<SendResult> SendMessageAsync(string channelId, ChannelMessage message, CancellationToken ct = default)
            => Task.FromResult(SendResult.Ok("m1"));
        public Task<IReadOnlyList<ChannelSession>> GetActiveSessionsAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());
        public Task CloseSessionAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<BroadcastResult> BroadcastAsync(string channelId, string tenantId, string content, CancellationToken ct = default)
            => Task.FromResult(BroadcastResult.Ok(0));
    }

    private sealed class SuppressingGateway : IChannelGateway
    {
        public void RegisterHandler(IChannelHandler handler) { }
        public IChannelHandler? GetHandler(ChannelType channelType) => null;
        public Task<ChannelMessage> ProcessMessageAsync(ChannelMessage incomingMessage, CancellationToken ct = default)
        {
            incomingMessage.Metadata["agentflow.delivery"] = "suppressed";
            incomingMessage.Metadata["agentflow.visibility"] = "inbox_only";
            return Task.FromResult(incomingMessage);
        }
        public Task<SendResult> SendMessageAsync(string channelId, ChannelMessage message, CancellationToken ct = default)
            => Task.FromResult(SendResult.Ok("m1"));
        public Task<IReadOnlyList<ChannelSession>> GetActiveSessionsAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());
        public Task CloseSessionAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<BroadcastResult> BroadcastAsync(string channelId, string tenantId, string content, CancellationToken ct = default)
            => Task.FromResult(BroadcastResult.Ok(0));
    }

    private sealed class NoopWorkflowAuditService : IWorkflowAuditService
    {
        public Task RecordStudioActionAsync(string tenantId, string actor, string action, string workflowId, object details, string? correlationId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordExecutionActionAsync(string tenantId, string actor, string action, string executionId, string workflowId, object details, string? correlationId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
