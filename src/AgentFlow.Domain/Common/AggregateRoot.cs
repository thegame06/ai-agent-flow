namespace AgentFlow.Domain.Common;

/// <summary>
/// Domain event base. Published via MediatR after aggregate changes.
/// </summary>
public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
    public string TenantId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// Aggregates that produce domain events.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<DomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public abstract class AggregateRoot : TenantEntity, IHasDomainEvents
{
    private readonly List<DomainEvent> _domainEvents = [];

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot(string tenantId) : base(tenantId) { }
    protected AggregateRoot() { }

    protected void AddDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
