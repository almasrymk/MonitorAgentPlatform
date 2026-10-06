using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Abstractions.Messaging;

/// <summary>Handles an event taken from the outbox. Each handler runs at most once per message (inbox).</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
