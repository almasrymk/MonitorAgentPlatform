using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Notifications;

public enum RecipientEvents
{
    All,
    CriticalOnly,
    WarningsAndCritical,
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>Channel switches of a tenant (Settings > Alert Settings; 02 section 7). Defaults: e-mail and in-app on.</summary>
public sealed class AlertChannelSettings : Entity, ITenantOwned
{
    private AlertChannelSettings()
    {
    }

    public Guid TenantId { get; private set; }
    public bool EmailEnabled { get; private set; }
    public bool SmsEnabled { get; private set; }
    public bool InAppEnabled { get; private set; }
    public bool WebhookEnabled { get; private set; }
    public string? WebhookUrl { get; private set; }

    /// <summary>The webhook signing secret, encrypted with Data Protection (never returned by the API).</summary>
    public string? WebhookSecretProtected { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetWebhookSecret(string? protectedSecret, DateTimeOffset now)
    {
        WebhookSecretProtected = Guard.MaxLength(protectedSecret, nameof(WebhookSecretProtected), 4000);
        UpdatedAt = now;
    }

    public static AlertChannelSettings Default(Guid tenantId, DateTimeOffset now) =>
        new() { Id = tenantId, TenantId = tenantId, EmailEnabled = true, InAppEnabled = true, UpdatedAt = now };

    /// <summary>SMS is not available yet and stays off (07 section 5.8).</summary>
    public void Update(bool email, bool inApp, bool webhook, string? webhookUrl, DateTimeOffset now)
    {
        Guard.Against(webhook && !Uri.TryCreate(webhookUrl, UriKind.Absolute, out _), Error.Validation(Guard.ValidationCode, "A webhook needs an absolute https URL."));
        if (webhook)
            Guard.Against(!webhookUrl!.StartsWith("https://", StringComparison.OrdinalIgnoreCase), Error.Validation(Guard.ValidationCode, "A webhook needs an absolute https URL."));
        EmailEnabled = email;
        InAppEnabled = inApp;
        WebhookEnabled = webhook;
        WebhookUrl = Guard.MaxLength(webhook ? webhookUrl : null, nameof(WebhookUrl), 500);
        SmsEnabled = false;
        UpdatedAt = now;
    }
}

/// <summary>Who gets alert e-mails (02 section 7).</summary>
public sealed class AlertRecipient : AggregateRoot, ITenantOwned
{
    private AlertRecipient()
    {
        Name = string.Empty;
        Email = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Email { get; private set; }
    public RecipientEvents Events { get; private set; }
    public Guid? LocationId { get; private set; }
    public bool IsActive { get; private set; }

    public static AlertRecipient Create(Guid tenantId, string name, string email, RecipientEvents events, Guid? locationId) =>
        new()
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            Name = Guard.NotEmpty(name, nameof(Name), 200),
            Email = Guard.NotEmpty(email, nameof(Email), 256).ToLowerInvariant(),
            Events = events,
            LocationId = locationId,
            IsActive = true,
        };

    public void Update(string name, string email, RecipientEvents events, Guid? locationId, bool isActive)
    {
        Name = Guard.NotEmpty(name, nameof(Name), 200);
        Email = Guard.NotEmpty(email, nameof(Email), 256).ToLowerInvariant();
        Events = events;
        LocationId = locationId;
        IsActive = isActive;
    }

    /// <summary>Whether an alert of this severity (1 Info, 2 Warning, 3 Critical) and location reaches the recipient.</summary>
    public bool Wants(byte severity, Guid locationId) =>
        IsActive && (LocationId is null || LocationId == locationId) && Events switch
        {
            RecipientEvents.CriticalOnly => severity >= 3,
            RecipientEvents.WarningsAndCritical => severity >= 2,
            _ => true,
        };
}

/// <summary>An in-app notification (02 section 7). <see cref="TenantId"/> null = the platform feed.</summary>
public sealed class Notification : Entity, IOptionallyTenantOwned
{
    private Notification()
    {
        Severity = "Info";
        Category = "System";
        Title = string.Empty;
        Body = string.Empty;
    }

    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Severity { get; private set; }
    public string Category { get; private set; }
    public string Title { get; private set; }
    public string Body { get; private set; }
    public Guid? LocationId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public Guid? AlertId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(Guid? tenantId, string severity, string category, string title, string? body, Guid? locationId, Guid? deviceId, Guid? alertId, DateTimeOffset now, Guid? userId = null) =>
        new()
        {
            TenantId = tenantId,
            UserId = userId,
            Severity = Guard.NotEmpty(severity, nameof(Severity), 8),
            Category = Guard.NotEmpty(category, nameof(Category), 16),
            Title = Truncate(title, 200),
            Body = Truncate(body ?? string.Empty, 1000),
            LocationId = locationId,
            DeviceId = deviceId,
            AlertId = alertId,
            CreatedAt = now,
        };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>A notification a user has read (02 section 7).</summary>
public sealed class NotificationRead : Entity
{
    private NotificationRead()
    {
    }

    public Guid NotificationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset ReadAt { get; private set; }

    public static NotificationRead Create(Guid notificationId, Guid userId, DateTimeOffset at) => new() { NotificationId = notificationId, UserId = userId, ReadAt = at };
}

/// <summary>One e-mail (or webhook) delivery of an alert, with retries (02 section 7).</summary>
public sealed class NotificationDelivery : Entity, ITenantOwned
{
    public const int MaxAttempts = 5;

    private NotificationDelivery()
    {
        Channel = "Email";
        Recipient = string.Empty;
        Subject = string.Empty;
        Body = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public Guid? AlertId { get; private set; }
    public string Channel { get; private set; }
    public string Recipient { get; private set; }
    public string Subject { get; private set; }
    public string Body { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public static NotificationDelivery Email(Guid tenantId, Guid? alertId, string recipient, string subject, string body, DateTimeOffset now) =>
        new()
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            AlertId = alertId,
            Channel = "Email",
            Recipient = Guard.NotEmpty(recipient, nameof(Recipient), 256),
            Subject = subject.Length <= 200 ? subject : subject[..200],
            Body = body.Length <= 4000 ? body : body[..4000],
            Status = DeliveryStatus.Pending,
            NextAttemptAt = now,
        };

    /// <summary>A webhook post of an alert: <see cref="Recipient"/> is the URL, <see cref="Body"/> the JSON payload.</summary>
    public static NotificationDelivery Webhook(Guid tenantId, Guid? alertId, string url, string subject, string json, DateTimeOffset now) =>
        new()
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            AlertId = alertId,
            Channel = "Webhook",
            Recipient = Guard.NotEmpty(url, nameof(Recipient), 256),
            Subject = subject.Length <= 200 ? subject : subject[..200],
            Body = json.Length <= 4000 ? json : json[..4000],
            Status = DeliveryStatus.Pending,
            NextAttemptAt = now,
        };

    public void Sent(DateTimeOffset now)
    {
        Attempts++;
        Status = DeliveryStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    /// <summary>Retries with back-off (1, 2, 4, 8 minutes); after <see cref="MaxAttempts"/> the delivery is Failed.</summary>
    public void Failed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length <= 500 ? error : error[..500];
        if (Attempts >= MaxAttempts)
        {
            Status = DeliveryStatus.Failed;
            return;
        }

        NextAttemptAt = now.AddMinutes(Math.Pow(2, Attempts - 1));
    }
}
