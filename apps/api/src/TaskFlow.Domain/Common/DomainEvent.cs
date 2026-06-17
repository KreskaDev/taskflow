namespace TaskFlow.Domain.Common;

/// <summary>
/// Marker base for domain events. Concrete events are plain records that
/// Wolverine publishes through the transactional outbox (see ADR-0008/R8).
/// </summary>
public abstract record DomainEvent;
