using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Tenancy;

/// <summary>Platform settings (06: <c>/platform/settings</c>): branding, default offline-alert delay, retention, e-mail sender. One row.</summary>
public sealed class PlatformSettings : Entity
{
    public static readonly Guid SingletonId = new("00000000-0000-7000-8000-000000000001");

    private PlatformSettings()
    {
        BrandName = "Monitor Agent Platform";
        EmailSenderName = "Monitor Cloud";
        EmailSenderAddress = "no-reply@monitor.local";
    }

    public string BrandName { get; private set; }
    public int OfflineAlertDelayMinutes { get; private set; } = 2;
    public int MinuteRetentionDays { get; private set; } = 30;
    public int HourRetentionDays { get; private set; } = 400;
    public string EmailSenderName { get; private set; }
    public string EmailSenderAddress { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PlatformSettings Default() => new() { Id = SingletonId };

    public void Update(string brandName, int offlineAlertDelayMinutes, int minuteRetentionDays, int hourRetentionDays, string emailSenderName, string emailSenderAddress, DateTimeOffset now)
    {
        BrandName = Guard.NotEmpty(brandName, nameof(BrandName), 100);
        OfflineAlertDelayMinutes = Guard.InRange(offlineAlertDelayMinutes, nameof(OfflineAlertDelayMinutes), 1, 60);
        MinuteRetentionDays = Guard.InRange(minuteRetentionDays, nameof(MinuteRetentionDays), 7, 90);
        HourRetentionDays = Guard.InRange(hourRetentionDays, nameof(HourRetentionDays), 90, 1100);
        EmailSenderName = Guard.NotEmpty(emailSenderName, nameof(EmailSenderName), 100);
        EmailSenderAddress = Guard.NotEmpty(emailSenderAddress, nameof(EmailSenderAddress), 256);
        UpdatedAt = now;
    }
}
