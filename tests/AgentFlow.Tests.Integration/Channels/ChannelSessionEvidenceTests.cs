using AgentFlow.Api.Controllers;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Tests.Integration.Channels;

public class ChannelSessionEvidenceTests
{
    [Fact]
    public async Task GetMessages_Returns_Execution_And_Channel_Message_Ids()
    {
        // Arrange
        var tenantId = "tenant-1";
        var sessionId = "session-123";

        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = tenantId,
            UserId = "u1",
            UserEmail = "u1@test.local",
            IsPlatformAdmin = false
        });

        var message = ChannelMessage.CreateIncoming(
            tenantId: tenantId,
            channelId: "ch-1",
            sessionId: sessionId,
            from: "+50581143874",
            content: "hola");

        message.LinkExecution("exec-abc");
        message.Metadata["wa_message_id"] = "wamid.in.1";
        message.Metadata["wa_message_id_out"] = "wamid.out.1";

        var messageRepo = new InMemoryChannelMessageRepository(message);
        var services = new ServiceCollection()
            .AddSingleton<IChannelMessageRepository>(messageRepo)
            .BuildServiceProvider();

        var controller = new ChannelSessionsController(
            new NoopChannelSessionRepository(),
            new NoopChannelGateway(),
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };

        // Act
        var result = await controller.GetMessages(tenantId, sessionId, 50, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<IEnumerable<ChannelMessageDto>>(ok.Value);
        var dto = Assert.Single(payload);

        Assert.Equal("exec-abc", dto.AgentExecutionId);
        Assert.Equal("wamid.in.1", dto.ChannelMessageIdIn);
        Assert.Equal("wamid.out.1", dto.ChannelMessageIdOut);
    }

    private sealed class InMemoryChannelMessageRepository : IChannelMessageRepository
    {
        private readonly ChannelMessage _message;

        public InMemoryChannelMessageRepository(ChannelMessage message)
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

        public Task<IReadOnlyList<ChannelMessage>> GetByChannelAsync(string channelId, string tenantId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelMessage>>(Array.Empty<ChannelMessage>());

        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelMessage message, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelMessage message, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string messageId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
    }

    private sealed class NoopChannelSessionRepository : IChannelSessionRepository
    {
        public Task<ChannelSession?> GetByIdAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelSession?>(null);

        public Task<ChannelSession?> GetByChannelAndIdentifierAsync(string channelId, string identifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelSession?>(null);

        public Task<IReadOnlyList<ChannelSession>> GetActiveByChannelAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());

        public Task<IReadOnlyList<ChannelSession>> GetActiveByUserAsync(string userIdentifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());

        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelSession session, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelSession session, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(0);
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
}
