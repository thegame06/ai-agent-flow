namespace AgentFlow.Tests.Unit.IntentRouting;

using AgentFlow.Abstractions;
using AgentFlow.Intents.Ownership;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

public sealed class ConversationOwnershipManagerTests
{
    private readonly Mock<IDatabase> _redis = new();
    private readonly Mock<IConnectionMultiplexer> _connectionMultiplexer = new();
    private readonly Mock<IDistributedLockService> _lockService = new();
    private readonly Mock<ILogger<ConversationOwnershipManager>> _logger = new();

    [Fact]
    public async Task TryAcquireLock_Available_ReturnsLock()
    {
        _connectionMultiplexer.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_redis.Object);
        _lockService.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopHandle());

        var manager = new ConversationOwnershipManager(_lockService.Object, _connectionMultiplexer.Object, _logger.Object);
        var lockValue = await manager.TryAcquireLockAsync("tenant-1", "conv-1", "agent-1", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.NotNull(lockValue);
        Assert.Equal("conv-1", lockValue.ConversationId);
        Assert.Equal("agent-1", lockValue.OwnerAgentId);
    }

    [Fact]
    public async Task TryAcquireLock_Unavailable_ReturnsNull()
    {
        _connectionMultiplexer.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_redis.Object);
        _lockService.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        var manager = new ConversationOwnershipManager(_lockService.Object, _connectionMultiplexer.Object, _logger.Object);
        var lockValue = await manager.TryAcquireLockAsync("tenant-1", "conv-1", "agent-1", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Null(lockValue);
    }

    [Fact]
    public async Task GetState_NoMetadata_ReturnsUnlockedState()
    {
        _connectionMultiplexer.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_redis.Object);
        _lockService.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);
        _redis.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(false);

        var manager = new ConversationOwnershipManager(_lockService.Object, _connectionMultiplexer.Object, _logger.Object);
        var state = await manager.GetStateAsync("tenant-1", "conv-1", CancellationToken.None);

        Assert.Equal("conv-1", state.ConversationId);
        Assert.False(state.IsLocked);
        Assert.Null(state.CurrentOwnerAgentId);
    }

    private sealed class NoopHandle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
