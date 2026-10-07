using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Domain.Devices;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>Retiring a device closes its stream at once (05 section 1.2); the agent does not retry.</summary>
public sealed class CloseRetiredSessions(IAgentSessionRegistry registry, TimeProvider clock) : IIntegrationEventHandler<DeviceRetiredV1>
{
    public async Task HandleAsync(DeviceRetiredV1 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (registry.Find(integrationEvent.DeviceId) is { } session && registry.Remove(session))
            await session.DisconnectAsync("DEVICE_RETIRED", "The device is retired.", 0, clock.GetUtcNow());
    }
}