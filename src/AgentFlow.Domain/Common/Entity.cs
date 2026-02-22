using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AgentFlow.Domain.Common;

/// <summary>
/// Base entity for all domain objects with MongoDB support.
/// Uses string-based IDs to maintain cross-boundary portability.
/// </summary>
public abstract class Entity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; protected set; } = ObjectId.GenerateNewId().ToString();

    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; protected set; } = string.Empty;
    public string UpdatedBy { get; protected set; } = string.Empty;
    public bool IsDeleted { get; protected set; } = false;
    public long Version { get; protected set; } = 1;

    protected void MarkUpdated(string updatedBy)
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
        Version++;
    }

    protected void MarkDeleted(string deletedBy)
    {
        IsDeleted = true;
        MarkUpdated(deletedBy);
    }
}
