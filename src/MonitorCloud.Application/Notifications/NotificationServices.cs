using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Email;
using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Notifications.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.Application.Abstractions.Context;

namespace MonitorCloud.Application.Notifications;

/// <summary>Creates the in-app notification and the e-mail deliveries of a tenant event (02 section 7).</summary>
public sealed class NotificationFanOut(IAppDbContext db, IUnitOfWork unitOfWork, IEntitlementReader entitlements, ILiveNotifier live, TimeProvider clock)
{
    public async Task<Notification?> SendAsync(
        Guid tenantId, byte severity, string category, string title, string body, Guid? locationId, Guid? deviceId, Guid? alertId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var settings = await db.Set<AlertChannelSettings>().AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, ct) ?? AlertChannelSettings.Default(tenantId, now);
        Notification? notification = null;
        if (settings.InAppEnabled)
        {
            notification = Notification.Create(tenantId, SeverityName(severity), category, title, body, locationId, deviceId, alertId, now);
            db.Set<Notification>().Add(notification);
        }

        var plan = await entitlements.GetAsync(tenantId, ct);
        if (settings.WebhookEnabled && settings.WebhookUrl is { } url && plan.Has(Features.NotificationsWebhook))
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(
                new { type = "alert", tenantId, alertId, severity = SeverityName(severity), category, title, body, locationId, deviceId, at = now }, WebhookPayloads.Json);
            db.Set<NotificationDelivery>().Add(NotificationDelivery.Webhook(tenantId, alertId, url, title, payload, now));
        }

        if (settings.EmailEnabled && plan.Has(Features.NotificationsEmail))
        {
            var recipients = await db.Set<AlertRecipient>().AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
            foreach (var recipient in recipients.Where(r => r.Wants(severity, locationId ?? Guid.Empty)))
                db.Set<NotificationDelivery>().Add(NotificationDelivery.Email(tenantId, alertId, recipient.Email, $"[{SeverityName(severity)}] {title}", $"{title}\n\n{body}", now));
        }

        // Saved before the push, so the bell that refetches on notificationCreated counts the new row.
        await unitOfWork.SaveChangesAsync(ct);
        if (notification is not null)
            await live.NotificationCreatedAsync(new NotificationChange(notification.Id, tenantId, notification.Severity, notification.Title, notification.LocationId), ct);
        return notification;
    }

    public static byte SeverityLevel(string severity) => severity switch
    {
        "Critical" => 3,
        "Warning" => 2,
        _ => 1,
    };

    public static string SeverityName(byte severity) => severity switch
    {
        3 => "Critical",
        2 => "Warning",
        _ => "Info",
    };
}

/// <summary>A new alert notifies once (touches do not raise the event); backlog alerts do not notify (rule 7).</summary>
internal sealed class NotifyOnAlertRaised(NotificationFanOut fanOut, IDeviceNames devices, ILocationLookup locations) : IIntegrationEventHandler<AlertRaisedV1>
{
    public async Task HandleAsync(AlertRaisedV1 integrationEvent, CancellationToken cancellationToken)
    {
        if (!integrationEvent.Notify)
            return;
        var device = (await devices.GetAsync([integrationEvent.DeviceId], cancellationToken)).GetValueOrDefault(integrationEvent.DeviceId) ?? "A device";
        var location = (await locations.GetAsync([integrationEvent.LocationId], cancellationToken)).GetValueOrDefault(integrationEvent.LocationId)?.Name;
        var body = location is null ? device : $"{device} - {location}";
        await fanOut.SendAsync(integrationEvent.TenantId, NotificationFanOut.SeverityLevel(integrationEvent.Severity), integrationEvent.Category, integrationEvent.Title, body,
            integrationEvent.LocationId, integrationEvent.DeviceId, integrationEvent.AlertId, cancellationToken);
    }
}

/// <summary>A possible cloned device is also reported to the platform feed (05 section 2, rule 3).</summary>
internal sealed class NotifyPlatformOnCloneSuspected(IAppDbContext db, IUnitOfWork unitOfWork, ITenantScopeSetter scope, ITenantNames tenants, ILiveNotifier live, TimeProvider clock)
    : IIntegrationEventHandler<DeviceCloneSuspectedV1>
{
    public async Task HandleAsync(DeviceCloneSuspectedV1 integrationEvent, CancellationToken cancellationToken)
    {
        var name = (await tenants.GetAsync([integrationEvent.TenantId], cancellationToken)).GetValueOrDefault(integrationEvent.TenantId) ?? "A customer";
        scope.RunAsSystem();
        var notification = Notification.Create(null, "Warning", "System", "Possible cloned device",
            $"{name}: {integrationEvent.Replacements} connections of one device replaced each other within 5 minutes.", null, integrationEvent.DeviceId, null, clock.GetUtcNow());
        db.Set<Notification>().Add(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await live.NotificationCreatedAsync(new NotificationChange(notification.Id, null, notification.Severity, notification.Title, null), cancellationToken);
    }
}

internal sealed class NotificationPublisher(NotificationFanOut fanOut, ILocationLookup locations) : INotificationPublisher
{
    public async Task LocationOutageAsync(Guid tenantId, Guid locationId, int devices, DateTimeOffset at, CancellationToken ct)
    {
        var location = (await locations.GetAsync([locationId], ct)).GetValueOrDefault(locationId)?.Name ?? "A location";
        await fanOut.SendAsync(tenantId, 3, "Connectivity", $"{location} is offline",
            $"All {devices} devices of {location} went offline at {at:u}.", locationId, null, null, ct);
    }
}

/// <summary>Sends due e-mail deliveries with retry (1, 2, 4, 8 minutes; 5 attempts). Runs in the system scope.</summary>
public sealed class NotificationDeliveryService(
    IAppDbContext db, IUnitOfWork unitOfWork, IEmailSender email, Abstractions.Storage.IWebhookSender webhooks, Abstractions.Storage.ISecretProtector protector, TimeProvider clock)
{
    public const int BatchSize = 50;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Set<NotificationDelivery>()
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        foreach (var delivery in due)
        {
            try
            {
                if (delivery.Channel == "Webhook")
                {
                    var secret = await db.Set<AlertChannelSettings>().AsNoTracking().Where(s => s.TenantId == delivery.TenantId).Select(s => s.WebhookSecretProtected).SingleOrDefaultAsync(ct);
                    var result = await webhooks.SendAsync(delivery.Recipient, secret is null ? null : protector.Unprotect(secret), delivery.Body, ct);
                    if (!result.Success)
                        throw new InvalidOperationException(result.Error ?? $"HTTP {result.StatusCode}");
                }
                else
                {
                    await email.SendAsync(new EmailMessage(delivery.Recipient, delivery.Subject, delivery.Body), ct);
                }

                delivery.Sent(clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                delivery.Failed($"{ex.GetType().Name}: {ex.Message}", clock.GetUtcNow());
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return due.Count;
    }
}
