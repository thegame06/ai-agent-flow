using AgentFlow.Api.Controllers;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Tests.Integration.Channels;

public class ChannelStatusTests
{
    [Fact]
    public async Task GetStatus_ForWhatsAppQr_Returns_QrAvailableFalse_When_Handler_IsNotConcreteWhatsAppHandler()
    {
        // Arrange
        var tenantId = "tenant-1";
        var channel = ChannelDefinition.Create(
            tenantId,
            "wa-qr",
            ChannelType.WhatsApp,
            new Dictionary<string, string> { ["AuthMode"] = "qr" });

        var channelRepo = new InMemoryChannelDefinitionRepository(channel);
        var gateway = new FakeChannelGateway(new HealthyHandler(ChannelType.WhatsApp));

        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = tenantId,
            UserId = "u1",
            UserEmail = "u1@test.local",
            IsPlatformAdmin = false
        });

        var controller = new ChannelsController(channelRepo, gateway, tenantContext);

        // Act
        var result = await controller.GetStatus(tenantId, channel.Id, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = ok.Value!;

        var qrAvailable = (bool)payload.GetType().GetProperty("qrAvailable")!.GetValue(payload)!;
        var healthy = (bool)payload.GetType().GetProperty("Healthy")!.GetValue(payload)!;

        Assert.False(qrAvailable);
        Assert.True(healthy);
    }

    private sealed class HealthyHandler : IChannelHandler
    {
        public HealthyHandler(ChannelType type) => SupportedChannelType = type;
        public ChannelType SupportedChannelType { get; }

        public Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult(ChannelStatus.Active);

        public Task ShutdownAsync(ChannelDefinition definition, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ChannelMessage?> ProcessIncomingMessageAsync(object rawMessage, ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult<ChannelMessage?>(null);

        public Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult(SendResult.Ok("m1"));

        public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
            => ChannelContext.Create(definition.Type, definition.Id, "session-1", "user-1");

        public Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult(ChannelSession.Create(definition.TenantId, definition.Id, definition.Type, context.UserIdentifier));

        public Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult(HealthStatus.Ok("healthy"));
    }

    private sealed class FakeChannelGateway : IChannelGateway
    {
        private readonly IChannelHandler _handler;

        public FakeChannelGateway(IChannelHandler handler)
        {
            _handler = handler;
        }

        public void RegisterHandler(IChannelHandler handler) { }

        public IChannelHandler? GetHandler(ChannelType channelType)
            => _handler.SupportedChannelType == channelType ? _handler : null;

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
            => Task.FromResult<IReadOnlyList<ChannelDefinition>>(_channel.TenantId == tenantId ? new[] { _channel } : Array.Empty<ChannelDefinition>());

        public Task<IReadOnlyList<ChannelDefinition>> GetByTypeAsync(ChannelType type, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelDefinition>>(_channel.TenantId == tenantId && _channel.Type == type ? new[] { _channel } : Array.Empty<ChannelDefinition>());

        public Task<ChannelDefinition?> GetByNameAsync(string name, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelDefinition?>(_channel.Name == name && _channel.TenantId == tenantId ? _channel : null);

        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelDefinition channel, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelDefinition channel, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());

        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(AgentFlow.Abstractions.Result.Success());
    }
}
