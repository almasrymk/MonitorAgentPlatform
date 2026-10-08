namespace MonitorCloud.Application.Notifications.Contracts;

/// <summary>Notifications other modules send (Monitoring: location-wide outage).</summary>
public interface INotificationPublisher
{
    /// <summary>One notification for a location whose devices all went offline (05 section 2). Current unit of work, no save.</summary>
    Task LocationOutageAsync(Guid tenantId, Guid locationId, int devices, DateTimeOffset at, CancellationToken ct);
}
