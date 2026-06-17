namespace TaskFlow.Domain.Common;

/// <summary>
/// Base class for aggregate roots. Holds the identity and collects domain
/// events raised during a unit of work; the application layer drains and
/// publishes them via the Wolverine outbox after the aggregate is persisted.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier value type.</typeparam>
public abstract class AggregateRoot<TId>
    where TId : struct
{
    private readonly List<DomainEvent> _domainEvents = [];

    /// <summary>The aggregate's identity. Set on creation and never reassigned.</summary>
    public TId Id { get; protected set; }

    /// <summary>Domain events raised since the aggregate was loaded or last cleared.</summary>
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Records a domain event to be published after persistence.</summary>
    protected void AddDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Clears the recorded domain events once they have been published.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
