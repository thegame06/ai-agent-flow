using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Connect;

public enum TenantConnectionType
{
    Sql,
    NoSql,
    Rest,
    Sheets
}

public enum ConnectionHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}

public sealed record TenantConnectionContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public TenantConnectionType Type { get; init; }
    public string ConnectorId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Config { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> AllowedAgentIds { get; init; } = [];
    public IReadOnlyList<string> AllowedNodeIds { get; init; } = [];
    public IReadOnlyList<string> AllowedConnectorIds { get; init; } = [];
    public int SecretVersion { get; init; } = 1;
    public DateTimeOffset? SecretRotatedAt { get; init; }
    public DateTimeOffset? SecretExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record TenantConnectionSecretContract
{
    public string ConnectionId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string CipherText { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public DateTimeOffset RotatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string RotatedBy { get; init; } = string.Empty;
}

public sealed record ConnectionUsageAuditContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ConnectionId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
    public string ConnectorId { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public interface ITenantConnectionStore
{
    Task<IReadOnlyList<TenantConnectionContract>> GetConnectionsAsync(string tenantId, CancellationToken ct = default);
    Task<TenantConnectionContract?> GetConnectionAsync(string tenantId, string connectionId, CancellationToken ct = default);
    Task<TenantConnectionContract> UpsertConnectionAsync(TenantConnectionContract connection, CancellationToken ct = default);
    Task<TenantConnectionSecretContract?> GetSecretAsync(string tenantId, string connectionId, CancellationToken ct = default);
    Task<TenantConnectionSecretContract> UpsertSecretAsync(TenantConnectionSecretContract secret, CancellationToken ct = default);
    Task RecordUsageAsync(ConnectionUsageAuditContract entry, CancellationToken ct = default);
    Task<IReadOnlyList<ConnectionUsageAuditContract>> GetUsageAuditAsync(string tenantId, string connectionId, int limit, CancellationToken ct = default);
}

public sealed class MongoTenantConnectionStore : ITenantConnectionStore
{
    private readonly IMongoCollection<TenantConnectionDocument> _connections;
    private readonly IMongoCollection<TenantConnectionSecretDocument> _secrets;
    private readonly IMongoCollection<ConnectionUsageAuditDocument> _usage;

    public MongoTenantConnectionStore(IMongoDatabase database)
    {
        _connections = database.GetCollection<TenantConnectionDocument>("tenant_connections");
        _secrets = database.GetCollection<TenantConnectionSecretDocument>("tenant_connection_secrets");
        _usage = database.GetCollection<ConnectionUsageAuditDocument>("tenant_connection_usage");
    }

    public async Task<IReadOnlyList<TenantConnectionContract>> GetConnectionsAsync(string tenantId, CancellationToken ct = default)
    {
        var docs = await _connections.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct);
        return docs.Select(ToContract).ToList();
    }

    public async Task<TenantConnectionContract?> GetConnectionAsync(string tenantId, string connectionId, CancellationToken ct = default)
    {
        var doc = await _connections.Find(x => x.TenantId == tenantId && x.Id == connectionId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<TenantConnectionContract> UpsertConnectionAsync(TenantConnectionContract connection, CancellationToken ct = default)
    {
        var doc = new TenantConnectionDocument
        {
            Id = connection.Id,
            TenantId = connection.TenantId,
            Name = connection.Name,
            Type = connection.Type,
            ConnectorId = connection.ConnectorId,
            Config = connection.Config.ToDictionary(),
            AllowedAgentIds = connection.AllowedAgentIds.ToList(),
            AllowedNodeIds = connection.AllowedNodeIds.ToList(),
            AllowedConnectorIds = connection.AllowedConnectorIds.ToList(),
            SecretVersion = connection.SecretVersion,
            SecretRotatedAt = connection.SecretRotatedAt,
            SecretExpiresAt = connection.SecretExpiresAt,
            CreatedAt = connection.CreatedAt,
            UpdatedAt = connection.UpdatedAt,
            UpdatedBy = connection.UpdatedBy
        };

        await _connections.ReplaceOneAsync(
            x => x.TenantId == connection.TenantId && x.Id == connection.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return ToContract(doc);
    }

    public async Task<TenantConnectionSecretContract?> GetSecretAsync(string tenantId, string connectionId, CancellationToken ct = default)
    {
        var doc = await _secrets.Find(x => x.TenantId == tenantId && x.ConnectionId == connectionId)
            .SortByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<TenantConnectionSecretContract> UpsertSecretAsync(TenantConnectionSecretContract secret, CancellationToken ct = default)
    {
        var doc = new TenantConnectionSecretDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ConnectionId = secret.ConnectionId,
            TenantId = secret.TenantId,
            CipherText = secret.CipherText,
            Version = secret.Version,
            RotatedAt = secret.RotatedAt,
            ExpiresAt = secret.ExpiresAt,
            RotatedBy = secret.RotatedBy
        };

        await _secrets.InsertOneAsync(doc, cancellationToken: ct);
        return ToContract(doc);
    }

    public async Task RecordUsageAsync(ConnectionUsageAuditContract entry, CancellationToken ct = default)
    {
        var doc = new ConnectionUsageAuditDocument
        {
            Id = entry.Id,
            TenantId = entry.TenantId,
            ConnectionId = entry.ConnectionId,
            AgentId = entry.AgentId,
            NodeId = entry.NodeId,
            ConnectorId = entry.ConnectorId,
            Succeeded = entry.Succeeded,
            ErrorCode = entry.ErrorCode,
            ErrorMessage = entry.ErrorMessage,
            OccurredAt = entry.OccurredAt
        };

        await _usage.InsertOneAsync(doc, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ConnectionUsageAuditContract>> GetUsageAuditAsync(string tenantId, string connectionId, int limit, CancellationToken ct = default)
    {
        var bounded = Math.Clamp(limit, 1, 1000);
        var docs = await _usage.Find(x => x.TenantId == tenantId && x.ConnectionId == connectionId)
            .SortByDescending(x => x.OccurredAt)
            .Limit(bounded)
            .ToListAsync(ct);

        return docs.Select(ToContract).ToList();
    }

    private static TenantConnectionContract ToContract(TenantConnectionDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        Type = doc.Type,
        ConnectorId = doc.ConnectorId,
        Config = doc.Config,
        AllowedAgentIds = doc.AllowedAgentIds,
        AllowedNodeIds = doc.AllowedNodeIds,
        AllowedConnectorIds = doc.AllowedConnectorIds,
        SecretVersion = doc.SecretVersion,
        SecretRotatedAt = doc.SecretRotatedAt,
        SecretExpiresAt = doc.SecretExpiresAt,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static TenantConnectionSecretContract ToContract(TenantConnectionSecretDocument doc) => new()
    {
        ConnectionId = doc.ConnectionId,
        TenantId = doc.TenantId,
        CipherText = doc.CipherText,
        Version = doc.Version,
        RotatedAt = doc.RotatedAt,
        ExpiresAt = doc.ExpiresAt,
        RotatedBy = doc.RotatedBy
    };

    private static ConnectionUsageAuditContract ToContract(ConnectionUsageAuditDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        ConnectionId = doc.ConnectionId,
        AgentId = doc.AgentId,
        NodeId = doc.NodeId,
        ConnectorId = doc.ConnectorId,
        Succeeded = doc.Succeeded,
        ErrorCode = doc.ErrorCode,
        ErrorMessage = doc.ErrorMessage,
        OccurredAt = doc.OccurredAt
    };

    private sealed class TenantConnectionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public TenantConnectionType Type { get; set; }
        public string ConnectorId { get; set; } = string.Empty;
        public Dictionary<string, string> Config { get; set; } = [];
        public List<string> AllowedAgentIds { get; set; } = [];
        public List<string> AllowedNodeIds { get; set; } = [];
        public List<string> AllowedConnectorIds { get; set; } = [];
        public int SecretVersion { get; set; }
        public DateTimeOffset? SecretRotatedAt { get; set; }
        public DateTimeOffset? SecretExpiresAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class TenantConnectionSecretDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string CipherText { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset RotatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string RotatedBy { get; set; } = string.Empty;
    }

    private sealed class ConnectionUsageAuditDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string ConnectorId { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
    }
}
