namespace AgentFlow.Tests.Unit.IntentRouting;

using AgentFlow.Intents.Ownership;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

/// <summary>
/// Tests unitarios para ConversationOwnershipManager - Happy Path.
/// Valida locks distribuidos con Redis.
/// </summary>
public sealed class ConversationOwnershipManagerTests
{
    private readonly Mock<IDatabase> _redis;
    private readonly Mock<IConnectionMultiplexer> _connectionMultiplexer;
    private readonly Mock<ILogger<ConversationOwnershipManager>> _logger;
    private readonly ConversationOwnershipManager _manager;

    public ConversationOwnershipManagerTests()
    {
        _redis = new Mock<IDatabase>();
        _connectionMultiplexer = new Mock<IConnectionMultiplexer>();
        _logger = new Mock<ILogger<ConversationOwnershipManager>>();

        _connectionMultiplexer.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_redis.Object);

        _manager = new ConversationOwnershipManager(
            _connectionMultiplexer.Object,
            _logger.Object);
    }

    [Fact]
    public async Task TryAcquireLock_NoExistingLock_Success()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-123";
        const string agentId = "agent-456";
        var ttl = TimeSpan.FromMinutes(10);

        // Redis SET NX retorna true (lock adquirido)
        _redis.Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.Is<TimeSpan?>(t => t == ttl),
                It.Is<When>(w => w == When.NotExists),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var acquired = await _manager.TryAcquireLockAsync(tenantId, conversationId, agentId, ttl, CancellationToken.None);

        // Assert
        Assert.True(acquired);

        _redis.Verify(x => x.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().Contains($"{tenantId}:{conversationId}")),
            It.Is<RedisValue>(v => v.ToString() == agentId),
            It.Is<TimeSpan?>(t => t == ttl),
            When.NotExists,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task TryAcquireLock_ExistingLock_Fails()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-123";
        const string agentId = "agent-456";
        var ttl = TimeSpan.FromMinutes(10);

        // Redis SET NX retorna false (lock ya existe)
        _redis.Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.Is<TimeSpan?>(t => t == ttl),
                It.Is<When>(w => w == When.NotExists),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var acquired = await _manager.TryAcquireLockAsync(tenantId, conversationId, agentId, ttl, CancellationToken.None);

        // Assert
        Assert.False(acquired);
    }

    [Fact]
    public async Task RenewLock_ExistingLock_Success()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-456";
        const string agentId = "agent-789";
        var newTtl = TimeSpan.FromMinutes(10);

        // Redis GET retorna el agentId correcto
        _redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(agentId));

        // Redis EXPIRE retorna true
        _redis.Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var renewed = await _manager.RenewLockAsync(tenantId, conversationId, agentId, newTtl, CancellationToken.None);

        // Assert
        Assert.True(renewed);

        _redis.Verify(x => x.KeyExpireAsync(
            It.Is<RedisKey>(k => k.ToString().Contains($"{tenantId}:{conversationId}")),
            It.Is<TimeSpan?>(t => t == newTtl),
            ExpireWhen.Always,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task RenewLock_WrongAgent_Fails()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-456";
        const string agentId = "agent-789";
        const string lockedByAgentId = "another-agent-123";
        var newTtl = TimeSpan.FromMinutes(10);

        // Redis GET retorna OTRO agentId (no el que intenta renovar)
        _redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(lockedByAgentId));

        // Act
        var renewed = await _manager.RenewLockAsync(tenantId, conversationId, agentId, newTtl, CancellationToken.None);

        // Assert
        Assert.False(renewed);

        // No debe llamar a EXPIRE si el agente no coincide
        _redis.Verify(x => x.KeyExpireAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task ReleaseLock_ExistingLock_Success()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-789";
        const string agentId = "agent-abc";

        // Redis GET retorna el agentId correcto
        _redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(agentId));

        // Redis DEL retorna true
        _redis.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var released = await _manager.ReleaseLockAsync(tenantId, conversationId, agentId, CancellationToken.None);

        // Assert
        Assert.True(released);

        _redis.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString().Contains($"{tenantId}:{conversationId}")),
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task ReleaseLock_WrongAgent_IgnoresAndReturnsTrue()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-789";
        const string agentId = "agent-abc";
        const string lockedByAgentId = "another-agent-def";

        // Redis GET retorna OTRO agentId
        _redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(lockedByAgentId));

        // Act
        var released = await _manager.ReleaseLockAsync(tenantId, conversationId, agentId, CancellationToken.None);

        // Assert
        Assert.True(released); // Idempotente: retorna true pero no borra

        // No debe llamar a DEL si el agente no coincide
        _redis.Verify(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task GetState_ExistingLock_ReturnsState()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-xyz";
        const string agentId = "agent-ghi";

        // Redis GET retorna el agentId
        _redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(agentId));

        // Redis TTL retorna 300 segundos
        _redis.Setup(x => x.KeyTimeToLiveAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromSeconds(300));

        // Act
        var state = await _manager.GetStateAsync(tenantId, conversationId, CancellationToken.None);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(conversationId, state.ConversationId);
        Assert.Equal(agentId, state.OwnerAgentId);
        Assert.True(state.IsActive);
        Assert.True(state.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetState_NoLock_ReturnsNull()
    {
        // Arrange
        const string tenantId = "tenant-1";
        const string conversationId = "conv-no-lock";

        // Redis GET retorna null (no existe lock)
        _redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var state = await _manager.GetStateAsync(tenantId, conversationId, CancellationToken.None);

        // Assert
        Assert.Null(state);
    }
}
